using System.Text.Json;

namespace UsageBar.Core.Providers;

/// <summary>
/// Shared HTTP plumbing for providers: send a request, ensure success, and parse the JSON body.
/// Centralised so every provider's fetch path behaves identically (status check, streaming parse).
/// </summary>
internal static class ProviderHttp
{
    /// <summary>
    /// Sends <paramref name="request"/> and parses the response body as JSON. Throws
    /// <see cref="HttpRequestException"/> on non-success status codes and
    /// <see cref="JsonException"/> on malformed bodies (the aggregator logs and isolates both).
    /// The caller owns (disposes) the returned document.
    /// </summary>
    public static async Task<JsonDocument> GetJsonAsync(
        HttpClient httpClient,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs a GET request to <paramref name="url"/> with <paramref name="apiKey"/> in the
    /// Authorization header as a Bearer token, and parses the response body as JSON.
    /// </summary>
    public static async Task<JsonDocument> GetJsonWithBearerAsync(
        HttpClient httpClient,
        string url,
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        return await GetJsonAsync(httpClient, request, cancellationToken).ConfigureAwait(false);
    }
}
