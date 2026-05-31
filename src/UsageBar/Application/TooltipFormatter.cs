using UsageBar.Domain;

namespace UsageBar.Application;

internal static class TooltipFormatter
{
    public static string Format(IReadOnlyList<UsageBlock> blocks)
    {
        if (blocks.Count == 0)
        {
            return "UsageBar\nNo configured providers";
        }

        var lines = new List<string>(blocks.Count * 2);

        foreach (var block in blocks)
        {
            if (block.Inline)
            {
                lines.Add($"{block.Label} {block.Value}");
                continue;
            }

            lines.Add(block.Label);
            lines.Add(block.Value);
        }

        var text = string.Join(Environment.NewLine, lines);
        return text.Length <= 127 ? text : text[..127];
    }
}
