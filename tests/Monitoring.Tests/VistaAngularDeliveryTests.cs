using System.Diagnostics;

namespace Monitoring.Tests;

public sealed class VistaAngularDeliveryTests
{
    [Fact]
    public void OpenspecConfigDeclaresAngularNpmAndUiGate()
    {
        var yaml = Normalize(File.ReadAllText(Path.Combine(RepoRoot(), "openspec", "config.yaml")));

        Assert.Contains("package_managers: [dotnet, npm]", yaml, StringComparison.Ordinal);
        Assert.Contains("- name: angular\n    version: \"22.2.0\"", yaml, StringComparison.Ordinal);
        Assert.Contains(
            "  ui:\n    required: true\n    on_fail: halt\n    command: npm --prefix src/monitoring-web test\n    timeout_ms: 300000",
            yaml,
            StringComparison.Ordinal);
        Assert.Contains("command: dotnet test Monitoring.slnx", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void CiWorkflowPinsNodeAndRunsAngularTests()
    {
        var yaml = Normalize(File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "ci.yml")));

        Assert.Contains("timeout-minutes: 15", yaml, StringComparison.Ordinal);
        Assert.Contains("dotnet test Monitoring.slnx --no-build", yaml, StringComparison.Ordinal);
        Assert.Contains("node-version: \"24.16.0\"", yaml, StringComparison.Ordinal);
        Assert.Contains("cache: npm", yaml, StringComparison.Ordinal);
        Assert.Contains("cache-dependency-path: src/monitoring-web/package-lock.json", yaml, StringComparison.Ordinal);
        Assert.Contains("npm ci", yaml, StringComparison.Ordinal);
        Assert.Contains("npm test", yaml, StringComparison.Ordinal);
        Assert.Contains("working-directory: src/monitoring-web", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("--watch=false", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CiVerifyJobStopsBeforePublishWhenAngularViewCheckFails()
    {
        var yaml = Normalize(File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "ci.yml")));
        var viewCheck = yaml.IndexOf("- name: Test Angular suite", StringComparison.Ordinal);
        var publish = yaml.IndexOf("- name: Publish host", StringComparison.Ordinal);
        Assert.True(viewCheck >= 0, yaml);
        Assert.True(publish > viewCheck, yaml);

        var span = yaml[viewCheck..publish];
        Assert.Contains("run: npm test", span, StringComparison.Ordinal);
        Assert.Contains("working-directory: src/monitoring-web", span, StringComparison.Ordinal);
        Assert.DoesNotContain("continue-on-error", span, StringComparison.Ordinal);

        var dotnetTest = yaml.IndexOf("dotnet test Monitoring.slnx --no-build", StringComparison.Ordinal);
        var npmCi = yaml.IndexOf("npm ci", StringComparison.Ordinal);
        Assert.True(dotnetTest >= 0 && npmCi > dotnetTest, yaml);
        var angularBuild = Path.Combine(RepoRoot(), "src", "monitoring-web", "node_modules", "@angular", "build");
        if (!Directory.Exists(angularBuild))
        {
            return;
        }

        var sentinel = Path.Combine(Path.GetTempPath(), "ci-publish-sentinel-" + Guid.NewGuid().ToString("N"));
        if (File.Exists(sentinel))
        {
            File.Delete(sentinel);
        }

        try
        {
            var viewCheckCommand = "npx ng test --watch=false --include=src/app/this-file-does-not-exist.spec.ts";
            var start = new ProcessStartInfo
            {
                WorkingDirectory = Path.Combine(RepoRoot(), "src", "monitoring-web"),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            if (OperatingSystem.IsWindows())
            {
                start.FileName = "cmd.exe";
                start.Arguments = "/c " + viewCheckCommand + " && echo published> \"" + sentinel + "\"";
            }
            else
            {
                start.FileName = "sh";
                start.ArgumentList.Add("-c");
                start.ArgumentList.Add(viewCheckCommand + " && echo published > '" + sentinel.Replace("'", "'\\''", StringComparison.Ordinal) + "'");
            }

            using var process = Process.Start(start) ?? throw new InvalidOperationException(start.FileName);
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                Assert.Fail(await stdoutTask + await stderrTask);
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var output = stdout + stderr;
            Assert.Contains("No tests found matching", output, StringComparison.Ordinal);
            Assert.True(process.ExitCode != 0, output);
            Assert.False(File.Exists(sentinel), output);
        }
        finally
        {
            if (File.Exists(sentinel))
            {
                File.Delete(sentinel);
            }
        }
    }

    [Fact]
    public void ReadmeDocumentsProxyTrustedScopeAndNonPublication()
    {
        var readme = Normalize(File.ReadAllText(Path.Combine(RepoRoot(), "README.md")));

        Assert.Contains("src/monitoring-web", readme, StringComparison.Ordinal);
        Assert.Contains("/api", readme, StringComparison.Ordinal);
        Assert.Contains("http://127.0.0.1:5080", readme, StringComparison.Ordinal);
        Assert.Contains("TrustedSessionRead:SiteId", readme, StringComparison.Ordinal);
        Assert.Contains("TrustedSessionRead:SensorId", readme, StringComparison.Ordinal);
        Assert.Contains("TrustedSessionRead__SiteId", readme, StringComparison.Ordinal);
        Assert.Contains("TrustedSessionRead__SensorId", readme, StringComparison.Ordinal);
        Assert.Contains("no se publica", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void CiWorkflowPublishesHostAndRejectsSpaTree()
    {
        var yaml = Normalize(File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "ci.yml")));

        Assert.Contains("dotnet publish src/Monitoring.Host --no-restore -o publish-host", yaml, StringComparison.Ordinal);
        Assert.Contains("publish-host/wwwroot", yaml, StringComparison.Ordinal);
        Assert.Contains("iname '*monitoring-web*'", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishedHostDoesNotIncludeWwwrootOrMonitoringWebArtifacts()
    {
        var output = Path.Combine(Path.GetTempPath(), "monitoring-host-publish-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = RepoRoot(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add("publish");
            start.ArgumentList.Add(Path.Combine("src", "Monitoring.Host"));
            start.ArgumentList.Add("--no-restore");
            start.ArgumentList.Add("-o");
            start.ArgumentList.Add(output);

            using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                Assert.Fail(await stdoutTask + await stderrTask);
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            Assert.True(process.ExitCode == 0, stderr + stdout);

            Assert.False(Directory.Exists(Path.Combine(output, "wwwroot")));
            var hits = Directory.EnumerateFileSystemEntries(output, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(output, path))
                .Where(relative =>
                    relative.Contains("wwwroot", StringComparison.OrdinalIgnoreCase)
                    || relative.Contains("monitoring-web", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            Assert.Empty(hits);
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Monitoring.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Monitoring.slnx");
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);
}
