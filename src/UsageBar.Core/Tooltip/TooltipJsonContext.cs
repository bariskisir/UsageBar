using System.Text.Json.Serialization;
using UsageBar.Core.Domain;

namespace UsageBar.Core.Tooltip;

/// <summary>The payload pushed to the WebView tooltip, serialised as <c>{"cards":[...],"scale":100}</c>.</summary>
internal sealed record TooltipPayload(
    [property: JsonPropertyName("cards")] IReadOnlyList<TooltipCard> Cards,
    [property: JsonPropertyName("scale")] int Scale);

internal sealed record TooltipInboundMessage(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("width")] int? Width,
    [property: JsonPropertyName("height")] int? Height);

/// <summary>
/// System.Text.Json source-generation context for the tooltip payload. Card/metric property names
/// come from the <c>[JsonPropertyName]</c> attributes on <see cref="TooltipCard"/> /
/// <see cref="TooltipMetric"/>, so no naming policy is needed and output stays trim/AOT-safe.
/// </summary>
[JsonSerializable(typeof(TooltipPayload))]
[JsonSerializable(typeof(TooltipInboundMessage))]
internal sealed partial class TooltipJsonContext : JsonSerializerContext;
