using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Primitives;

namespace Monitoring.Host.Sessions;

// Syntactically validated query selectors; scope resolution and the search itself happen only after this succeeds.
internal sealed record SessionSearchQuery(DateTimeOffset From, DateTimeOffset To, string? SiteId, string? SensorId,
    string? SourceIp, string? DestinationIp, string? Protocol, int? SourcePort, int? DestinationPort, int PageSize, string? Cursor)
{
    // Canonical text of every filter that shapes the result; a cursor is only valid for the exact same text.
    internal string Fingerprint() => string.Join('|', From.UtcTicks, To.UtcTicks, SiteId, SensorId, SourceIp, DestinationIp, Protocol, SourcePort, DestinationPort);

    internal const int DefaultPageSize = 50;
    internal const int MaximumPageSize = 100;
    private const int MaximumIdentifierLength = 128;
    private const int MaximumCursorLength = 2048;
    internal static readonly TimeSpan MaximumWindow = TimeSpan.FromDays(30);
    internal static readonly TimeSpan ShortWindow = TimeSpan.FromHours(24);
    internal static readonly TimeSpan ClockSkewTolerance = TimeSpan.FromSeconds(5);

    private static readonly HashSet<string> Names = new(StringComparer.Ordinal)
    {
        "from", "to", "siteId", "sensorId", "sourceIp", "destinationIp", "protocol", "sourcePort", "destinationPort", "pageSize", "cursor"
    };
    private static readonly Regex Instant = new(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,3})?Z$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex Digits = new(@"^\d{1,5}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex DottedQuad = new(@"^(0|[1-9]\d{0,2})(\.(0|[1-9]\d{0,2})){3}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    // The failure code is a stable constant, never an echo of the input.
    internal static bool TryParse(IEnumerable<KeyValuePair<string, StringValues>> query, DateTimeOffset now,
        out SessionSearchQuery? parsed, out string error)
    {
        parsed = null;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in query)
        {
            if (!Names.Contains(name)) return Fail("unknown-parameter", out error);
            if (value.Count != 1) return Fail("duplicate-parameter", out error);
            if (string.IsNullOrEmpty(value[0])) return Fail("empty-parameter", out error);
            values[name] = value[0]!;
        }
        if (!values.TryGetValue("from", out var fromText) || !values.TryGetValue("to", out var toText)) return Fail("interval-required", out error);
        if (!TryInstant(fromText, out var from) || !TryInstant(toText, out var to)) return Fail("invalid-instant", out error);
        // A client clock slightly ahead of the server (the SPA sends its own "now") is not a future query: it is clamped to the server's now.
        if (to > now + ClockSkewTolerance) return Fail("future-interval", out error);
        if (to > now) to = now;
        if (from >= to) return Fail("invalid-interval", out error);
        if (to - from > MaximumWindow || from < now - MaximumWindow) return Fail("interval-out-of-retention", out error);

        values.TryGetValue("siteId", out var site);
        values.TryGetValue("sensorId", out var sensor);
        if (site is { Length: > MaximumIdentifierLength } || sensor is { Length: > MaximumIdentifierLength }) return Fail("invalid-scope-selector", out error);
        string? sourceIp = null, destinationIp = null;
        if ((values.TryGetValue("sourceIp", out var source) && !TryAddress(source, out sourceIp))
            || (values.TryGetValue("destinationIp", out var destination) && !TryAddress(destination, out destinationIp)))
            return Fail("invalid-ip", out error);
        values.TryGetValue("protocol", out var protocol);
        if (protocol is not null && protocol is not ("TCP" or "UDP")) return Fail("invalid-protocol", out error);
        if (!TryPort(values, "sourcePort", out var sourcePort) || !TryPort(values, "destinationPort", out var destinationPort))
            return Fail("invalid-port", out error);
        var pageSize = DefaultPageSize;
        if (values.TryGetValue("pageSize", out var pageText)
            && (!Digits.IsMatch(pageText) || (pageSize = int.Parse(pageText, CultureInfo.InvariantCulture)) is < 1 or > MaximumPageSize))
            return Fail("invalid-page-size", out error);
        values.TryGetValue("cursor", out var cursor);
        if (cursor is { Length: > MaximumCursorLength }) return Fail("invalid-cursor", out error);

        // Beyond 24 hours the request must be selective: site, sensor and at least one endpoint address.
        if (to - from > ShortWindow && (site is null || sensor is null || (sourceIp is null && destinationIp is null)))
            return Fail("selectivity-required", out error);

        parsed = new SessionSearchQuery(from, to, site, sensor, sourceIp, destinationIp, protocol, sourcePort, destinationPort, pageSize, cursor);
        error = string.Empty;
        return true;
    }

    private static bool Fail(string code, out string error)
    {
        error = code;
        return false;
    }

    private static bool TryInstant(string text, out DateTimeOffset instant)
    {
        instant = default;
        return Instant.IsMatch(text) && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out instant);
    }

    // Literal addresses only; legacy short IPv4 forms and scoped IPv6 are rejected. Output matches the indexed normalized text.
    private static bool TryAddress(string text, out string? normalized)
    {
        normalized = null;
        var literal = text.Contains(':') ? !text.Contains('%') : DottedQuad.IsMatch(text);
        if (!literal || !IPAddress.TryParse(text, out var address)) return false;
        normalized = address.ToString();
        return true;
    }

    private static bool TryPort(Dictionary<string, string> values, string name, out int? port)
    {
        port = null;
        if (!values.TryGetValue(name, out var text)) return true;
        if (!Digits.IsMatch(text) || !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number > 65535) return false;
        port = number;
        return true;
    }
}
