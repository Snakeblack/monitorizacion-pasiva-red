using System.Text.Json;
using System.Globalization;

namespace Monitoring.Domain.Ingestion;

public sealed record IngestionBatch(
    int SchemaVersion,
    string BatchId,
    string SiteId,
    string SensorId,
    IReadOnlyList<IngestionEvent> Events);

public sealed record IngestionEvent(string EventId, string OccurredAt, JsonElement Data);

public static class BatchContract
{
    public const int MaximumBodyBytes = 1024 * 1024;
    public const int MaximumEvents = 500;
    public const int MaximumIdentifierLength = 128;

    private static readonly HashSet<string> BatchFields =
        ["schemaVersion", "batchId", "siteId", "sensorId", "events"];

    private static readonly HashSet<string> EventFields =
        ["eventId", "occurredAt", "data"];

    private static readonly string[] UtcTimestampFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.f'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.ff'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.fff'Z'"
    ];

    public static bool TryParse(ReadOnlyMemory<byte> utf8Json, out IngestionBatch? batch)
    {
        batch = null;
        if (utf8Json.Length is 0 or > MaximumBodyBytes)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(utf8Json);
            if (!TryReadBatch(document.RootElement, out batch))
            {
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            batch = null;
            return false;
        }
    }

    private static bool TryReadBatch(JsonElement root, out IngestionBatch? batch)
    {
        batch = null;
        if (!TryReadFields(root, BatchFields, out var fields)
            || !fields.TryGetValue("schemaVersion", out var version)
            || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var schemaVersion)
            || schemaVersion != 1
            || !TryGetIdentifier(fields, "batchId", out var batchId)
            || !TryGetIdentifier(fields, "siteId", out var siteId)
            || !TryGetIdentifier(fields, "sensorId", out var sensorId)
            || !fields.TryGetValue("events", out var eventsElement)
            || eventsElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var eventCount = eventsElement.GetArrayLength();
        if (eventCount is < 1 or > MaximumEvents)
        {
            return false;
        }

        var events = new List<IngestionEvent>(eventCount);
        foreach (var eventElement in eventsElement.EnumerateArray())
        {
            if (!TryReadEvent(eventElement, out var ingestionEvent))
            {
                return false;
            }

            events.Add(ingestionEvent!);
        }

        batch = new IngestionBatch(schemaVersion, batchId!, siteId!, sensorId!, events);
        return true;
    }

    private static bool TryReadEvent(JsonElement element, out IngestionEvent? ingestionEvent)
    {
        ingestionEvent = null;
        if (!TryReadFields(element, EventFields, out var fields)
            || !TryGetIdentifier(fields, "eventId", out var eventId)
            || !fields.TryGetValue("occurredAt", out var occurredAtElement)
            || occurredAtElement.ValueKind != JsonValueKind.String
            || !IsUtcTimestamp(occurredAtElement.GetString())
            || !fields.TryGetValue("data", out var data)
            || data.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        ingestionEvent = new IngestionEvent(eventId!, occurredAtElement.GetString()!, data.Clone());
        return true;
    }

    private static bool TryReadFields(
        JsonElement element,
        HashSet<string> allowedFields,
        out Dictionary<string, JsonElement> fields)
    {
        fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!allowedFields.Contains(property.Name) || !fields.TryAdd(property.Name, property.Value))
            {
                return false;
            }
        }

        return fields.Count == allowedFields.Count;
    }

    private static bool TryGetIdentifier(
        IReadOnlyDictionary<string, JsonElement> fields,
        string fieldName,
        out string? identifier)
    {
        identifier = null;
        if (!fields.TryGetValue(fieldName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        identifier = value.GetString();
        return !string.IsNullOrEmpty(identifier) && identifier.Length <= MaximumIdentifierLength;
    }

    private static bool IsUtcTimestamp(string? value) =>
        value is not null
        && DateTimeOffset.TryParseExact(
            value,
            UtcTimestampFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out _);
}
