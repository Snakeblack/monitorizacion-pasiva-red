using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Monitoring.Host.Sessions;
using Monitoring.Persistence;
using Monitoring.Persistence.Sessions;
using Npgsql;

namespace Monitoring.Tests;

public sealed class SessionWorkerTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task TransientFailureDisposesScopeAndRetryRecoversWithNewContextAndSafeLogs()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await SessionTestDatabase.AcceptAsync(connection, "event");
        await SessionTestDatabase.InstallFailureTriggerAsync(connection, PostgresErrorCodes.SerializationFailure);
        var contexts = new ConcurrentQueue<MonitoringDbContext>();
        var logger = new RecordingLogger();
        using var host = CreateWorkerHost(connection, contexts, logger);
        await host.StartAsync();
        await WaitAsync(() => Task.FromResult(!logger.Messages.IsEmpty));
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_projection"));
        await SessionTestDatabase.ExecuteAsync(connection, "DROP TRIGGER fail_mark ON monitoring.ingestion_inbox");
        await WaitAsync(async () => await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.ingestion_inbox WHERE processed_at IS NOT NULL") == 1);
        Assert.True(contexts.Count >= 2, "Retry must resolve a new scoped context.");
        Assert.True(contexts.TryPeek(out var failedContext));
        Assert.Throws<ObjectDisposedException>(() => _ = failedContext!.Model);
        Assert.All(logger.Messages, message =>
        {
            Assert.DoesNotContain("sensitive-test-payload", message, StringComparison.Ordinal);
            Assert.DoesNotContain("192.0.2.1", message, StringComparison.Ordinal);
            Assert.DoesNotContain("local-test-password", message, StringComparison.Ordinal);
        });
        await host.StopAsync();
        using var restarted = CreateWorkerHost(connection, new(), new());
        await restarted.StartAsync();
        await restarted.StopAsync();
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_projection"));
    }

    [Fact]
    public async Task CancellationDuringMarkingReleasesTransactionAndRestartRecovers()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await SessionTestDatabase.AcceptAsync(connection, "event");
        await SessionTestDatabase.ExecuteAsync(connection, """
            CREATE FUNCTION monitoring.wait_mark() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN PERFORM pg_sleep(30); RETURN NEW; END $$;
            CREATE TRIGGER wait_mark BEFORE UPDATE OF processed_at ON monitoring.ingestion_inbox
            FOR EACH ROW EXECUTE FUNCTION monitoring.wait_mark();
            """);
        using var host = CreateWorkerHost(connection, new(), new());
        await host.StartAsync();
        await WaitAsync(async () => await SessionTestDatabase.ScalarAsync(connection, """
            SELECT count(*) FROM pg_stat_activity WHERE datname=current_database()
            AND wait_event='PgSleep' AND query LIKE '%UPDATE monitoring.ingestion_inbox%'
            """) == 1);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StopAsync(timeout.Token);
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_projection"));
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.ingestion_inbox WHERE processed_at IS NULL"));
        // DDL would block if the cancelled worker had retained its row/table lock.
        await SessionTestDatabase.ExecuteAsync(connection, "SET lock_timeout='2s'; DROP TRIGGER wait_mark ON monitoring.ingestion_inbox");
        using var restarted = CreateWorkerHost(connection, new(), new());
        await restarted.StartAsync();
        await WaitAsync(async () => await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_projection") == 1);
        await restarted.StopAsync(timeout.Token);
        Assert.Equal(1L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.ingestion_inbox WHERE processed_at IS NOT NULL"));
    }

    [Fact]
    public async Task NonTransientFailureStopsHostAndNeverLogsPostgresPayload()
    {
        var connection = await SessionTestDatabase.CreateAsync(postgres);
        await SessionTestDatabase.AcceptAsync(connection, "event");
        await SessionTestDatabase.InstallFailureTriggerAsync(connection, "P0001");
        var logger = new RecordingLogger();
        using var host = CreateWorkerHost(connection, new(), logger);
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        await host.StartAsync();
        await WaitAsync(() => Task.FromResult(lifetime.ApplicationStopping.IsCancellationRequested));
        Assert.Equal(0L, await SessionTestDatabase.ScalarAsync(connection, "SELECT count(*) FROM monitoring.session_projection"));
        Assert.NotEmpty(logger.Messages);
        Assert.All(logger.Messages, message => Assert.DoesNotContain("sensitive-test-payload", message, StringComparison.Ordinal));
        await host.StopAsync();
    }

    private static IHost CreateWorkerHost(string connection, ConcurrentQueue<MonitoringDbContext> contexts, RecordingLogger logger) =>
        new HostBuilder().ConfigureServices(services =>
        {
            services.AddScoped(_ =>
            {
                var context = new MonitoringDbContext(new DbContextOptionsBuilder<MonitoringDbContext>().UseNpgsql(connection).Options);
                contexts.Enqueue(context);
                return context;
            });
            services.AddScoped<SessionProjector>();
            services.AddSingleton<ILogger<SessionProjectionWorker>>(logger);
            services.AddHostedService<SessionProjectionWorker>();
        }).Build();

    internal static async Task WaitAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!await condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    private sealed class RecordingLogger : ILogger<SessionProjectionWorker>
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Enqueue(formatter(state, exception) + (exception?.ToString() ?? ""));
    }
}
