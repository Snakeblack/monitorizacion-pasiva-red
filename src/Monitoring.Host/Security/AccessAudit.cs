using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Monitoring.Domain.Security;
using Monitoring.Persistence;
using Npgsql;

namespace Monitoring.Host.Security;

public interface IAccessAudit
{
    Task RecordDenialAsync(string? subject, Operation operation, string cause, string correlationId, CancellationToken cancellationToken);
}

// Records a denial in PostgreSQL. A failure to record never turns the denial into access: it is logged by type only.
public sealed class PostgresAccessAudit(MonitoringDbContext dbContext, ILogger<PostgresAccessAudit> logger) : IAccessAudit
{
    public async Task RecordDenialAsync(string? subject, Operation operation, string cause, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                await using var command = new NpgsqlCommand(
                    "INSERT INTO monitoring.access_denial_audit(subject,operation,cause,correlation_id) VALUES (@subject,@operation,@cause,@correlation)", connection);
                command.Parameters.AddWithValue("subject", NpgsqlTypes.NpgsqlDbType.Text, (object?)Truncate(subject, 256) ?? DBNull.Value);
                command.Parameters.AddWithValue("operation", operation.ToString());
                command.Parameters.AddWithValue("cause", Truncate(cause, 64)!);
                command.Parameters.AddWithValue("correlation", Truncate(correlationId, 128)!);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            finally { await dbContext.Database.CloseConnectionAsync(); }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Access denial could not be recorded ({FailureType}).", exception.GetType().Name);
        }
    }

    private static string? Truncate(string? value, int length) => value is null || value.Length <= length ? value : value[..length];
}

// Without a database the denial is still observable, as a structured log line with the same minimal fields.
public sealed class LoggingAccessAudit(ILogger<LoggingAccessAudit> logger) : IAccessAudit
{
    public Task RecordDenialAsync(string? subject, Operation operation, string cause, string correlationId, CancellationToken cancellationToken)
    {
        logger.LogInformation("Access denied: subject {Subject}, operation {Operation}, cause {Cause}, correlation {Correlation}.",
            subject ?? "unknown", operation, cause, correlationId);
        return Task.CompletedTask;
    }
}

// Every human operation passes through here: authenticate, authorize and, when refused, record the denial.
public sealed class AccessGate(IAccessProvider provider, IAccessAudit audit)
{
    public async Task<AccessDecision> AuthorizeAsync(HttpContext context, Operation operation)
    {
        var decision = await provider.AuthorizeAsync(context, operation, context.RequestAborted);
        if (decision.Outcome != AccessOutcome.Granted)
            await audit.RecordDenialAsync(decision.Subject, operation, decision.Cause ?? "unspecified", Correlation(context), context.RequestAborted);
        return decision;
    }

    // A refusal after a successful grant, such as a scope the token does not hold.
    public Task DenyAsync(HttpContext context, AccessDecision granted, Operation operation, string cause) =>
        audit.RecordDenialAsync(granted.Subject, operation, cause, Correlation(context), context.RequestAborted);

    private static string Correlation(HttpContext context) => Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
}
