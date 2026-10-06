using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Monitoring.Probe;

public sealed record SpoolOptions(long MaximumDiskBytes = 64 * 1024 * 1024, long MaximumPayloadBytes = 16 * 1024 * 1024,
    int MaximumEvents = 100000, int MaximumCheckpointBytes = 1024 * 1024);
public sealed record DurableBatch(string BatchId, byte[] Body, int EventCount, int Attempts, DateTimeOffset CreatedAt,
    DateTimeOffset NextAttemptAt, string State, string? Cause);
public sealed record SpoolStatus(long Events, long PayloadBytes, long DiskBytes, long LostEvents, long DeliveredEvents,
    long Retries, long IsolatedEvents, bool IdentitySuspended, double? OldestAgeSeconds);
public interface IProbeSpool
{
    void SaveCapture(IReadOnlyList<ProbeEvent> events, IReadOnlyList<FlowState> checkpoint, DateTimeOffset now);
    IReadOnlyList<FlowState> LoadCheckpoint();
    DurableBatch? Next(DateTimeOffset now);
    bool Acknowledge(DurableBatch batch);
    void Fail(DurableBatch batch, DateTimeOffset nextAttemptAt, string cause, bool isolate = false, bool suspendIdentity = false);
    SpoolStatus Status(DateTimeOffset now);
}
public sealed class SqliteSpool : IProbeSpool, IDisposable
{
    private readonly object gate = new();
    private readonly string path;
    private readonly ProbeScope scope;
    private readonly SpoolOptions options;
    private readonly SqliteConnection connection;
    private readonly long databaseBudget;
    public SqliteSpool(string path, ProbeScope scope, SpoolOptions options)
    {
        ProbeJson.ValidateScope(scope);
        if (options.MaximumDiskBytes < 262144 || options.MaximumPayloadBytes <= 0 || options.MaximumEvents <= 0 || options.MaximumCheckpointBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options));
        this.path = Path.GetFullPath(path); this.scope = scope; this.options = options;
        Directory.CreateDirectory(Path.GetDirectoryName(this.path)!);
        // A page may exist in both DB and WAL. Reserve frame headers, WAL header, SHM and four spare pages.
        var pages = (options.MaximumDiskBytes - 32768 - 32) / (2 * 4096 + 24) - 4;
        databaseBudget = pages * 4096;
        connection = new(new SqliteConnectionStringBuilder { DataSource = this.path, Pooling = false }.ToString()); connection.Open();
        Execute($"PRAGMA page_size=4096; PRAGMA max_page_count={pages}; PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA cache_spill=OFF; PRAGMA wal_autocheckpoint=1; PRAGMA journal_size_limit=0; PRAGMA busy_timeout=5000;");
        Execute("""
            CREATE TABLE IF NOT EXISTS batches(seq INTEGER PRIMARY KEY AUTOINCREMENT,batch_id TEXT NOT NULL UNIQUE,
              body BLOB NOT NULL,event_count INTEGER NOT NULL,created_at INTEGER NOT NULL,attempts INTEGER NOT NULL DEFAULT 0,
              next_attempt_at INTEGER NOT NULL,state TEXT NOT NULL DEFAULT 'pending',cause TEXT);
            CREATE TABLE IF NOT EXISTS statistics(id INTEGER PRIMARY KEY CHECK(id=1),lost INTEGER NOT NULL DEFAULT 0,
              delivered INTEGER NOT NULL DEFAULT 0,retries INTEGER NOT NULL DEFAULT 0,suspended INTEGER NOT NULL DEFAULT 0);
            INSERT OR IGNORE INTO statistics(id) VALUES(1);
            CREATE TABLE IF NOT EXISTS capture_state(id INTEGER PRIMARY KEY CHECK(id=1),scope TEXT NOT NULL,checkpoint TEXT NOT NULL);
            """);
        var scopeJson = JsonSerializer.Serialize(scope, ProbeJson.Options);
        Execute("INSERT OR IGNORE INTO capture_state(id,scope,checkpoint) VALUES(1,$scope,'[]');", null, ("$scope", scopeJson));
        if ((string)Scalar("SELECT scope FROM capture_state;")! != scopeJson)
        { connection.Dispose(); throw new InvalidDataException("Spool belongs to another probe scope."); }
        Checkpoint();
    }
    public void SaveCapture(IReadOnlyList<ProbeEvent> events, IReadOnlyList<FlowState> checkpoint, DateTimeOffset now)
    {
        foreach (var value in events) ProbeJson.Validate(value);
        if (checkpoint.Any(flow => flow.Scope != scope)) throw new ArgumentException("Checkpoint scope mismatch.");
        var checkpointJson = JsonSerializer.Serialize(checkpoint, ProbeJson.Options);
        var checkpointBytes = System.Text.Encoding.UTF8.GetByteCount(checkpointJson);
        if (checkpointBytes > options.MaximumCheckpointBytes || checkpointBytes + 32768 >= databaseBudget)
            throw new InvalidDataException("Checkpoint exceeds spool capacity.");
        lock (gate)
        {
            using var transaction = connection.BeginTransaction();
            Execute("UPDATE capture_state SET checkpoint=$json WHERE id=1;", transaction, ("$json", checkpointJson));
            var chunk = new List<ProbeEvent>(); var batchId = Guid.NewGuid().ToString("N");
            foreach (var value in events)
            {
                var candidate = chunk.Append(value).ToArray();
                if (candidate.Length > 500 || ProbeJson.Batch(scope, batchId, candidate).Length > 1048576)
                {
                    if (chunk.Count > 0) Insert(chunk, batchId, now, checkpointBytes, transaction);
                    chunk.Clear(); batchId = Guid.NewGuid().ToString("N");
                }
                chunk.Add(value);
            }
            if (chunk.Count > 0) Insert(chunk, batchId, now, checkpointBytes, transaction);
            transaction.Commit(); Checkpoint();
        }
    }
    private void Insert(IReadOnlyList<ProbeEvent> events, string id, DateTimeOffset now, int checkpointBytes, SqliteTransaction transaction)
    {
        var body = ProbeJson.Batch(scope, id, events);
        // Reserve row/index overhead conservatively; physical max_page_count is the final hard boundary.
        while (Count(transaction) + events.Count > options.MaximumEvents
            || Bytes(transaction) + body.Length > options.MaximumPayloadBytes
            || Cost(transaction) + body.Length * 2L + 2048 + checkpointBytes + 32768 > databaseBudget)
        {
            if (!EvictOldest(transaction))
            { Execute("UPDATE statistics SET lost=lost+$count WHERE id=1;", transaction, ("$count", events.Count)); return; }
        }
        Execute("INSERT INTO batches(batch_id,body,event_count,created_at,next_attempt_at) VALUES($id,$body,$count,$now,$now);",
            transaction, ("$id", id), ("$body", body), ("$count", events.Count), ("$now", now.ToUnixTimeMilliseconds()));
    }
    private bool EvictOldest(SqliteTransaction transaction)
    {
        var count = Scalar("SELECT event_count FROM batches ORDER BY seq LIMIT 1;", transaction);
        if (count is null) return false;
        Execute("DELETE FROM batches WHERE seq=(SELECT min(seq) FROM batches); UPDATE statistics SET lost=lost+$count WHERE id=1;",
            transaction, ("$count", count)); return true;
    }
    public IReadOnlyList<FlowState> LoadCheckpoint()
    { lock (gate) return JsonSerializer.Deserialize<FlowState[]>((string)Scalar("SELECT checkpoint FROM capture_state;")!, ProbeJson.Options)!; }
    public DurableBatch? Next(DateTimeOffset now)
    {
        lock (gate)
        {
            if ((long)Scalar("SELECT suspended FROM statistics;")! != 0) return null;
            using var command = Command("SELECT batch_id,body,event_count,attempts,created_at,next_attempt_at,state,cause FROM batches WHERE state='pending' AND next_attempt_at<=$now ORDER BY seq LIMIT 1;", null,
                ("$now", now.ToUnixTimeMilliseconds()));
            using var reader = command.ExecuteReader();
            return !reader.Read() ? null : new(reader.GetString(0), (byte[])reader[1], reader.GetInt32(2), reader.GetInt32(3),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(4)), DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(5)),
                reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7));
        }
    }
    public bool Acknowledge(DurableBatch batch)
    {
        lock (gate)
        {
            using var transaction = connection.BeginTransaction();
            var removed = Execute("DELETE FROM batches WHERE batch_id=$id AND body=$body AND state='pending';", transaction,
                ("$id", batch.BatchId), ("$body", batch.Body));
            if (removed == 1) Execute("UPDATE statistics SET delivered=delivered+$count WHERE id=1;", transaction, ("$count", batch.EventCount));
            transaction.Commit(); Checkpoint(); return removed == 1;
        }
    }
    public void Fail(DurableBatch batch, DateTimeOffset nextAttemptAt, string cause, bool isolate = false, bool suspendIdentity = false)
    {
        lock (gate)
        {
            using var transaction = connection.BeginTransaction();
            var changed = Execute("UPDATE batches SET attempts=min(attempts+1,2147483647),next_attempt_at=$next,state=$state,cause=$cause WHERE batch_id=$id AND body=$body AND state='pending';",
                transaction, ("$next", nextAttemptAt.ToUnixTimeMilliseconds()), ("$state", isolate ? "isolated" : "pending"),
                ("$cause", cause), ("$id", batch.BatchId), ("$body", batch.Body));
            if (changed == 1) Execute("UPDATE statistics SET retries=retries+1,suspended=max(suspended,$suspended) WHERE id=1;", transaction, ("$suspended", suspendIdentity ? 1 : 0));
            transaction.Commit(); Checkpoint();
        }
    }
    public void ResumeIdentity() { lock (gate) { Execute("UPDATE statistics SET suspended=0 WHERE id=1;"); Checkpoint(); } }
    public SpoolStatus Status(DateTimeOffset now)
    {
        lock (gate)
        {
            using var command = Command("SELECT lost,delivered,retries,suspended FROM statistics;"); using var reader = command.ExecuteReader(); reader.Read();
            var lost = reader.GetInt64(0); var delivered = reader.GetInt64(1); var retries = reader.GetInt64(2); var suspended = reader.GetInt64(3) != 0;
            reader.Close();
            var oldest = Scalar("SELECT min(created_at) FROM batches;");
            return new(Count(), Bytes(), DiskBytes(), lost, delivered, retries,
                (long)Scalar("SELECT coalesce(sum(event_count),0) FROM batches WHERE state='isolated';")!, suspended,
                oldest is null ? null : Math.Max(0, (now.ToUnixTimeMilliseconds() - (long)oldest) / 1000d));
        }
    }
    private long Count(SqliteTransaction? tx = null) => (long)Scalar("SELECT coalesce(sum(event_count),0) FROM batches;", tx)!;
    private long Bytes(SqliteTransaction? tx = null) => (long)Scalar("SELECT coalesce(sum(length(body)),0) FROM batches;", tx)!;
    private long Cost(SqliteTransaction? tx = null) => (long)Scalar("SELECT coalesce(sum(length(body)*2+2048),0) FROM batches;", tx)!;
    private long DiskBytes() => new[] { path, path + "-wal", path + "-shm" }.Where(File.Exists).Sum(f => new FileInfo(f).Length);
    private void Checkpoint() => Execute("PRAGMA wal_checkpoint(TRUNCATE);");
    private SqliteCommand Command(string sql, SqliteTransaction? transaction = null, params (string Name, object Value)[] parameters)
    {
        var command = connection.CreateCommand(); command.CommandText = sql; command.Transaction = transaction;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value); return command;
    }
    private int Execute(string sql, SqliteTransaction? tx = null, params (string Name, object Value)[] parameters)
    { using var command = Command(sql, tx, parameters); return command.ExecuteNonQuery(); }
    private object? Scalar(string sql, SqliteTransaction? tx = null)
    { using var command = Command(sql, tx); var value = command.ExecuteScalar(); return value is DBNull ? null : value; }
    public void Dispose() { lock (gate) connection.Dispose(); }
}
public static class SpoolSizing
{
    public static long RequiredBytes(decimal eventsPerSecond, decimal measuredBytesPerEvent, decimal margin = .25m)
    {
        if (eventsPerSecond <= 0 || measuredBytesPerEvent <= 0 || margin < .25m) throw new ArgumentOutOfRangeException(nameof(eventsPerSecond));
        return checked((long)decimal.Ceiling((14400m * eventsPerSecond + 900m * 4m * eventsPerSecond) * measuredBytesPerEvent * (1 + margin)));
    }
}
