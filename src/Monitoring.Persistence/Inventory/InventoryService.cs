using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Monitoring.Domain.Inventory;
using Monitoring.Domain.Sessions.Search;
using Npgsql;
using NpgsqlTypes;

namespace Monitoring.Persistence.Inventory;

// The one place the manual inventory rules live, shared by the API and any worker. Every change locks the affected rows in a fixed
// order (candidate, then device), checks every affected scope and revision, and commits together with its audit record: a failed audit
// reverts the change. Observations and candidates are never deleted or rewritten by a decision.
public sealed class InventoryService(MonitoringDbContext dbContext)
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);
    private NpgsqlConnection Connection => (NpgsqlConnection)dbContext.Database.GetDbConnection();

    public async Task<InventoryResult<DeviceView>> CreateDeviceAsync(InventoryActor actor, string name, string description, AuthorizedPair scope,
        CancellationToken cancellationToken)
    {
        if (!ValidNaming(name, description)) return InventoryResult<DeviceView>.Failure(InventoryStatus.Invalid, "invalid-name-or-description");
        if (!Holds(actor, scope)) return InventoryResult<DeviceView>.Failure(InventoryStatus.Forbidden, "scope-not-authorized");
        return await InTransactionAsync(async transaction =>
        {
            var deviceId = await InsertDeviceAsync(name, description, transaction, cancellationToken);
            await AddScopeAsync(deviceId, scope, transaction, cancellationToken);
            await AuditAsync(transaction, actor, "device-created", deviceId, null, null, 1, null, null,
                new JsonObject { ["name"] = new JsonObject { ["to"] = name }, ["description"] = new JsonObject { ["to"] = description } }, null, cancellationToken);
            return InventoryResult<DeviceView>.Success((await ReadDeviceAsync(deviceId, transaction, cancellationToken))!);
        }, cancellationToken);
    }

    public async Task<InventoryResult<DeviceView>> UpdateDeviceAsync(InventoryActor actor, Guid deviceId, long expectedRevision, string name, string description,
        CancellationToken cancellationToken)
    {
        if (!ValidNaming(name, description)) return InventoryResult<DeviceView>.Failure(InventoryStatus.Invalid, "invalid-name-or-description");
        return await InTransactionAsync(async transaction =>
        {
            var current = await LockDeviceAsync(deviceId, transaction, cancellationToken);
            if (current is null) return InventoryResult<DeviceView>.Failure(InventoryStatus.NotFound, "device-not-found");
            if (!HoldsAll(actor, current.Scopes)) return InventoryResult<DeviceView>.Failure(InventoryStatus.Forbidden, "scope-not-authorized");
            if (current.Revision != expectedRevision) return InventoryResult<DeviceView>.Failure(InventoryStatus.Conflict, "stale-device-revision");
            var changes = new JsonObject();
            if (current.Name != name) changes["name"] = new JsonObject { ["from"] = current.Name, ["to"] = name };
            if (current.Description != description) changes["description"] = new JsonObject { ["from"] = current.Description, ["to"] = description };
            await using (var update = Command("UPDATE monitoring.device SET name=@name,description=@description,revision=revision+1,updated_at=clock_timestamp() WHERE device_id=@id", transaction))
            {
                update.Parameters.AddWithValue("name", name);
                update.Parameters.AddWithValue("description", description);
                update.Parameters.AddWithValue("id", deviceId);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }
            await AuditAsync(transaction, actor, "device-updated", deviceId, null, current.Revision, current.Revision + 1, null, null, changes, null, cancellationToken);
            return InventoryResult<DeviceView>.Success((await ReadDeviceAsync(deviceId, transaction, cancellationToken))!);
        }, cancellationToken);
    }

    // Confirms a candidate as a new device. Allowed from "candidate" and, as a deliberate later decision, from "rejected".
    public async Task<InventoryResult<CandidateView>> ConfirmCandidateAsync(InventoryActor actor, Guid candidateId, long expectedRevision,
        string deviceName, string deviceDescription, CancellationToken cancellationToken)
    {
        if (!ValidNaming(deviceName, deviceDescription)) return InventoryResult<CandidateView>.Failure(InventoryStatus.Invalid, "invalid-name-or-description");
        return await InTransactionAsync(async transaction =>
        {
            var candidate = await LockCandidateAsync(candidateId, transaction, cancellationToken);
            var refusal = Check(actor, candidate, expectedRevision, allowedStates: ["candidate", "rejected"]);
            if (refusal is not null) return refusal;
            var deviceId = await InsertDeviceAsync(deviceName, deviceDescription, transaction, cancellationToken);
            await AddScopeAsync(deviceId, new AuthorizedPair(candidate!.SiteId, candidate.SensorId), transaction, cancellationToken);
            await LinkAsync(candidate, deviceId, transaction, cancellationToken);
            await AuditAsync(transaction, actor, "candidate-confirmed", deviceId, candidateId, null, 1, candidate.Revision, candidate.Revision + 1,
                new JsonObject { ["state"] = new JsonObject { ["from"] = candidate.State, ["to"] = "confirmed" } }, null, cancellationToken);
            return InventoryResult<CandidateView>.Success((await ReadCandidateAsync(candidateId, transaction, cancellationToken))!);
        }, cancellationToken);
    }

    public async Task<InventoryResult<CandidateView>> RejectCandidateAsync(InventoryActor actor, Guid candidateId, long expectedRevision, string reason,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > InventoryLimits.MaximumReasonLength)
            return InventoryResult<CandidateView>.Failure(InventoryStatus.Invalid, "invalid-reason");
        return await InTransactionAsync(async transaction =>
        {
            var candidate = await LockCandidateAsync(candidateId, transaction, cancellationToken);
            var refusal = Check(actor, candidate, expectedRevision, allowedStates: ["candidate"]);
            if (refusal is not null) return refusal;
            await using (var update = Command("UPDATE monitoring.device_candidate SET state='rejected',revision=revision+1 WHERE candidate_id=@id", transaction))
            {
                update.Parameters.AddWithValue("id", candidateId);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }
            await AuditAsync(transaction, actor, "candidate-rejected", null, candidateId, null, null, candidate!.Revision, candidate.Revision + 1,
                new JsonObject { ["state"] = new JsonObject { ["from"] = candidate.State, ["to"] = "rejected" } }, reason, cancellationToken);
            return InventoryResult<CandidateView>.Success((await ReadCandidateAsync(candidateId, transaction, cancellationToken))!);
        }, cancellationToken);
    }

    // Explicitly links a candidate to an existing device. Both revisions must be current and every affected scope authorized.
    public async Task<InventoryResult<CandidateView>> MergeCandidateAsync(InventoryActor actor, Guid candidateId, long expectedCandidateRevision,
        Guid deviceId, long expectedDeviceRevision, CancellationToken cancellationToken) =>
        await InTransactionAsync(async transaction =>
        {
            var candidate = await LockCandidateAsync(candidateId, transaction, cancellationToken);
            var device = await LockDeviceAsync(deviceId, transaction, cancellationToken);
            if (candidate is null || device is null) return InventoryResult<CandidateView>.Failure(InventoryStatus.NotFound, "not-found");
            if (!Holds(actor, new AuthorizedPair(candidate.SiteId, candidate.SensorId)) || !HoldsAll(actor, device.Scopes))
                return InventoryResult<CandidateView>.Failure(InventoryStatus.Forbidden, "scope-not-authorized");
            if (candidate.Revision != expectedCandidateRevision || device.Revision != expectedDeviceRevision)
                return InventoryResult<CandidateView>.Failure(InventoryStatus.Conflict, "stale-revision");
            if (candidate.DeviceId is not null || candidate.State is not ("candidate" or "rejected"))
                return InventoryResult<CandidateView>.Failure(InventoryStatus.Conflict, "candidate-already-linked");
            await AddScopeAsync(deviceId, new AuthorizedPair(candidate.SiteId, candidate.SensorId), transaction, cancellationToken);
            await LinkAsync(candidate, deviceId, transaction, cancellationToken);
            await using (var bump = Command("UPDATE monitoring.device SET revision=revision+1,updated_at=clock_timestamp() WHERE device_id=@id", transaction))
            {
                bump.Parameters.AddWithValue("id", deviceId);
                await bump.ExecuteNonQueryAsync(cancellationToken);
            }
            await AuditAsync(transaction, actor, "candidate-merged", deviceId, candidateId, device.Revision, device.Revision + 1, candidate.Revision, candidate.Revision + 1,
                new JsonObject { ["state"] = new JsonObject { ["from"] = candidate.State, ["to"] = "confirmed" } }, null, cancellationToken);
            return InventoryResult<CandidateView>.Success((await ReadCandidateAsync(candidateId, transaction, cancellationToken))!);
        }, cancellationToken);

    public async Task<InventoryPage<DeviceView>> ListDevicesAsync(IReadOnlyCollection<AuthorizedPair> scopes, int pageSize, string? after, CancellationToken cancellationToken)
    {
        ValidatePage(pageSize);
        var (sites, sensors) = ScopeArrays(scopes);
        return await ReadAsync(async connection =>
        {
            var ids = new List<Guid>();
            await using (var command = ReadCommand("""
                SELECT d.device_id FROM monitoring.device d
                WHERE (@after IS NULL OR d.device_id > @after) AND EXISTS (
                  SELECT 1 FROM monitoring.device_scope s JOIN unnest(@sites, @sensors) AS a(site_id, sensor_id)
                    ON s.site_id = a.site_id AND s.sensor_id = a.sensor_id WHERE s.device_id = d.device_id)
                ORDER BY d.device_id LIMIT @limit
                """, connection))
            {
                AddScopes(command, sites, sensors);
                command.Parameters.AddWithValue("after", NpgsqlDbType.Uuid, ParseCursor(after) is { } cursor ? cursor : DBNull.Value);
                command.Parameters.AddWithValue("limit", pageSize + 1);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken)) ids.Add(reader.GetGuid(0));
            }
            var hasMore = ids.Count > pageSize;
            var items = new List<DeviceView>();
            foreach (var id in ids.Take(pageSize)) items.Add((await ReadDeviceAsync(id, null, cancellationToken))!);
            return new InventoryPage<DeviceView>(items, hasMore ? items[^1].DeviceId.ToString("D") : null);
        }, cancellationToken);
    }

    public async Task<InventoryPage<CandidateView>> ListCandidatesAsync(IReadOnlyCollection<AuthorizedPair> scopes, string? state, int pageSize, string? after,
        CancellationToken cancellationToken)
    {
        ValidatePage(pageSize);
        if (state is not null && state is not ("candidate" or "confirmed" or "rejected")) throw new ArgumentException("Unknown candidate state.", nameof(state));
        var (sites, sensors) = ScopeArrays(scopes);
        return await ReadAsync(async connection =>
        {
            var ids = new List<Guid>();
            await using (var command = ReadCommand("""
                SELECT c.candidate_id FROM monitoring.device_candidate c
                WHERE (@after IS NULL OR c.candidate_id > @after) AND (@state IS NULL OR c.state = @state)
                  AND EXISTS (SELECT 1 FROM unnest(@sites, @sensors) AS a(site_id, sensor_id) WHERE a.site_id = c.site_id AND a.sensor_id = c.sensor_id)
                ORDER BY c.candidate_id LIMIT @limit
                """, connection))
            {
                AddScopes(command, sites, sensors);
                command.Parameters.AddWithValue("after", NpgsqlDbType.Uuid, ParseCursor(after) is { } cursor ? cursor : DBNull.Value);
                command.Parameters.AddWithValue("state", NpgsqlDbType.Text, (object?)state ?? DBNull.Value);
                command.Parameters.AddWithValue("limit", pageSize + 1);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken)) ids.Add(reader.GetGuid(0));
            }
            var hasMore = ids.Count > pageSize;
            var items = new List<CandidateView>();
            foreach (var id in ids.Take(pageSize)) items.Add((await ReadCandidateAsync(id, null, cancellationToken))!);
            return new InventoryPage<CandidateView>(items, hasMore ? items[^1].CandidateId.ToString("D") : null);
        }, cancellationToken);
    }

    public async Task<InventoryPage<ObservationView>> ListObservationsAsync(IReadOnlyCollection<AuthorizedPair> scopes, int pageSize, string? after,
        CancellationToken cancellationToken)
    {
        ValidatePage(pageSize);
        var (sites, sensors) = ScopeArrays(scopes);
        return await ReadAsync(async connection =>
        {
            DateTimeOffset? afterTime = null;
            string? afterSite = null, afterSensor = null, afterEvent = null;
            if (after is not null)
            {
                try
                {
                    var position = JsonSerializer.Deserialize<string[]>(Convert.FromBase64String(after));
                    if (position is not { Length: 4 }) throw new ArgumentException("Invalid position.", nameof(after));
                    (afterTime, afterSite, afterSensor, afterEvent) = (DateTimeOffset.Parse(position[0], System.Globalization.CultureInfo.InvariantCulture), position[1], position[2], position[3]);
                }
                catch (Exception exception) when (exception is FormatException or JsonException) { throw new ArgumentException("Invalid position.", nameof(after)); }
            }
            var items = new List<ObservationView>();
            await using var command = ReadCommand("""
                SELECT o.site_id,o.sensor_id,o.event_id,o.observed_at,host(o.ip),o.mac::text,o.vlan_id FROM monitoring.device_observation o
                WHERE EXISTS (SELECT 1 FROM unnest(@sites, @sensors) AS a(site_id, sensor_id) WHERE a.site_id = o.site_id AND a.sensor_id = o.sensor_id)
                  AND (@afterTime::timestamptz IS NULL OR (o.observed_at, o.site_id, o.sensor_id, o.event_id) < (@afterTime, @afterSite, @afterSensor, @afterEvent))
                ORDER BY o.observed_at DESC, o.site_id DESC, o.sensor_id DESC, o.event_id DESC LIMIT @limit
                """, connection);
            AddScopes(command, sites, sensors);
            command.Parameters.AddWithValue("afterTime", NpgsqlDbType.TimestampTz, (object?)afterTime ?? DBNull.Value);
            command.Parameters.AddWithValue("afterSite", NpgsqlDbType.Text, (object?)afterSite ?? DBNull.Value);
            command.Parameters.AddWithValue("afterSensor", NpgsqlDbType.Text, (object?)afterSensor ?? DBNull.Value);
            command.Parameters.AddWithValue("afterEvent", NpgsqlDbType.Text, (object?)afterEvent ?? DBNull.Value);
            command.Parameters.AddWithValue("limit", pageSize + 1);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                items.Add(new ObservationView(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3),
                    reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetInt32(6)));
            var hasMore = items.Count > pageSize;
            var page = items.Take(pageSize).ToList();
            var next = hasMore
                ? Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new[] { page[^1].ObservedAt.ToString("O"), page[^1].SiteId, page[^1].SensorId, page[^1].EventId }))
                : null;
            return new InventoryPage<ObservationView>(page, next);
        }, cancellationToken);
    }

    // ---- rules ----

    private static InventoryResult<CandidateView>? Check(InventoryActor actor, CandidateRow? candidate, long expectedRevision, string[] allowedStates)
    {
        if (candidate is null) return InventoryResult<CandidateView>.Failure(InventoryStatus.NotFound, "candidate-not-found");
        if (!Holds(actor, new AuthorizedPair(candidate.SiteId, candidate.SensorId))) return InventoryResult<CandidateView>.Failure(InventoryStatus.Forbidden, "scope-not-authorized");
        if (candidate.Revision != expectedRevision) return InventoryResult<CandidateView>.Failure(InventoryStatus.Conflict, "stale-candidate-revision");
        if (candidate.DeviceId is not null || !allowedStates.Contains(candidate.State)) return InventoryResult<CandidateView>.Failure(InventoryStatus.Conflict, "candidate-state");
        return null;
    }

    private static bool ValidNaming(string name, string description) => !string.IsNullOrWhiteSpace(name) && name.Length <= InventoryLimits.MaximumNameLength
        && description is not null && description.Length <= InventoryLimits.MaximumDescriptionLength;

    private static bool Holds(InventoryActor actor, AuthorizedPair scope) => actor.Scopes.Contains(scope);
    private static bool HoldsAll(InventoryActor actor, IEnumerable<AuthorizedPair> scopes) => scopes.All(scope => Holds(actor, scope));

    private static void ValidatePage(int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, InventoryLimits.MaximumPageSize);
    }

    private static Guid? ParseCursor(string? after) => after is null ? null : Guid.TryParseExact(after, "D", out var id) ? id : throw new ArgumentException("Invalid position.", nameof(after));

    private static (string[] Sites, string[] Sensors) ScopeArrays(IReadOnlyCollection<AuthorizedPair> scopes) =>
        (scopes.Select(scope => scope.SiteId).ToArray(), scopes.Select(scope => scope.SensorId).ToArray());

    private static void AddScopes(NpgsqlCommand command, string[] sites, string[] sensors)
    {
        command.Parameters.AddWithValue("sites", NpgsqlDbType.Array | NpgsqlDbType.Varchar, sites);
        command.Parameters.AddWithValue("sensors", NpgsqlDbType.Array | NpgsqlDbType.Varchar, sensors);
    }

    // ---- persistence helpers ----

    private sealed record CandidateRow(string SiteId, string SensorId, string State, long Revision, Guid? DeviceId, long CandidateRevisionBeforeLink = 0)
    {
        public Guid CandidateId { get; init; }
    }

    private sealed record DeviceRow(string Name, string Description, long Revision, IReadOnlyList<AuthorizedPair> Scopes);

    private async Task<T> InTransactionAsync<T>(Func<NpgsqlTransaction, Task<T>> action, CancellationToken cancellationToken)
        where T : IInventoryResult
    {
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var transaction = await Connection.BeginTransactionAsync(cancellationToken);
            var result = await action(transaction);
            // Refusals (not found, forbidden, conflict, invalid) change nothing; only a successful, audited decision commits.
            if (result.Status == InventoryStatus.Ok) await transaction.CommitAsync(cancellationToken);
            return result;
        }
        finally { await dbContext.Database.CloseConnectionAsync(); }
    }

    private async Task<T> ReadAsync<T>(Func<NpgsqlConnection, Task<T>> action, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReadTimeout);
        await dbContext.Database.OpenConnectionAsync(timeout.Token);
        try { return await action(Connection); }
        finally { await dbContext.Database.CloseConnectionAsync(); }
    }

    private NpgsqlCommand ReadCommand(string sql, NpgsqlConnection connection) => new(sql, connection) { CommandTimeout = (int)ReadTimeout.TotalSeconds };

    private NpgsqlCommand Command(string sql, NpgsqlTransaction? transaction) => new(sql, Connection, transaction);

    private async Task<CandidateRow?> LockCandidateAsync(Guid candidateId, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = Command("SELECT site_id,sensor_id,state,revision,device_id FROM monitoring.device_candidate WHERE candidate_id=@id FOR UPDATE", transaction);
        command.Parameters.AddWithValue("id", candidateId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new CandidateRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), reader.IsDBNull(4) ? null : reader.GetGuid(4)) { CandidateId = candidateId }
            : null;
    }

    private async Task<DeviceRow?> LockDeviceAsync(Guid deviceId, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        string name, description;
        long revision;
        await using (var command = Command("SELECT name,description,revision FROM monitoring.device WHERE device_id=@id FOR UPDATE", transaction))
        {
            command.Parameters.AddWithValue("id", deviceId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            (name, description, revision) = (reader.GetString(0), reader.GetString(1), reader.GetInt64(2));
        }
        return new DeviceRow(name, description, revision, await ReadScopesAsync(deviceId, transaction, cancellationToken));
    }

    private async Task<IReadOnlyList<AuthorizedPair>> ReadScopesAsync(Guid deviceId, NpgsqlTransaction? transaction, CancellationToken cancellationToken)
    {
        var scopes = new List<AuthorizedPair>();
        await using var command = Command("SELECT site_id,sensor_id FROM monitoring.device_scope WHERE device_id=@id ORDER BY site_id,sensor_id", transaction);
        command.Parameters.AddWithValue("id", deviceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) scopes.Add(new AuthorizedPair(reader.GetString(0), reader.GetString(1)));
        return scopes;
    }

    private async Task<DeviceView?> ReadDeviceAsync(Guid deviceId, NpgsqlTransaction? transaction, CancellationToken cancellationToken)
    {
        string name, description;
        long revision;
        DateTimeOffset updated;
        await using (var command = Command("SELECT name,description,revision,updated_at FROM monitoring.device WHERE device_id=@id", transaction))
        {
            command.Parameters.AddWithValue("id", deviceId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            (name, description, revision, updated) = (reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetFieldValue<DateTimeOffset>(3));
        }
        return new DeviceView(deviceId, name, description, revision, await ReadScopesAsync(deviceId, transaction, cancellationToken), updated);
    }

    private async Task<CandidateView?> ReadCandidateAsync(Guid candidateId, NpgsqlTransaction? transaction, CancellationToken cancellationToken)
    {
        await using (var command = Command("SELECT site_id,sensor_id,mac::text,vlan_id,first_seen,last_seen,state,revision,device_id FROM monitoring.device_candidate WHERE candidate_id=@id", transaction))
        {
            command.Parameters.AddWithValue("id", candidateId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            var associations = new List<IpAssociationView>();
            var view = new CandidateView(candidateId, reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.GetFieldValue<DateTimeOffset>(4), reader.GetFieldValue<DateTimeOffset>(5), reader.GetString(6), reader.GetInt64(7),
                reader.IsDBNull(8) ? null : reader.GetGuid(8), associations);
            await reader.CloseAsync();
            await using var ips = Command("SELECT host(ip),first_seen,last_seen FROM monitoring.device_ip_association WHERE candidate_id=@id ORDER BY first_seen,ip", transaction);
            ips.Parameters.AddWithValue("id", candidateId);
            await using var ipReader = await ips.ExecuteReaderAsync(cancellationToken);
            while (await ipReader.ReadAsync(cancellationToken))
                associations.Add(new IpAssociationView(ipReader.GetString(0), ipReader.GetFieldValue<DateTimeOffset>(1), ipReader.GetFieldValue<DateTimeOffset>(2)));
            return view;
        }
    }

    private async Task<Guid> InsertDeviceAsync(string name, string description, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = Command("INSERT INTO monitoring.device(name,description) VALUES (@name,@description) RETURNING device_id", transaction);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("description", description);
        return (Guid)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private async Task AddScopeAsync(Guid deviceId, AuthorizedPair scope, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = Command("INSERT INTO monitoring.device_scope(device_id,site_id,sensor_id) VALUES (@id,@site,@sensor) ON CONFLICT DO NOTHING", transaction);
        command.Parameters.AddWithValue("id", deviceId);
        command.Parameters.AddWithValue("site", scope.SiteId);
        command.Parameters.AddWithValue("sensor", scope.SensorId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task LinkAsync(CandidateRow candidate, Guid deviceId, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = Command("UPDATE monitoring.device_candidate SET state='confirmed',device_id=@device,revision=revision+1 WHERE candidate_id=@id", transaction);
        command.Parameters.AddWithValue("device", deviceId);
        command.Parameters.AddWithValue("id", candidate.CandidateId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task AuditAsync(NpgsqlTransaction transaction, InventoryActor actor, string action, Guid? deviceId, Guid? candidateId, long? deviceBefore,
        long? deviceAfter, long? candidateBefore, long? candidateAfter, JsonObject changes, string? reason, CancellationToken cancellationToken)
    {
        await using var command = Command("""
            INSERT INTO monitoring.inventory_audit(actor,action,device_id,candidate_id,device_revision_before,device_revision_after,
              candidate_revision_before,candidate_revision_after,changes,reason)
            VALUES (@actor,@action,@device,@candidate,@dbefore,@dafter,@cbefore,@cafter,CAST(@changes AS jsonb),@reason)
            """, transaction);
        command.Parameters.AddWithValue("actor", actor.Subject);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("device", NpgsqlDbType.Uuid, (object?)deviceId ?? DBNull.Value);
        command.Parameters.AddWithValue("candidate", NpgsqlDbType.Uuid, (object?)candidateId ?? DBNull.Value);
        command.Parameters.AddWithValue("dbefore", NpgsqlDbType.Bigint, (object?)deviceBefore ?? DBNull.Value);
        command.Parameters.AddWithValue("dafter", NpgsqlDbType.Bigint, (object?)deviceAfter ?? DBNull.Value);
        command.Parameters.AddWithValue("cbefore", NpgsqlDbType.Bigint, (object?)candidateBefore ?? DBNull.Value);
        command.Parameters.AddWithValue("cafter", NpgsqlDbType.Bigint, (object?)candidateAfter ?? DBNull.Value);
        command.Parameters.AddWithValue("changes", changes.ToJsonString());
        command.Parameters.AddWithValue("reason", NpgsqlDbType.Text, (object?)reason ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
