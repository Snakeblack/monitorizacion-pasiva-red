using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using Monitoring.Persistence;

namespace Monitoring.Host.Security;

// `--probes register <site> <sensor> --certificate FILE --actor A --reason R` and `--probes disable --certificate FILE --actor A --reason R`.
// The binding key is read from the certificate itself (issuer hash + serial), never typed by hand. Exit codes: 0 done, 2 not applicable
// (already registered/disabled, unknown, bound to another identity), 1 invalid usage or failure. Only public certificate files are read.
public static class ProbesCommand
{
    private const string Usage = "Usage: --probes register <site> <sensor> --certificate FILE --actor A --reason R | disable --certificate FILE --actor A --reason R";

    public static async Task<int> RunAsync(string[] args, IConfiguration configuration, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("Monitoring");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            await error.WriteLineAsync("Probes command failed: ConnectionStrings:Monitoring is required.");
            return 1;
        }
        var positional = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index].StartsWith("--", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length) return await UsageAsync(error);
                options[args[index]] = args[++index];
            }
            else positional.Add(args[index]);
        }
        try
        {
            if (!options.TryGetValue("--certificate", out var file) || !options.TryGetValue("--actor", out var actor) || !options.TryGetValue("--reason", out var reason))
                return await UsageAsync(error);
            using var certificate = X509CertificateLoader.LoadCertificateFromFile(file);
            var issuer = ProbeCertificateValidator.IssuerKey(certificate);
            var serial = CrlParser.Normalize(certificate.SerialNumberBytes.Span);
            await using var db = new MonitoringDbContext(new DbContextOptionsBuilder<MonitoringDbContext>().UseNpgsql(connectionString).Options);
            var registry = new ProbeRegistry(db);
            switch (positional.FirstOrDefault())
            {
                case "register" when positional.Count == 3:
                    var registration = await registry.RegisterAsync(positional[1], positional[2], issuer, serial, actor, reason, cancellationToken);
                    await output.WriteLineAsync(registration switch
                    {
                        ProbeRegistration.Registered => "registered",
                        ProbeRegistration.AlreadyRegistered => "already-registered",
                        _ => "conflict"
                    });
                    return registration == ProbeRegistration.Registered ? 0 : 2;
                case "disable" when positional.Count == 1:
                    var disabling = await registry.DisableAsync(issuer, serial, actor, reason, cancellationToken);
                    await output.WriteLineAsync(disabling switch
                    {
                        ProbeDisabling.Disabled => "disabled",
                        ProbeDisabling.AlreadyDisabled => "already-disabled",
                        _ => "not-registered"
                    });
                    return disabling == ProbeDisabling.Disabled ? 0 : 2;
                default:
                    return await UsageAsync(error);
            }
        }
        catch (ArgumentException exception)
        {
            await error.WriteLineAsync(exception.Message);
            return 1;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await error.WriteLineAsync($"Probes command failed ({exception.GetType().Name}).");
            return 1;
        }
    }

    private static async Task<int> UsageAsync(TextWriter error)
    {
        await error.WriteLineAsync(Usage);
        return 1;
    }
}
