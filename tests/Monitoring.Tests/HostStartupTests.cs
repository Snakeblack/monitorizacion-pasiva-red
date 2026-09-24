using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Monitoring.Tests;

public sealed class HostStartupTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HostStartupTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HostStartsWithoutPersistenceAndReportsLiveness()
    {
        var response = await _client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
