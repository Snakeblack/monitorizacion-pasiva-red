using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Monitoring.Persistence.Search;

public sealed record GenerationInfo(int Generation, string IndexName, string Topic, DateTimeOffset SnapshotDeadline, DateTimeOffset CreatedAt);

public sealed class SearchGenerationException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public sealed class GenerationOptions
{
    public TimeSpan SnapshotWindow { get; set; } = TimeSpan.FromMinutes(30);
    public int BlockSize { get; set; } = 100;
    // Catch-up passes only look at rows this much older than the first pass; writers' transactions last milliseconds.
    public TimeSpan CatchUpMargin { get; set; } = TimeSpan.FromMinutes(5);
}

// Authority-side bookkeeping of search index generations. A generation re-publishes the latest authority record of every
// identity to its own topic; correctness comes from a revision anti-join (a record is copied only when the generation lacks that
// identity at that revision or newer), so no ordering of commits is ever deduced from sequences or ids.
public sealed class SearchGenerationStore(MonitoringDbContext dbContext, GenerationOptions options)
{
    private const long AllocationLock = 7182041003;

    private NpgsqlConnection Connection => (NpgsqlConnection)dbContext.Database.GetDbConnection();

    public async Task<GenerationInfo?> ActiveAsync(CancellationToken cancellationToken) =>
        await ReadOneAsync("SELECT generation,index_name,target_topic,snapshot_deadline,created_at FROM monitoring.search_generation WHERE state='active'", cancellationToken);

    public async Task<GenerationInfo?> FindAsync(int generation, CancellationToken cancellationToken) =>
        await ReadOneAsync($"SELECT generation,index_name,target_topic,snapshot_deadline,created_at FROM monitoring.search_generation WHERE generation={generation}", cancellationToken);

