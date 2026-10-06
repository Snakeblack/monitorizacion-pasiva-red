using System.Text.Json;
using System.Globalization;
using Monitoring.Domain.Sessions;
using Npgsql;
using NpgsqlTypes;

namespace Monitoring.Persistence.Sessions;

// Both the live projector and the paged historical initializer use the same canonical publication contract.
public static class OutboxStore
{
    public const string SessionTopic = "monitoring.sessions.v1";
    // All session/retention writers share this lock; rebuild takes its exclusive counterpart at the alias switch.
    public const long PublicationLock = 7182041001;

    public static async Task WriteSessionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        SessionIdentity identity, CanonicalSession session, DateTimeOffset acceptedAt, CancellationToken cancellationToken)
    {
        await using (var publicationLock = new NpgsqlCommand("SELECT pg_advisory_xact_lock_shared(@lock)", connection, transaction))
        {
            publicationLock.Parameters.AddWithValue("lock", PublicationLock);
            await publicationLock.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var command = Command("""
            INSERT INTO monitoring.session_identity(site_id,sensor_id,event_id,document_key,revision,state)
            VALUES (@site,@sensor,@event,@key,1,'active') ON CONFLICT DO NOTHING;
            """, connection, transaction, identity))
            await command.ExecuteNonQueryAsync(cancellationToken);
        long revision;
        await using (var command = Command("""
            SELECT revision,state FROM monitoring.session_identity
            WHERE site_id=@site AND sensor_id=@sensor AND event_id=@event FOR UPDATE
            """, connection, transaction, identity))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken) || reader.GetString(1) != "active")
                throw new InvalidOperationException("A suppressed session identity cannot be republished.");
            revision = reader.GetInt64(0);
        }
        await using (var command = Command("""
            INSERT INTO monitoring.session_metadata(site_id,sensor_id,event_id,started_at,ended_at,source_ip,destination_ip,
              source_port,destination_port,protocol,vlan_id,provenance,revision,inferred,partial,close_reason,packet_count,byte_count)
            VALUES (@site,@sensor,@event,@start,@end,@source,@destination,@source_port,@destination_port,@protocol,@vlan,
              @provenance,@revision,@inferred,@partial,@reason,@packets,@bytes) ON CONFLICT DO NOTHING
            """, connection, transaction, identity))
        {
            command.Parameters.AddWithValue("start", session.StartedAt);
            command.Parameters.AddWithValue("end", session.EndedAt);
            command.Parameters.AddWithValue("source", NpgsqlDbType.Inet, session.SourceIp);
            command.Parameters.AddWithValue("destination", NpgsqlDbType.Inet, session.DestinationIp);
            command.Parameters.AddWithValue("source_port", session.SourcePort);
            command.Parameters.AddWithValue("destination_port", session.DestinationPort);
            command.Parameters.AddWithValue("protocol", session.Protocol);
            command.Parameters.AddWithValue("provenance", session.Provenance);
            command.Parameters.AddWithValue("revision", revision);
            AddNullable(command, "vlan", NpgsqlDbType.Integer, session.VlanId);
            AddNullable(command, "inferred", NpgsqlDbType.Boolean, session.Inferred);
            AddNullable(command, "partial", NpgsqlDbType.Boolean, session.Partial);
            AddNullable(command, "reason", NpgsqlDbType.Text, session.CloseReason);
            AddNullable(command, "packets", NpgsqlDbType.Bigint, session.PacketCount);
            AddNullable(command, "bytes", NpgsqlDbType.Bigint, session.ByteCount);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        var payload = JsonSerializer.Serialize(new
        {
            schemaVersion = 1, operation = "upsert", documentKey = identity.DocumentKey, revision,
            siteId = identity.SiteId, sensorId = identity.SensorId, eventId = identity.EventId,
            startedAt = UtcText(session.StartedAt), endedAt = UtcText(session.EndedAt),
            sourceIp = session.SourceIp.ToString(), destinationIp = session.DestinationIp.ToString(),
            sourcePort = session.SourcePort, destinationPort = session.DestinationPort, protocol = session.Protocol,
            vlanId = session.VlanId, provenance = session.Provenance, inferred = session.Inferred,
            partial = session.Partial, closeReason = session.CloseReason, packetCount = session.PacketCount,
            byteCount = session.ByteCount, acceptedAt = UtcText(acceptedAt)
        });
        await using var insert = Command("""
            INSERT INTO monitoring.projection_outbox(id,aggregateid,aggregatetype,target_topic,revision,schema_version,payload)
            VALUES (@id,@key,'sessions',@topic,@revision,1,CAST(@payload AS jsonb)) ON CONFLICT DO NOTHING
            """, connection, transaction, identity);
        insert.Parameters.AddWithValue("id", Guid.NewGuid());
        insert.Parameters.AddWithValue("topic", SessionTopic);
        insert.Parameters.AddWithValue("revision", revision);
        insert.Parameters.AddWithValue("payload", payload);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddNullable(NpgsqlCommand command, string name, NpgsqlDbType type, object? value) =>
        command.Parameters.AddWithValue(name, type, value ?? DBNull.Value);
    private static string UtcText(DateTimeOffset instant) => instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    private static NpgsqlCommand Command(string sql, NpgsqlConnection connection, NpgsqlTransaction transaction, SessionIdentity identity)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("site", identity.SiteId);
        command.Parameters.AddWithValue("sensor", identity.SensorId);
        command.Parameters.AddWithValue("event", identity.EventId);
        command.Parameters.AddWithValue("key", identity.DocumentKey);
        return command;
    }
}
