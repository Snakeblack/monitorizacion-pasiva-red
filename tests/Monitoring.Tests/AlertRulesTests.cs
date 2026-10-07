using System.Text.Json;
using System.Text.RegularExpressions;

namespace Monitoring.Tests;

// The alert rules are data, but they are part of the product's operations contract: every rule must be actionable (cause, action,
// owner, runbook), every metric it reads must be produced by the code, and nothing in them may carry a secret.
public sealed partial class AlertRulesTests
{
    private static readonly string Root = FindRoot();
    private static readonly string[] Severities = ["page", "ticket"];
    private static readonly string[] Owners = ["operaciones-red", "plataforma", "seguridad"];
    private static readonly string[] Stages = ["probe", "ingestion", "projection", "cdc", "search", "retention"];

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Monitoring.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static JsonElement[] Rules()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "deploy", "observability", "alerts.rules.json")));
        return document.RootElement.GetProperty("groups").EnumerateArray()
            .SelectMany(group => group.GetProperty("rules").EnumerateArray().Select(rule => rule.Clone())).ToArray();
    }

    [GeneratedRegex(@"monitoring_[a-z0-9_]+")]
    private static partial Regex MetricPattern();

    [GeneratedRegex("\"(monitoring\\.[a-z0-9_.]+)\"")]
    private static partial Regex ProducedPattern();

    private static HashSet<string> ProducedMetrics() => Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
        .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        .SelectMany(path => ProducedPattern().Matches(File.ReadAllText(path)).Select(match => match.Groups[1].Value.Replace('.', '_')))
        .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void EveryAlertIsActionableWithACauseAnActionAnOwnerAndARunbookSection()
    {
        var rules = Rules();
        Assert.NotEmpty(rules);
        var runbook = File.ReadAllText(Path.Combine(Root, "docs", "runbooks", "pipeline-alerts.md"));
        foreach (var rule in rules)
        {
            var name = rule.GetProperty("alert").GetString()!;
            var labels = rule.GetProperty("labels");
            var annotations = rule.GetProperty("annotations");
            Assert.Contains(labels.GetProperty("severity").GetString(), Severities);
            Assert.Contains(labels.GetProperty("owner").GetString(), Owners);
            Assert.Contains(labels.GetProperty("stage").GetString(), Stages);
            foreach (var field in new[] { "summary", "cause", "action", "runbook" })
                Assert.False(string.IsNullOrWhiteSpace(annotations.GetProperty(field).GetString()), $"{name} lacks {field}");
            Assert.False(string.IsNullOrWhiteSpace(rule.GetProperty("expr").GetString()), name);
            Assert.True(rule.TryGetProperty("for", out _), $"{name} lacks a duration");
            Assert.Equal($"docs/runbooks/pipeline-alerts.md#{name.ToLowerInvariant()}", annotations.GetProperty("runbook").GetString());
            Assert.Contains($"\n## {name}\n", runbook, StringComparison.Ordinal);
        }
        Assert.Equal(rules.Length, rules.Select(rule => rule.GetProperty("alert").GetString()).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryStageOfThePipelineHasAtLeastOneAlert()
    {
        var stages = Rules().Select(rule => rule.GetProperty("labels").GetProperty("stage").GetString()).ToHashSet();
        Assert.Superset(Stages.ToHashSet(), stages!);
    }

    [Fact]
    public void EveryMetricAnAlertReadsIsProducedByTheCode()
    {
        var produced = ProducedMetrics();
        foreach (var rule in Rules())
        {
            foreach (Match match in MetricPattern().Matches(rule.GetProperty("expr").GetString()!))
            {
                var name = match.Value.EndsWith("_total", StringComparison.Ordinal) ? match.Value[..^"_total".Length] : match.Value;
                Assert.True(produced.Contains(name), $"{rule.GetProperty("alert")} reads {match.Value}, which no code produces");
            }
        }
    }

    [Fact]
    public void NoRuleCarriesASecretAnAddressOrAConnectionString()
    {
        var text = File.ReadAllText(Path.Combine(Root, "deploy", "observability", "alerts.rules.json"))
            + File.ReadAllText(Path.Combine(Root, "docs", "runbooks", "pipeline-alerts.md"));
        foreach (var forbidden in new[] { "password", "secret", "token", "apikey", "Host=", "Server=", "://" })
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(@"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b", text);
    }
}
