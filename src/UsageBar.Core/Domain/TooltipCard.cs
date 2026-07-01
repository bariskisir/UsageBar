using System.Text.Json.Serialization;

namespace UsageBar.Domain;

/// <summary>
/// One metric row inside a tooltip card. Property names are serialised verbatim to
/// match the DOM builder in <c>Assets/index.html</c>.
/// </summary>
public sealed record TooltipMetric(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("percent")] double Percent,
    [property: JsonPropertyName("detail")] string Detail);

/// <summary>
/// A provider card pushed to the WebView2 tooltip. Serialised as part of
/// <c>{"cards":[...]}</c>; keys must match <c>Assets/index.html</c>.
/// </summary>
public sealed record TooltipCard(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("plan")] string? Plan,
    [property: JsonPropertyName("metrics")] IReadOnlyList<TooltipMetric> Metrics,
    [property: JsonPropertyName("lines")] IReadOnlyList<string> Lines,
    [property: JsonPropertyName("hide")] bool Hide = false);
