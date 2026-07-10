using System.Text.Json;
using UsageBar.Core.Providers;
using Xunit;

namespace UsageBar.Tests;

public sealed class ProviderJsonTests
{
    [Fact]
    public void GetDecimal_reads_number_value()
    {
        using var document = JsonDocument.Parse("""{ "amount": 12.34 }""");
        var value = ProviderJson.GetDecimal(document.RootElement, "amount");
        Assert.Equal(12.34m, value);
    }

    [Fact]
    public void GetDecimal_reads_string_value()
    {
        using var document = JsonDocument.Parse("""{ "amount": "56.78" }""");
        var value = ProviderJson.GetDecimal(document.RootElement, "amount");
        Assert.Equal(56.78m, value);
    }

    [Fact]
    public void GetDecimal_returns_null_for_missing_property()
    {
        using var document = JsonDocument.Parse("""{ "other": 1 }""");
        Assert.Null(ProviderJson.GetDecimal(document.RootElement, "amount"));
    }

    [Fact]
    public void GetDecimal_returns_null_for_non_numeric_string()
    {
        using var document = JsonDocument.Parse("""{ "amount": "not-a-number" }""");
        Assert.Null(ProviderJson.GetDecimal(document.RootElement, "amount"));
    }

    [Fact]
    public void GetDecimal_returns_null_for_boolean()
    {
        using var document = JsonDocument.Parse("""{ "amount": true }""");
        Assert.Null(ProviderJson.GetDecimal(document.RootElement, "amount"));
    }

    [Fact]
    public void GetDouble_reads_number_value()
    {
        using var document = JsonDocument.Parse("""{ "percent": 53.5 }""");
        var value = ProviderJson.GetDouble(document.RootElement, "percent");
        Assert.Equal(53.5, value);
    }

    [Fact]
    public void GetDouble_reads_string_value()
    {
        using var document = JsonDocument.Parse("""{ "percent": "87.3" }""");
        var value = ProviderJson.GetDouble(document.RootElement, "percent");
        Assert.Equal(87.3, value);
    }

    [Fact]
    public void GetDouble_returns_null_for_missing_property()
    {
        using var document = JsonDocument.Parse("""{ "other": 1 }""");
        Assert.Null(ProviderJson.GetDouble(document.RootElement, "percent"));
    }

    [Fact]
    public void GetString_returns_first_matching_property()
    {
        using var document = JsonDocument.Parse("""{ "id": "first", "project_id": "second" }""");
        Assert.Equal("first", ProviderJson.GetString(document.RootElement, "id", "project_id"));
    }

    [Fact]
    public void GetString_falls_back_to_second_name()
    {
        using var document = JsonDocument.Parse("""{ "project_id": "p123" }""");
        Assert.Equal("p123", ProviderJson.GetString(document.RootElement, "id", "project_id"));
    }

    [Fact]
    public void GetString_returns_null_when_none_match()
    {
        using var document = JsonDocument.Parse("""{ "name": "test" }""");
        Assert.Null(ProviderJson.GetString(document.RootElement, "id", "project_id"));
    }

    [Fact]
    public void GetString_returns_null_for_non_string_property()
    {
        using var document = JsonDocument.Parse("""{ "id": 123 }""");
        Assert.Null(ProviderJson.GetString(document.RootElement, "id"));
    }

    [Fact]
    public void TryGetProperty_finds_existing_property()
    {
        using var document = JsonDocument.Parse("""{ "key": "value" }""");
        Assert.True(ProviderJson.TryGetProperty(document.RootElement, "key", out var property));
        Assert.Equal("value", property.GetString());
    }

    [Fact]
    public void TryGetProperty_returns_false_for_missing_property()
    {
        using var document = JsonDocument.Parse("""{ "key": "value" }""");
        Assert.False(ProviderJson.TryGetProperty(document.RootElement, "missing", out _));
    }

    [Fact]
    public void TryGetProperty_returns_false_for_non_object()
    {
        using var document = JsonDocument.Parse("42");
        Assert.False(ProviderJson.TryGetProperty(document.RootElement, "key", out _));
    }
}
