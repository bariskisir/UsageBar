namespace UsageBar.Domain;

/// <summary>Associates a provider with its subscription/plan label (e.g. "Pro", "Max", "Free").</summary>
public sealed record ProviderPlan(string ProviderName, string Plan);
