using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using UsageBar.Core.Domain;
using UsageBar.Core.Providers;

namespace UsageBar.Core.Application;

/// <summary>Discovers the lightest available model and sends a one-token dot prompt.</summary>
internal sealed class WindowStartRequestSender(
    HttpClient httpClient,
    ICodexAuthReader codexAuthReader,
    IClaudeAuthReader claudeAuthReader,
    IAntigravityAuthReader antigravityAuthReader,
    ILogger<WindowStartRequestSender> logger) : IWindowStartRequestSender
{
    private const string CodexModelsEndpoint = "https://chatgpt.com/backend-api/codex/models";
    private const string CodexResponsesEndpoint = "https://chatgpt.com/backend-api/codex/responses";
    private const string CodexLatestEndpoint = "https://registry.npmjs.org/@openai/codex/latest";
    private const string ClaudeModelsEndpoint = "https://api.anthropic.com/v1/models";
    private const string ClaudeMessagesEndpoint = "https://api.anthropic.com/v1/messages?beta=true";
    private const string ClaudeMetaBeta = "oauth-2025-04-20";
    private const string ClaudeChatBeta = "claude-code-20250219,oauth-2025-04-20";
    private const string ClaudeSystemPrompt = "You are Claude Code, Anthropic's official CLI for Claude.";
    private const string AntigravityBaseEndpoint = "https://daily-cloudcode-pa.googleapis.com";

    public Task StartAsync(string providerName, string smallModelSelector, CancellationToken cancellationToken) => providerName.ToLowerInvariant() switch
    {
        "codex" => StartCodexAsync(smallModelSelector, cancellationToken),
        "claude" => StartClaudeAsync(smallModelSelector, cancellationToken),
        "antigravity" => StartAntigravityAsync(smallModelSelector, cancellationToken),
        _ => throw new InvalidOperationException($"Window starting is not supported for {providerName}."),
    };

    private async Task StartCodexAsync(string smallModelSelector, CancellationToken cancellationToken)
    {
        var auth = codexAuthReader.Read() ?? throw new ProviderException("Codex credentials were not found.");
        var version = await TryGetCodexVersionAsync(cancellationToken).ConfigureAwait(false);
        var modelsUrl = string.IsNullOrWhiteSpace(version)
            ? CodexModelsEndpoint
            : $"{CodexModelsEndpoint}?client_version={Uri.EscapeDataString(version)}";

        using var modelsRequest = new HttpRequestMessage(HttpMethod.Get, modelsUrl);
        AddCodexHeaders(modelsRequest, auth, "application/json", jsonContent: false);
        using var modelsDocument = await SendForJsonAsync(modelsRequest, "Codex model catalog", cancellationToken).ConfigureAwait(false);
        var models = ParseCodexModels(modelsDocument.RootElement);
        var model = SelectLightest(models, smallModelSelector);

        var payload = new JsonObject
        {
            ["model"] = model.Id,
            ["input"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "message",
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        new JsonObject { ["type"] = "input_text", ["text"] = "." },
                    },
                },
            },
            ["stream"] = true,
            ["store"] = false,
            ["instructions"] = ".",
            ["text"] = new JsonObject { ["verbosity"] = "low" },
        };
        var effort = model.ReasoningLevels.OrderBy(EffortScore).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(effort))
        {
            payload["reasoning"] = new JsonObject { ["effort"] = effort, ["summary"] = "auto" };
        }

        using var request = JsonRequest(HttpMethod.Post, CodexResponsesEndpoint, payload);
        AddCodexHeaders(request, auth, "text/event-stream", jsonContent: true);
        var responseBody = await SendAndDrainAsync(request, "Codex window start", cancellationToken).ConfigureAwait(false);
        LogCompletion("Codex", model.Id, responseBody, instructions: ".");
    }

    private async Task StartClaudeAsync(string smallModelSelector, CancellationToken cancellationToken)
    {
        var auth = claudeAuthReader.Read() ?? throw new ProviderException("Claude credentials were not found.");
        using var modelsRequest = new HttpRequestMessage(HttpMethod.Get, ClaudeModelsEndpoint);
        AddClaudeHeaders(modelsRequest, auth.AccessToken, ClaudeMetaBeta, "application/json", jsonContent: false);
        using var modelsDocument = await SendForJsonAsync(modelsRequest, "Claude model catalog", cancellationToken).ConfigureAwait(false);
        var model = SelectLightest(ParseClaudeModels(modelsDocument.RootElement), smallModelSelector);

        var payload = new JsonObject
        {
            ["model"] = model.Id,
            ["max_tokens"] = 1,
            ["stream"] = false,
            ["system"] = new JsonArray
            {
                new JsonObject { ["type"] = "text", ["text"] = ClaudeSystemPrompt },
            },
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        new JsonObject { ["type"] = "text", ["text"] = "." },
                    },
                },
            },
        };

        using var request = JsonRequest(HttpMethod.Post, ClaudeMessagesEndpoint, payload);
        AddClaudeHeaders(request, auth.AccessToken, ClaudeChatBeta, "application/json", jsonContent: true);
        var responseBody = await SendAndDrainAsync(request, "Claude window start", cancellationToken).ConfigureAwait(false);
        LogCompletion("Claude", model.Id, responseBody, instructions: ClaudeSystemPrompt);
    }

    private async Task StartAntigravityAsync(string smallModelSelector, CancellationToken cancellationToken)
    {
        var auth = antigravityAuthReader.Read() ?? throw new ProviderException("Antigravity credentials were not found.");
        var userAgent = AntigravityUserAgent();
        var projectPayload = new JsonObject
        {
            ["metadata"] = new JsonObject { ["ideType"] = "ANTIGRAVITY" },
        };
        using var projectRequest = JsonRequest(HttpMethod.Post, $"{AntigravityBaseEndpoint}/v1internal:loadCodeAssist", projectPayload);
        AddAntigravityHeaders(projectRequest, auth.AccessToken, userAgent, "application/json");
        using var projectDocument = await SendForJsonAsync(projectRequest, "Antigravity project lookup", cancellationToken).ConfigureAwait(false);
        var projectId = ReadString(projectDocument.RootElement, "cloudaicompanionProject")
            ?? throw new ProviderException("Antigravity project lookup did not return a project id.");

        var modelsPayload = new JsonObject { ["project"] = projectId };
        using var modelsRequest = JsonRequest(HttpMethod.Post, $"{AntigravityBaseEndpoint}/v1internal:fetchAvailableModels", modelsPayload);
        AddAntigravityHeaders(modelsRequest, auth.AccessToken, userAgent, "application/json");
        using var modelsDocument = await SendForJsonAsync(modelsRequest, "Antigravity model catalog", cancellationToken).ConfigureAwait(false);
        var model = SelectLightest(ParseAntigravityModels(modelsDocument.RootElement), smallModelSelector);

        var requestId = $"usagebar-{Guid.NewGuid():N}";
        var payload = new JsonObject
        {
            ["project"] = projectId,
            ["requestId"] = requestId,
            ["request"] = new JsonObject
            {
                ["contents"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["role"] = "user",
                        ["parts"] = new JsonArray { new JsonObject { ["text"] = "." } },
                    },
                },
                ["generationConfig"] = new JsonObject
                {
                    ["maxOutputTokens"] = 1,
                    ["thinkingConfig"] = new JsonObject
                    {
                        ["includeThoughts"] = false,
                        ["thinkingBudget"] = 0,
                    },
                },
                ["sessionId"] = $"-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            },
            ["model"] = model.Id,
            ["userAgent"] = "antigravity",
            ["requestType"] = "checkpoint",
        };

        using var request = JsonRequest(HttpMethod.Post, $"{AntigravityBaseEndpoint}/v1internal:streamGenerateContent?alt=sse", payload);
        AddAntigravityHeaders(request, auth.AccessToken, userAgent, "text/event-stream");
        var responseBody = await SendAndDrainAsync(request, "Antigravity window start", cancellationToken).ConfigureAwait(false);
        LogCompletion("Antigravity", model.Id, responseBody, instructions: null);
    }

    private async Task<string?> TryGetCodexVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, CodexLatestEndpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var document = await SendForJsonAsync(request, "Codex client version", cancellationToken).ConfigureAwait(false);
            return ReadString(document.RootElement, "version");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (ProviderException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<JsonDocument> SendForJsonAsync(
        HttpRequestMessage request,
        string operation,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, operation, cancellationToken).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> SendAndDrainAsync(
        HttpRequestMessage request,
        string operation,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, operation, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private void LogCompletion(string provider, string model, string responseBody, string? instructions)
    {
        var usage = ParseTokenUsage(responseBody);
        logger.LogInformation(
            "{Provider} warm-window request completed: model={Model}; prompt={Prompt}; instructions={Instructions}; inputTokens={InputTokens}; outputTokens={OutputTokens}; totalTokens={TotalTokens}.",
            provider,
            model,
            ".",
            instructions,
            usage?.InputTokens,
            usage?.OutputTokens,
            usage?.TotalTokens);
    }

    private static TokenUsage? ParseTokenUsage(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        TokenUsage? latest = null;
        if (TryParseUsageDocument(responseBody, out var direct))
        {
            latest = direct;
        }

        foreach (var line in responseBody.Split('\n'))
        {
            var trimmed = line.Trim();
            var payload = trimmed.StartsWith("data:", StringComparison.Ordinal)
                ? trimmed[5..].Trim()
                : null;
            if (string.IsNullOrWhiteSpace(payload) || payload == "[DONE]")
            {
                continue;
            }

            if (TryParseUsageDocument(payload, out var streamed))
            {
                latest = streamed;
            }
        }

        return latest;
    }

    private static bool TryParseUsageDocument(string json, out TokenUsage? usage)
    {
        usage = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!TryFindUsage(document.RootElement, depth: 0, out var usageElement))
            {
                return false;
            }

            var input = ReadInteger(usageElement, "input_tokens", "inputTokens", "promptTokenCount", "prompt_token_count");
            var output = ReadInteger(usageElement, "output_tokens", "outputTokens", "candidatesTokenCount", "outputTokenCount");
            var total = ReadInteger(usageElement, "total_tokens", "totalTokens", "totalTokenCount");
            if (input is null && output is null && total is null)
            {
                return false;
            }

            usage = new TokenUsage(input, output, total ?? AddNullable(input, output));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryFindUsage(JsonElement element, int depth, out JsonElement usage)
    {
        if (depth > 6)
        {
            usage = default;
            return false;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "usage", "usageMetadata" })
            {
                if (TryGetProperty(element, name, out usage) && usage.ValueKind == JsonValueKind.Object)
                {
                    return true;
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                if (TryFindUsage(property.Value, depth + 1, out usage))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindUsage(item, depth + 1, out usage))
                {
                    return true;
                }
            }
        }

        usage = default;
        return false;
    }

    private static long? ReadInteger(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetProperty(element, name, out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt64(out var number))
            {
                return number;
            }
        }

        return null;
    }

    private static long? AddNullable(long? left, long? right) => left is not null && right is not null ? left + right : null;

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new ProviderException($"{operation} failed with HTTP {(int)response.StatusCode}: {body[..Math.Min(body.Length, 240)]}");
    }

    private static HttpRequestMessage JsonRequest(HttpMethod method, string endpoint, JsonNode payload) =>
        new(method, endpoint)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };

    private static void AddCodexHeaders(HttpRequestMessage request, CodexAuth auth, string accept, bool jsonContent)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        request.Headers.TryAddWithoutValidation("originator", "codex_cli_rs");
        request.Headers.TryAddWithoutValidation("User-Agent", "UsageBar");
        if (jsonContent)
        {
            request.Headers.TryAddWithoutValidation("OpenAI-Beta", "responses=experimental");
        }

        if (!string.IsNullOrWhiteSpace(auth.AccountId))
        {
            request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", auth.AccountId);
        }
    }

    private static void AddClaudeHeaders(
        HttpRequestMessage request,
        string accessToken,
        string beta,
        string accept,
        bool jsonContent)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        request.Headers.TryAddWithoutValidation("anthropic-beta", beta);
        request.Headers.TryAddWithoutValidation("anthropic-dangerous-direct-browser-access", "true");
        request.Headers.TryAddWithoutValidation("x-app", "cli");
        request.Headers.TryAddWithoutValidation("User-Agent", "claude-cli (external, cli)");
        if (jsonContent && request.Content is not null)
        {
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
    }

    private static void AddAntigravityHeaders(HttpRequestMessage request, string accessToken, string userAgent, string accept)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
    }

    private static IReadOnlyList<DynamicModel> ParseCodexModels(JsonElement root)
    {
        if (!TryGetProperty(root, "models", out var models) || models.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return models.EnumerateArray().Select(model =>
        {
            var id = ReadString(model, "slug") ?? ReadString(model, "model") ?? ReadString(model, "id") ?? string.Empty;
            var levels = TryGetProperty(model, "supported_reasoning_levels", out var reasoning) && reasoning.ValueKind == JsonValueKind.Array
                ? reasoning.EnumerateArray().Select(item => ReadString(item, "effort")).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).ToArray()
                : [];
            return new DynamicModel(
                id,
                ReadString(model, "display_name") ?? id,
                ReadString(model, "description") ?? string.Empty,
                ReadBool(model, "hidden") || string.Equals(ReadString(model, "visibility"), "hide", StringComparison.OrdinalIgnoreCase),
                levels);
        }).Where(model => !string.IsNullOrWhiteSpace(model.Id)).ToArray();
    }

    private static IReadOnlyList<DynamicModel> ParseClaudeModels(JsonElement root)
    {
        if (!TryGetProperty(root, "data", out var models) || models.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return models.EnumerateArray()
            .Select(model =>
            {
                var id = ReadString(model, "id") ?? string.Empty;
                var supportsEffort = TryGetProperty(model, "capabilities", out var capabilities)
                    && TryGetProperty(capabilities, "effort", out var effort)
                    && ReadBool(effort, "supported");
                return new DynamicModel(id, ReadString(model, "display_name") ?? id, string.Empty, false, supportsEffort ? ["low"] : []);
            })
            .Where(model => !string.IsNullOrWhiteSpace(model.Id))
            .ToArray();
    }

    private static IReadOnlyList<DynamicModel> ParseAntigravityModels(JsonElement root)
    {
        if (!TryGetProperty(root, "models", out var models) || models.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return models.EnumerateObject()
            .Where(property => !ReadBool(property.Value, "isInternal"))
            .Select(property => new DynamicModel(
                property.Name,
                ReadString(property.Value, "displayName") ?? property.Name,
                string.Empty,
                false,
                ReadBool(property.Value, "supportsThinking") ? ["low"] : []))
            .ToArray();
    }

    private static DynamicModel SelectLightest(IReadOnlyList<DynamicModel> models, string smallModelSelector)
    {
        var visible = models.Where(model => !model.Hidden).ToArray();
        if (visible.Length == 0)
        {
            throw new ProviderException("The dynamic model catalog did not contain a usable model.");
        }

        var selectors = smallModelSelector
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(selector => !string.IsNullOrWhiteSpace(selector));
        foreach (var selector in selectors)
        {
            var match = visible
                .Where(model => $"{model.Id} {model.DisplayName} {model.Description}".Contains(selector, StringComparison.OrdinalIgnoreCase))
                .OrderBy(model => model.ReasoningLevels.Count > 0)
                .ThenBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (match is not null)
            {
                return match;
            }
        }

        return visible
            .OrderBy(model => model.ReasoningLevels.Count > 0)
            .ThenBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static int EffortScore(string value) => value.ToLowerInvariant() switch
    {
        "none" => 0,
        "minimal" => 1,
        "low" => 2,
        "medium" => 3,
        "high" => 4,
        "xhigh" => 5,
        "max" => 6,
        _ => 10,
    };

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static string? ReadString(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBool(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();

    private static string AntigravityUserAgent()
    {
        var os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "darwin" : "linux";
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "amd64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "386",
            Architecture.Arm => "arm",
            var value => value.ToString().ToLowerInvariant(),
        };
        return $"antigravity/cli/UsageBar (aidev_client; os_type={os}; arch={architecture}; auth_method=consumer)";
    }

    private sealed record DynamicModel(
        string Id,
        string DisplayName,
        string Description,
        bool Hidden,
        IReadOnlyList<string> ReasoningLevels);

    private sealed record TokenUsage(long? InputTokens, long? OutputTokens, long? TotalTokens);

}
