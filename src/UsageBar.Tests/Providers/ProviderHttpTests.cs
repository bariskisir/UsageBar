using System.Net;
using UsageBar.Providers;
using Xunit;

namespace UsageBar.Tests;

public sealed class ProviderHttpTests
{
    [Fact]
    public async Task GetJsonAsync_returns_parsed_document_on_success()
    {
        using var httpClient = new HttpClient(FakeHttpMessageHandler.Json("""{ "key": "value" }""", HttpStatusCode.OK));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://test.example/api");

        using var document = await ProviderHttp.GetJsonAsync(httpClient, request, CancellationToken.None);

        Assert.True(document.RootElement.TryGetProperty("key", out var key));
        Assert.Equal("value", key.GetString());
    }

    [Fact]
    public async Task GetJsonAsync_throws_on_non_success_status()
    {
        using var httpClient = new HttpClient(FakeHttpMessageHandler.Json("{}", HttpStatusCode.InternalServerError));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://test.example/api");

        await Assert.ThrowsAsync<HttpRequestException>(
            () => ProviderHttp.GetJsonAsync(httpClient, request, CancellationToken.None));
    }

    [Fact]
    public async Task GetJsonAsync_throws_on_invalid_json()
    {
        // FakeHttpMessageHandler.Json always sends with application/json content type,
        // so we need a handler that returns invalid JSON content.
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not-json", System.Text.Encoding.UTF8, "application/json"),
        });
        using var httpClient = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://test.example/api");

        // Invalid JSON throws a System.Text.Json parse exception.
        var ex = await Record.ExceptionAsync(
            () => ProviderHttp.GetJsonAsync(httpClient, request, CancellationToken.None));
        Assert.NotNull(ex);
        Assert.Contains("Json", ex.GetType().FullName!);
    }
}
