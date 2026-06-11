using System.Net;
using System.Text;

namespace UsageBar.Tests;

/// <summary>Test double that returns canned HTTP responses and records requests.</summary>
internal sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>Always responds with the given JSON body and status.</summary>
    public static FakeHttpMessageHandler Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

    /// <summary>Responds with the given JSON bodies in order.</summary>
    public static FakeHttpMessageHandler Sequence(params (string Json, HttpStatusCode Status)[] responses)
    {
        var queue = new Queue<(string Json, HttpStatusCode Status)>(responses);
        return new FakeHttpMessageHandler(_ =>
        {
            var response = queue.Dequeue();
            return new HttpResponseMessage(response.Status)
            {
                Content = new StringContent(response.Json, Encoding.UTF8, "application/json"),
            };
        });
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(responder(request));
    }
}
