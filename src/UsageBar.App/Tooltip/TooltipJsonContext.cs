using System.Text.Json.Serialization;
using UsageBar.Domain;

namespace UsageBar.Tooltip;

/// <summary>The payload pushed to the WebView tooltip, serialised as <c>{"cards":[...],"scale":100}</c>.</summary>
internal sealed record TooltipPayload(
    [property: JsonPropertyName("cards")] IReadOnlyList<TooltipCard> Cards,
    [property: JsonPropertyName("scale")] int Scale);

/// <summary>
/// System.Text.Json source-generation context for the tooltip payload. Card/metric property names
/// come from the <c>[JsonPropertyName]</c> attributes on <see cref="TooltipCard"/> /
/// <see cref="TooltipMetric"/>, so no naming policy is needed and output stays trim/AOT-safe.
/// </summary>
[JsonSerializable(typeof(TooltipPayload))]
internal sealed partial class TooltipJsonContext : JsonSerializerContext;
