using System.Text;
using Monitoring.Domain.Ingestion;

namespace Monitoring.Tests;

public sealed class IngestionContractTests
{
    private const string ValidBatch = "{\"schemaVersion\":1,\"batchId\":\"batch-1\",\"siteId\":\"site-1\",\"sensorId\":\"sensor-1\",\"events\":[{\"eventId\":\"event-1\",\"occurredAt\":\"2026-09-24T12:30:00.123Z\",\"data\":{\"value\":7}}]}";
    private const string OneEvent = "{\"eventId\":\"e\",\"occurredAt\":\"2026-09-24T12:30:00Z\",\"data\":{}}";

    [Fact]
    public void ContractAcceptsV1AndMaximumIdentifierAndEventBoundaries()
    {
        Assert.True(BatchContract.TryParse(Encoding.UTF8.GetBytes(ValidBatch), out var batch));
        Assert.Equal(1, batch!.SchemaVersion);
        Assert.Equal("batch-1", batch.BatchId);
        Assert.Equal("site-1", batch.SiteId);
        Assert.Equal("sensor-1", batch.SensorId);
        Assert.Equal("event-1", Assert.Single(batch.Events).EventId);

        var maximumId = new string('i', 128);
        var maximumIdsBatch = ValidBatch.Replace("batch-1", maximumId, StringComparison.Ordinal)
            .Replace("site-1", maximumId, StringComparison.Ordinal)
            .Replace("sensor-1", maximumId, StringComparison.Ordinal)
            .Replace("event-1", maximumId, StringComparison.Ordinal);
        Assert.True(BatchContract.TryParse(Encoding.UTF8.GetBytes(maximumIdsBatch), out batch));
        Assert.Equal(maximumId, batch!.Events[0].EventId);

        var events = string.Join(',', Enumerable.Repeat(OneEvent, 500));
        Assert.True(BatchContract.TryParse(Encoding.UTF8.GetBytes(Envelope(events)), out batch));
        Assert.Equal(500, batch!.Events.Count);
    }

    [Theory]
    [InlineData("\"schemaVersion\":1", "\"schemaVersion\":2")]
    [InlineData("\"events\":[", "\"unknown\":true,\"events\":[")]
    [InlineData("\"batchId\":\"batch-1\"", "\"batchId\":\"\"")]
    [InlineData("\"batchId\":\"batch-1\"", "\"batchId\":null")]
    [InlineData("\"batchId\":\"batch-1\"", "\"batchId\":1")]
    [InlineData("\"schemaVersion\":1", "\"schemaVersion\":\"1\"")]
    [InlineData("\"schemaVersion\":1", "\"schemaVersion\":1.0")]
    [InlineData("2026-09-24T12:30:00.123Z", "2026-09-24T12:30:00+00:00")]
    [InlineData("2026-09-24T12:30:00.123Z", "2026-09-24T14:30:00+02:00")]
    [InlineData("2026-09-24T12:30:00.123Z", "2026-09-24T12:30:00.1234Z")]
    [InlineData("\"data\":{\"value\":7}", "\"data\":[]")]
    [InlineData("\"data\":{\"value\":7}", "\"data\":null")]
    [InlineData("\"data\":{\"value\":7}", "\"data\":{\"value\":7},\"unknown\":true")]
    [InlineData("\"eventId\":\"event-1\"", "\"eventId\":1")]
    public void ContractRejectsUnsupportedVersionsFieldsTypesAndFormats(string original, string replacement)
    {
        var json = ValidBatch.Replace(original, replacement, StringComparison.Ordinal);
        Assert.False(BatchContract.TryParse(Encoding.UTF8.GetBytes(json), out _));
    }

    [Theory]
    [InlineData("batch-1", "")]
    [InlineData("site-1", "")]
    [InlineData("sensor-1", "")]
    [InlineData("event-1", "")]
    [InlineData("batch-1", "too-long")]
    [InlineData("site-1", "too-long")]
    [InlineData("sensor-1", "too-long")]
    [InlineData("event-1", "too-long")]
    public void ContractRejectsEmptyAndOverlongIdentifiers(string original, string replacement)
    {
        var value = replacement.Length == 0 ? replacement : new string('i', 129);
        var json = ValidBatch.Replace(original, value, StringComparison.Ordinal);

        Assert.False(BatchContract.TryParse(Encoding.UTF8.GetBytes(json), out _));
    }

    [Theory]
    [InlineData("2026-09-24T12:30:00Z")]
    [InlineData("2026-09-24T12:30:00.1Z")]
    [InlineData("2026-09-24T12:30:00.12Z")]
    [InlineData("2026-09-24T12:30:00.123Z")]
    public void ContractAcceptsUtcTimestampsWithAtMostMillisecondPrecision(string occurredAt)
    {
        var json = ValidBatch.Replace("2026-09-24T12:30:00.123Z", occurredAt, StringComparison.Ordinal);

        Assert.True(BatchContract.TryParse(Encoding.UTF8.GetBytes(json), out var batch));
        Assert.Equal(occurredAt, batch!.Events[0].OccurredAt);
    }

    [Fact]
    public void ContractRejectsMalformedJsonEmptyBatchesAndMoreThan500Events()
    {
        Assert.False(BatchContract.TryParse(Encoding.UTF8.GetBytes("not-json"), out _));
        Assert.False(BatchContract.TryParse(Encoding.UTF8.GetBytes(Envelope(string.Empty)), out _));

        var events = string.Join(',', Enumerable.Repeat(OneEvent, 501));
        Assert.False(BatchContract.TryParse(Encoding.UTF8.GetBytes(Envelope(events)), out _));
    }

    [Fact]
    public void ContractAcceptsExactlyOneMebibyteAndRejectsOneByteMore()
    {
        const string prefix = "{\"schemaVersion\":1,\"batchId\":\"b\",\"siteId\":\"s\",\"sensorId\":\"n\",\"events\":[{\"eventId\":\"e\",\"occurredAt\":\"2026-09-24T12:00:00Z\",\"data\":{\"p\":\"";
        const string suffix = "\"}}]}";
        var exact = prefix + new string('a', BatchContract.MaximumBodyBytes - prefix.Length - suffix.Length) + suffix;
        var exactBytes = Encoding.UTF8.GetBytes(exact);

        Assert.Equal(BatchContract.MaximumBodyBytes, exactBytes.Length);
        Assert.True(BatchContract.TryParse(exactBytes, out _));
        Assert.False(BatchContract.TryParse(Encoding.UTF8.GetBytes(exact + " "), out _));
    }

    private static string Envelope(string events) =>
        "{\"schemaVersion\":1,\"batchId\":\"b\",\"siteId\":\"s\",\"sensorId\":\"n\",\"events\":[" + events + "]}";
}