    public async Task<GenerationInfo> BeginAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var transaction = await Connection.BeginTransactionAsync(cancellationToken);
            await using (var gate = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@lock)", Connection, transaction))
            {
                gate.Parameters.AddWithValue("lock", AllocationLock);
                await gate.ExecuteNonQueryAsync(cancellationToken);
            }
            await using (var busy = new NpgsqlCommand("SELECT count(*) FROM monitoring.search_generation WHERE state IN ('building','ready')", Connection, transaction))
                if ((long)(await busy.ExecuteScalarAsync(cancellationToken))! > 0) throw new SearchGenerationException("rebuild-in-progress");
            await using var insert = new NpgsqlCommand("""
                INSERT INTO monitoring.search_generation(generation,index_name,target_topic,state,snapshot_deadline)
                SELECT n,'sessions-v2-'||lpad(n::text,6,'0'),'monitoring.sessions.v2.g'||n,'building',clock_timestamp()+@window
                FROM (SELECT max(generation)+1 AS n FROM monitoring.search_generation) next
                RETURNING generation,index_name,target_topic,snapshot_deadline,created_at
                """, Connection, transaction);
            insert.Parameters.AddWithValue("window", options.SnapshotWindow);
            GenerationInfo created;
            await using (var reader = await insert.ExecuteReaderAsync(cancellationToken))
            {
                await reader.ReadAsync(cancellationToken);
                created = Read(reader);
            }
            await transaction.CommitAsync(cancellationToken);
            return created;
        }
        finally { await dbContext.Database.CloseConnectionAsync(); }
    }

    // One full keyset pass over every identity's latest authority record.
    public Task<long> CopyAllAsync(GenerationInfo generation, CancellationToken cancellationToken) =>
        CopyAsync(generation, null, cancellationToken);

    // Passes restricted to rows created since shortly before the generation began, repeated until a pass copies nothing;
    // returns the total copied.
    public async Task<long> CatchUpAsync(GenerationInfo generation, CancellationToken cancellationToken)
    {
        long total = 0, copied;
        do
        {
            copied = await CopyAsync(generation, generation.CreatedAt - options.CatchUpMargin, cancellationToken);
            total += copied;
        }
        while (copied > 0);
        return total;
    }

    public async Task<DateTimeOffset> NowAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand("SELECT clock_timestamp()", Connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            return reader.GetFieldValue<DateTimeOffset>(0);
        }
        finally { await dbContext.Database.CloseConnectionAsync(); }
    }

    public Task MarkReadyAsync(int generation, CancellationToken cancellationToken) => TransitionAsync(generation, "building", "ready", cancellationToken);
    public Task AbortAsync(int generation, CancellationToken cancellationToken) =>
        TransitionAsync(generation, "state IN ('building','ready')", "aborted", cancellationToken);
    public Task RetireAsync(int generation, CancellationToken cancellationToken) => TransitionAsync(generation, "retired-only-from-active", "retired", cancellationToken);

    // Switches the generation writers publish to. Callers hold the exclusive publication lock.
    public async Task ActivateAsync(int generation, CancellationToken cancellationToken)
    {
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var transaction = await Connection.BeginTransactionAsync(cancellationToken);
            // Verify before touching anything: a refused activation must leave the serving generation active.
            await using (var check = new NpgsqlCommand("SELECT state FROM monitoring.search_generation WHERE generation=@generation FOR UPDATE", Connection, transaction))
            {
                check.Parameters.AddWithValue("generation", generation);
                if ((string?)await check.ExecuteScalarAsync(cancellationToken) != "ready") throw new SearchGenerationException("generation-not-ready");
            }
            await using var command = new NpgsqlCommand("""
                UPDATE monitoring.search_generation SET state='retired' WHERE state='active';
                UPDATE monitoring.search_generation SET state='active',activated_at=clock_timestamp() WHERE generation=@generation;
                """, Connection, transaction);
            command.Parameters.AddWithValue("generation", generation);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally { await dbContext.Database.CloseConnectionAsync(); }
    }

    private async Task<long> CopyAsync(GenerationInfo generation, DateTimeOffset? createdFloor, CancellationToken cancellationToken)
    {
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            long total = 0;
            string? after = null;
            while (true)
            {
                if (await NowAsync(cancellationToken) > generation.SnapshotDeadline) throw new SearchGenerationException("snapshot-deadline");
                var ids = new List<Guid>();
                string? last = null;
                await using (var read = new NpgsqlCommand("""
                    SELECT DISTINCT ON (o.aggregateid) o.id,o.aggregateid FROM monitoring.projection_outbox o
                    WHERE o.target_topic=(SELECT target_topic FROM monitoring.search_generation WHERE state='active')
                      AND (@first OR o.aggregateid>@after) AND (@floor::timestamptz IS NULL OR o.created_at>=@floor)
                    ORDER BY o.aggregateid,o.revision DESC LIMIT @limit
                    """, Connection))
                {
                    read.Parameters.AddWithValue("first", after is null);
                    read.Parameters.AddWithValue("after", after ?? "");
                    read.Parameters.AddWithValue("floor", NpgsqlTypes.NpgsqlDbType.TimestampTz, (object?)createdFloor ?? DBNull.Value);
                    read.Parameters.AddWithValue("limit", options.BlockSize);
                    await using var reader = await read.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        ids.Add(reader.GetGuid(0));
                        last = reader.GetString(1);
                    }
                }
                if (ids.Count == 0) break;
                await using (var insert = new NpgsqlCommand("""
                    INSERT INTO monitoring.projection_outbox(id,aggregateid,aggregatetype,target_topic,revision,schema_version,payload,source_outbox_id)
                    SELECT gen_random_uuid(),l.aggregateid,l.aggregatetype,@topic,l.revision,l.schema_version,l.payload,l.id
                    FROM monitoring.projection_outbox l WHERE l.id=ANY(@ids)
                      AND NOT EXISTS (SELECT 1 FROM monitoring.projection_outbox g
                                      WHERE g.target_topic=@topic AND g.aggregateid=l.aggregateid AND g.revision>=l.revision)
                    ON CONFLICT DO NOTHING
                    """, Connection))
                {
                    insert.Parameters.AddWithValue("topic", generation.Topic);
                    insert.Parameters.AddWithValue("ids", ids.ToArray());
                    var inserted = await insert.ExecuteNonQueryAsync(cancellationToken);
                    total += inserted;
                    if (inserted > 0)
                    {
                        await using var count = new NpgsqlCommand("UPDATE monitoring.search_generation SET copied=copied+@n WHERE generation=@generation", Connection);
                        count.Parameters.AddWithValue("n", (long)inserted);
                        count.Parameters.AddWithValue("generation", generation.Generation);
                        await count.ExecuteNonQueryAsync(cancellationToken);
                    }
                }
                after = last;
                if (ids.Count < options.BlockSize) break;
            }
            return total;
        }
        finally { await dbContext.Database.CloseConnectionAsync(); }
    }

    private async Task TransitionAsync(int generation, string from, string to, CancellationToken cancellationToken)
    {
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var condition = from switch
            {
                "building" => "state='building'",
                "retired-only-from-active" => "state IN ('active','ready')",
                _ => from
            };
            await using var command = new NpgsqlCommand($"UPDATE monitoring.search_generation SET state=@to WHERE generation=@generation AND {condition}", Connection);
            command.Parameters.AddWithValue("to", to);
            command.Parameters.AddWithValue("generation", generation);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new SearchGenerationException("invalid-transition");
        }
        finally { await dbContext.Database.CloseConnectionAsync(); }
    }

    private async Task<GenerationInfo?> ReadOneAsync(string sql, CancellationToken cancellationToken)
    {
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand(sql, Connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
        }
        finally { await dbContext.Database.CloseConnectionAsync(); }
    }

    private static GenerationInfo Read(NpgsqlDataReader reader) =>
        new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3), reader.GetFieldValue<DateTimeOffset>(4));
}
