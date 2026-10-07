using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Monitoring.Domain.Ingestion;
using Monitoring.Persistence;
using Npgsql;

namespace Monitoring.Host.Security;

public enum ProbeRegistration { Registered, AlreadyRegistered, Conflict }

public enum ProbeDisabling { Disabled, AlreadyDisabled, NotFound }

// PostgreSQL-backed binding of probe certificates to site/sensor identities. Registration and disabling are the only writes; both
// carry an actor and a reason and are audited in the same transaction. Disabling survives restarts because it lives in the database.
public sealed partial class ProbeRegistry(MonitoringDbContext dbContext) : IProbeRegistry
{
    private NpgsqlConnection Connection => (NpgsqlConnection)dbContext.Database.GetDbConnection();

    [GeneratedRegex("^[0-9A-F]{64}$")]
    private static partial Regex IssuerPattern();

    [GeneratedRegex("^[0-9A-Fa-f]{1,80}$")]
    private static partial Regex SerialPattern();

    public async Task<ProbeBinding?> FindAsync(string issuerSha256, string serialHex, CancellationToken cancellationToken)
    {
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand("SELECT site_id,sensor_id,active FROM monitoring.probe_registry WHERE issuer_sha256=@issuer AND serial_hex=@serial", Connection);
            command.Parameters.AddWithValue("issuer", issuerSha256);
            command.Parameters.AddWithValue("serial", serialHex);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? new ProbeBinding(reader.GetString(0), reader.GetString(1), reader.GetBoolean(2)) : null;
        }
        finally { await dbContext.Database.CloseConnectionAsync(); }
    }

    public async Task<ProbeRegistration> RegisterAsync(string siteId, string sensorId, string issuerSha256, string serialHex, string actor, string reason, CancellationToken cancellationToken)
    {
        Validate(siteId, sensorId, issuerSha256, serialHex, actor, reason);
        serialHex = serialHex.ToUpperInvariant();
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var transaction = await Connection.BeginTransactionAsync(cancellationToken);
            await using var insert = new NpgsqlCommand("""
                INSERT INTO monitoring.probe_registry(issuer_sha256,serial_hex,site_id,sensor_id) VALUES (@issuer,@serial,@site,@sensor)
                ON CONFLICT (issuer_sha256,serial_hex) DO NOTHING
                """, Connection, transaction);
            Bind(insert, issuerSha256, serialHex);
            insert.Parameters.AddWithValue("site", siteId);
            insert.Parameters.AddWithValue("sensor", sensorId);
            if (await insert.ExecuteNonQueryAsync(cancellationToken) == 1)
            {
                await AuditAsync(transaction, "registered", issuerSha256, serialHex, siteId, sensorId, actor, reason, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return ProbeRegistration.Registered;
            }
            await using var existing = new NpgsqlCommand(
                "SELECT site_id=@site AND sensor_id=@sensor AND active FROM monitoring.probe_registry WHERE issuer_sha256=@issuer AND serial_hex=@serial", Connection, transaction);
            Bind(existing, issuerSha256, serialHex);
            existing.Parameters.AddWithValue("site", siteId);
            existing.Parameters.AddWithValue("sensor", sensorId);
            return (bool)(await existing.ExecuteScalarAsync(cancellationToken))! ? ProbeRegistration.AlreadyRegistered : ProbeRegistration.Conflict;
        }
        finally { await dbContext.Database.CloseConnectionAsync(); }
    }

    public async Task<ProbeDisabling> DisableAsync(string issuerSha256, string serialHex, string actor, string reason, CancellationToken cancellationToken)
    {
        Validate("-", "-", issuerSha256, serialHex, actor, reason);
        serialHex = serialHex.ToUpperInvariant();
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var transaction = await Connection.BeginTransactionAsync(cancellationToken);
            await using var select = new NpgsqlCommand(
                "SELECT site_id,sensor_id,active FROM monitoring.probe_registry WHERE issuer_sha256=@issuer AND serial_hex=@serial FOR UPDATE", Connection, transaction);
            Bind(select, issuerSha256, serialHex);
            string site, sensor;
            await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken)) return ProbeDisabling.NotFound;
                if (!reader.GetBoolean(2)) return ProbeDisabling.AlreadyDisabled;
                (site, sensor) = (reader.GetString(0), reader.GetString(1));
            }
            await using var update = new NpgsqlCommand(
                "UPDATE monitoring.probe_registry SET active=false, disabled_at=clock_timestamp() WHERE issuer_sha256=@issuer AND serial_hex=@serial", Connection, transaction);
            Bind(update, issuerSha256, serialHex);
            await update.ExecuteNonQueryAsync(cancellationToken);
            await AuditAsync(transaction, "disabled", issuerSha256, serialHex, site, sensor, actor, reason, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ProbeDisabling.Disabled;
        }
        finally { await dbContext.Database.CloseConnectionAsync(); }
    }

    private async Task AuditAsync(NpgsqlTransaction transaction, string action, string issuer, string serial, string site, string sensor, string actor, string reason, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO monitoring.probe_registry_audit(action,issuer_sha256,serial_hex,site_id,sensor_id,actor,reason) VALUES (@action,@issuer,@serial,@site,@sensor,@actor,@reason)
            """, Connection, transaction);
        Bind(command, issuer, serial);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("site", site);
        command.Parameters.AddWithValue("sensor", sensor);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("reason", reason);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void Bind(NpgsqlCommand command, string issuer, string serial)
    {
        command.Parameters.AddWithValue("issuer", issuer);
        command.Parameters.AddWithValue("serial", serial);
    }

    private static void Validate(string siteId, string sensorId, string issuerSha256, string serialHex, string actor, string reason)
    {
        if (string.IsNullOrWhiteSpace(siteId) || siteId.Length > BatchContract.MaximumIdentifierLength) throw new ArgumentException("A valid site is required.", nameof(siteId));
        if (string.IsNullOrWhiteSpace(sensorId) || sensorId.Length > BatchContract.MaximumIdentifierLength) throw new ArgumentException("A valid sensor is required.", nameof(sensorId));
        if (!IssuerPattern().IsMatch(issuerSha256)) throw new ArgumentException("The issuer must be an upper-case SHA-256 hex digest.", nameof(issuerSha256));
        if (!SerialPattern().IsMatch(serialHex)) throw new ArgumentException("The serial must be hexadecimal.", nameof(serialHex));
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 128) throw new ArgumentException("An actor is required.", nameof(actor));
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 512) throw new ArgumentException("A reason is required.", nameof(reason));
    }
}
