using System.Reflection;

namespace UsageBar.Core.Infrastructure;

/// <summary>Builds a self-contained WebView document from separately editable embedded assets.</summary>
internal static class EmbeddedPageLoader
{
    public static string Load(
        Assembly assembly,
        string htmlResource,
        string cssResource,
        string scriptResource,
        string cssToken,
        string scriptToken) =>
        Read(assembly, htmlResource)
            .Replace(cssToken, Read(assembly, cssResource), StringComparison.Ordinal)
            .Replace(scriptToken, Read(assembly, scriptResource), StringComparison.Ordinal);

    private static string Read(Assembly assembly, string resourceName)
    {
        var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
