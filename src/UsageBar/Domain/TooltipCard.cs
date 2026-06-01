using System.Text.Json.Serialization;

namespace UsageBar.Domain;

internal sealed record TooltipMetric(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("percent")] double Percent,
    [property: JsonPropertyName("detail")] string Detail);

internal sealed record TooltipCard(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("plan")] string? Plan,
    [property: JsonPropertyName("metrics")] IReadOnlyList<TooltipMetric> Metrics,
    [property: JsonPropertyName("lines")] IReadOnlyList<string> Lines);
