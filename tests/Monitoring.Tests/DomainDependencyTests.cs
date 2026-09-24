using System.Reflection;

namespace Monitoring.Tests;

public sealed class DomainDependencyTests
{
    [Fact]
    public void DomainProjectDoesNotReferencePersistence()
    {
        var domainAssemblyPath = Path.Combine(AppContext.BaseDirectory, "Monitoring.Domain.dll");

        Assert.True(File.Exists(domainAssemblyPath), "The domain assembly must be built and referenced by the test project.");

        var domainAssembly = Assembly.LoadFrom(domainAssemblyPath);
        var referencedAssemblies = domainAssembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty);

        Assert.DoesNotContain("Monitoring.Persistence", referencedAssemblies);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", referencedAssemblies);
        Assert.DoesNotContain("Npgsql", referencedAssemblies);
    }
}
