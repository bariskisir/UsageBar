using System.Net;
using UsageBar.Core.Providers;
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

    [Fact]
    public async Task GetJsonWithBearerAsync_sends_request_with_correct_bearer_token()
    {
        var handler = FakeHttpMessageHandler.Json("""{ "status": "ok" }""", HttpStatusCode.OK);
        using var httpClient = new HttpClient(handler);

        using var document = await ProviderHttp.GetJsonWithBearerAsync(httpClient, "https://test.example/api/v2", "my-secret-key", CancellationToken.None);

        Assert.Single(handler.Requests);
        var request = handler.Requests[0];
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://test.example/api/v2", request.RequestUri?.ToString());
        Assert.NotNull(request.Headers.Authorization);
        Assert.Equal("Bearer", request.Headers.Authorization.Scheme);
        Assert.Equal("my-secret-key", request.Headers.Authorization.Parameter);
        
        Assert.True(document.RootElement.TryGetProperty("status", out var statusProp));
        Assert.Equal("ok", statusProp.GetString());
    }

    [Fact]
    public async Task GetJsonWithBearerAsync_throws_on_non_success_status()
    {
        var handler = FakeHttpMessageHandler.Json("{}", HttpStatusCode.Unauthorized);
        using var httpClient = new HttpClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => ProviderHttp.GetJsonWithBearerAsync(httpClient, "https://test.example/api/v2", "invalid-key", CancellationToken.None));
    }
}
