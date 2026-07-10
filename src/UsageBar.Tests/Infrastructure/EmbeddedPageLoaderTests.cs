using UsageBar.Core.Infrastructure;
using Xunit;

namespace UsageBar.Tests;

public sealed class EmbeddedPageLoaderTests
{
    [Fact]
    public void Tooltip_document_combines_template_css_and_script_without_tokens()
    {
        var assembly = typeof(EmbeddedPageLoader).Assembly;

        var document = EmbeddedPageLoader.Load(
            assembly,
            "UsageBar.Core.Frontend.index.html",
            "UsageBar.Core.Frontend.tooltip.css",
            "UsageBar.Core.Frontend.tooltip.js",
            "{{TOOLTIP_CSS}}",
            "{{TOOLTIP_JS}}");

        Assert.DoesNotContain("{{TOOLTIP_CSS}}", document, StringComparison.Ordinal);
        Assert.DoesNotContain("{{TOOLTIP_JS}}", document, StringComparison.Ordinal);
        Assert.Contains(".card", document, StringComparison.Ordinal);
        Assert.Contains("window.__render", document, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_document_combines_template_css_and_script_without_tokens()
    {
        var assembly = typeof(EmbeddedPageLoader).Assembly;

        var document = EmbeddedPageLoader.Load(
            assembly,
            "UsageBar.Core.Frontend.settings.html",
            "UsageBar.Core.Frontend.settings.css",
            "UsageBar.Core.Frontend.settings.js",
            "{{SETTINGS_CSS}}",
            "{{SETTINGS_JS}}");

        Assert.DoesNotContain("{{SETTINGS_CSS}}", document, StringComparison.Ordinal);
        Assert.DoesNotContain("{{SETTINGS_JS}}", document, StringComparison.Ordinal);
        Assert.Contains(".settings", document, StringComparison.Ordinal);
        Assert.Contains("window.__loadSettings", document, StringComparison.Ordinal);
    }
}
