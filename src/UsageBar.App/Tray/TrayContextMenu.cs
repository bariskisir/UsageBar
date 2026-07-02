using UsageBar.Application;
using UsageBar.Configuration;
using UsageBar.Domain;
using UsageBar.Providers;

namespace UsageBar.Tray;

internal sealed class TrayContextMenu(ISettingsStore settings) : ITrayContextMenu
{
    private const uint TestNotificationCommandId = 1000;
    private const uint RefreshCommandId = 1001;
    private const uint ExitCommandId = 1002;
    private const uint RefreshEveryBase = 2001;
    private const uint HighLevelBase = 3001;
    private const uint CriticalLevelBase = 4001;
    private const uint ProviderShowBase = 5000;
    private const uint ProviderSetKeyBase = 6000;
    private const uint BalanceHidingBase = 5500;
    private const uint TelegramTokenCommandId = 7000;
    private const uint TelegramChatIdCommandId = 7001;
    private const uint DiscordWebhookCommandId = 8000;
    private const uint DiscordUsernameCommandId = 8001;
    private const uint UpdateCheckNowCommandId = 9000;
    private const uint UpdateCheckOnStartupBase = 9001;

    private static readonly int[] RefreshEveryValues = [1, 5, 15, 60];
    private static readonly int[] LevelValues = [50, 60, 70, 75, 80, 85, 90, 95];

    private sealed record ProviderEntry(
        string Name,
        string CredentialName,
        Func<AppSettings, string?> Getter,
        Func<AppSettings, string?, AppSettings> Setter);

    private static readonly ProviderEntry[] ProviderEntries =
    [
        new("DeepSeek", CredentialNames.DeepSeek, static s => s.DeepSeekApiKey, static (s, v) => s with { DeepSeekApiKey = v }),
        new("OpenRouter", CredentialNames.OpenRouter, static s => s.OpenRouterApiKey, static (s, v) => s with { OpenRouterApiKey = v }),
        new("Moonshot", CredentialNames.Moonshot, static s => s.MoonshotApiKey, static (s, v) => s with { MoonshotApiKey = v }),
        new("Deepgram", CredentialNames.Deepgram, static s => s.DeepgramApiKey, static (s, v) => s with { DeepgramApiKey = v }),
        new("ElevenLabs", CredentialNames.ElevenLabs, static s => s.ElevenLabsApiKey, static (s, v) => s with { ElevenLabsApiKey = v }),
        new("Kilo", CredentialNames.Kilo, static s => s.KiloApiKey, static (s, v) => s with { KiloApiKey = v }),
        new("OpenAI", CredentialNames.OpenAI, static s => s.OpenAiApiKey, static (s, v) => s with { OpenAiApiKey = v }),
        new("Venice", CredentialNames.Venice, static s => s.VeniceApiKey, static (s, v) => s with { VeniceApiKey = v }),
        new("Copilot", CredentialNames.Copilot, static s => s.CopilotApiKey, static (s, v) => s with { CopilotApiKey = v }),
        new("Crof", CredentialNames.Crof, static s => s.CrofApiKey, static (s, v) => s with { CrofApiKey = v }),
        new("Codebuff", CredentialNames.Codebuff, static s => s.CodebuffApiKey, static (s, v) => s with { CodebuffApiKey = v }),
        new("Warp", CredentialNames.Warp, static s => s.WarpApiKey, static (s, v) => s with { WarpApiKey = v }),
        new("Zai", CredentialNames.Zai, static s => s.ZaiApiKey, static (s, v) => s with { ZaiApiKey = v }),
        new("Synthetic", CredentialNames.Synthetic, static s => s.SyntheticApiKey, static (s, v) => s with { SyntheticApiKey = v }),
        new("Chutes", CredentialNames.Chutes, static s => s.ChutesApiKey, static (s, v) => s with { ChutesApiKey = v }),
        new("MiniMax", CredentialNames.MiniMax, static s => s.MiniMaxApiKey, static (s, v) => s with { MiniMaxApiKey = v }),
        new("Poe", CredentialNames.Poe, static s => s.PoeApiKey, static (s, v) => s with { PoeApiKey = v }),
        new("Alibaba", CredentialNames.Alibaba, static s => s.AlibabaApiKey, static (s, v) => s with { AlibabaApiKey = v }),
    ];

    public event Action? RefreshRequested;
    public event Action? TestNotificationRequested;
    public event Action? ExitRequested;
    public event Action? UpdateCheckNowRequested;

    public void Show(nint ownerHwnd, NativeMethods.Point point)
    {
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            var current = settings.Read();

            var refreshEvery = BuildCheckedSubmenu(RefreshEveryBase, RefreshEveryValues, current.RefreshPeriodMinute, static value => $"{value} min");
            NativeMethods.AppendMenu(menu, NativeMethods.MfPopup, (nuint)refreshEvery, "Refresh every");

            var highLevel = BuildCheckedSubmenu(HighLevelBase, LevelValues, (int)current.HighPercentage, static value => $"{value}%");
            NativeMethods.AppendMenu(menu, NativeMethods.MfPopup, (nuint)highLevel, "High Level");

            var criticalLevel = BuildCheckedSubmenu(CriticalLevelBase, LevelValues, (int)current.CriticalPercentage, static value => $"{value}%");
            NativeMethods.AppendMenu(menu, NativeMethods.MfPopup, (nuint)criticalLevel, "Critical Level");

            NativeMethods.AppendMenu(menu, NativeMethods.MfSeparator, 0, string.Empty);
            var providerMenu = BuildProviderMenu(current);
            NativeMethods.AppendMenu(menu, NativeMethods.MfPopup, (nuint)providerMenu, "Provider");

            var balanceHiding = BuildBalanceHidingMenu(current);
            NativeMethods.AppendMenu(menu, NativeMethods.MfPopup, (nuint)balanceHiding, "Hide Provider Under X Balance");

            NativeMethods.AppendMenu(menu, NativeMethods.MfSeparator, 0, string.Empty);
            var telegramMenu = BuildTelegramMenu(current);
            NativeMethods.AppendMenu(menu, NativeMethods.MfPopup, (nuint)telegramMenu, "Telegram");

            var discordMenu = BuildDiscordMenu(current);
            NativeMethods.AppendMenu(menu, NativeMethods.MfPopup, (nuint)discordMenu, "Discord");

            NativeMethods.AppendMenu(menu, NativeMethods.MfSeparator, 0, string.Empty);
            var updateMenu = BuildUpdateMenu(current);
            NativeMethods.AppendMenu(menu, NativeMethods.MfPopup, (nuint)updateMenu, "Update");
            NativeMethods.AppendMenu(menu, NativeMethods.MfSeparator, 0, string.Empty);
            NativeMethods.AppendMenu(menu, NativeMethods.MfString, TestNotificationCommandId, "Test Notification");
            NativeMethods.AppendMenu(menu, NativeMethods.MfSeparator, 0, string.Empty);
            NativeMethods.AppendMenu(menu, NativeMethods.MfString, RefreshCommandId, "Refresh");
            NativeMethods.AppendMenu(menu, NativeMethods.MfString, ExitCommandId, "Exit");

            NativeMethods.SetForegroundWindow(ownerHwnd);

            var command = NativeMethods.TrackPopupMenuEx(
                menu,
                NativeMethods.TpmRightButton | NativeMethods.TpmReturnCmd,
                point.X,
                point.Y,
                ownerHwnd,
                0);

            if (command != 0)
            {
                HandleCommand(command, current, ownerHwnd);
            }

            NativeMethods.PostMessage(ownerHwnd, NativeMethods.WmNull, 0, 0);
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    private nint BuildProviderMenu(AppSettings current)
    {
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == 0) return 0;

        var hidden = current.HiddenProviders is { Length: > 0 }
            ? new HashSet<string>(current.HiddenProviders, StringComparer.Ordinal)
            : [];

        for (var i = 0; i < ProviderEntries.Length; i++)
        {
            var entry = ProviderEntries[i];
            var providerMenu = BuildSingleProviderMenu(current, entry, i, hidden);
            if (providerMenu != 0)
            {
                var flags = NativeMethods.MfPopup;
                if (ProviderHasKey(current, entry))
                {
                    flags |= NativeMethods.MfChecked;
                }
                NativeMethods.AppendMenu(menu, flags, (nuint)providerMenu, entry.Name);
            }
        }

        return menu;
    }

    private static nint BuildSingleProviderMenu(AppSettings current, ProviderEntry entry, int index, HashSet<string> hidden)
    {
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == 0) return 0;

        var isHidden = hidden.Contains(entry.Name);
        var showFlags = NativeMethods.MfString;
        if (!isHidden)
        {
            showFlags |= NativeMethods.MfChecked;
        }
        NativeMethods.AppendMenu(menu, showFlags, (nuint)(ProviderShowBase + (uint)index), "Show");

        var hasKey = ProviderHasKey(current, entry);
        var keyFlags = NativeMethods.MfString;
        if (hasKey)
        {
            keyFlags |= NativeMethods.MfChecked;
        }
        NativeMethods.AppendMenu(menu, keyFlags, (nuint)(ProviderSetKeyBase + (uint)index), "API Key");

        return menu;
    }

    private static bool ProviderHasKey(AppSettings current, ProviderEntry entry)
    {
        var settingsValue = entry.Getter(current);
        if (!string.IsNullOrWhiteSpace(settingsValue))
            return true;
        var envValue = Environment.GetEnvironmentVariable(entry.CredentialName);
        return !string.IsNullOrWhiteSpace(envValue);
    }

    private static nint BuildBalanceHidingMenu(AppSettings current)
    {
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == 0) return 0;

        var threshold = current.BalanceHidingThreshold;
        AddMenuItem(menu, BalanceHidingBase, "Show All", threshold == -1);
        AddMenuItem(menu, BalanceHidingBase + 1, "0 (Hides 0 balances)", threshold == 0);
        AddMenuItem(menu, BalanceHidingBase + 2, "1", threshold == 1);
        AddMenuItem(menu, BalanceHidingBase + 3, "5", threshold == 5);
        AddMenuItem(menu, BalanceHidingBase + 4, "10", threshold == 10);

        return menu;
    }

    private static nint BuildTelegramMenu(AppSettings current)
    {
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == 0) return 0;

        var telegram = current.Telegram;
        AddMenuItem(menu, TelegramTokenCommandId, "Token", !string.IsNullOrEmpty(telegram?.Token));
        AddMenuItem(menu, TelegramChatIdCommandId, "Chat ID", telegram?.ChatId != 0);

        return menu;
    }

    private static nint BuildDiscordMenu(AppSettings current)
    {
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == 0) return 0;

        var discord = current.Discord;
        AddMenuItem(menu, DiscordWebhookCommandId, "Webhook URL", !string.IsNullOrEmpty(discord?.WebhookUrl));
        AddMenuItem(menu, DiscordUsernameCommandId, "Username", !string.IsNullOrEmpty(discord?.Username));

        return menu;
    }

    private static nint BuildUpdateMenu(AppSettings current)
    {
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == 0) return 0;

        AddMenuItem(menu, UpdateCheckOnStartupBase, "Check Updates on Startup", current.CheckUpdatesOnStartup ?? true);
        NativeMethods.AppendMenu(menu, NativeMethods.MfString, UpdateCheckNowCommandId, "Check Updates Now");

        return menu;
    }

    private static void AddMenuItem(nint menu, uint commandId, string label, bool isChecked)
    {
        var flags = NativeMethods.MfString;
        if (isChecked)
        {
            flags |= NativeMethods.MfChecked;
        }
        NativeMethods.AppendMenu(menu, flags, (nuint)commandId, label);
    }

    private void HandleCommand(uint commandId, AppSettings current, nint ownerHwnd)
    {
        switch (commandId)
        {
            case TestNotificationCommandId:
                TestNotificationRequested?.Invoke();
                break;

            case RefreshCommandId:
                RefreshRequested?.Invoke();
                break;

            case ExitCommandId:
                ExitRequested?.Invoke();
                break;

            case var cmd when cmd >= RefreshEveryBase && cmd - RefreshEveryBase < RefreshEveryValues.Length:
                ApplySettings(current with { RefreshPeriodMinute = RefreshEveryValues[(int)(cmd - RefreshEveryBase)] });
                break;

            case var cmd when cmd >= HighLevelBase && cmd - HighLevelBase < LevelValues.Length:
                ApplySettings(current with { HighPercentage = LevelValues[(int)(cmd - HighLevelBase)] });
                break;

            case var cmd when cmd >= CriticalLevelBase && cmd - CriticalLevelBase < LevelValues.Length:
                ApplySettings(current with { CriticalPercentage = LevelValues[(int)(cmd - CriticalLevelBase)] });
                break;

            case var cmd when cmd >= ProviderShowBase && cmd - ProviderShowBase < ProviderEntries.Length:
                HandleProviderShowToggle((int)(cmd - ProviderShowBase), current);
                break;

            case var cmd when cmd >= ProviderSetKeyBase && cmd - ProviderSetKeyBase < ProviderEntries.Length:
                HandleProviderSetKey((int)(cmd - ProviderSetKeyBase), current, ownerHwnd);
                break;

            case var cmd when cmd >= BalanceHidingBase && cmd - BalanceHidingBase < 5:
                HandleBalanceHidingCommand((int)(cmd - BalanceHidingBase), current);
                break;

            case TelegramTokenCommandId:
                HandleTelegramToken(current, ownerHwnd);
                break;

            case TelegramChatIdCommandId:
                HandleTelegramChatId(current, ownerHwnd);
                break;

            case DiscordWebhookCommandId:
                HandleDiscordWebhook(current, ownerHwnd);
                break;

            case DiscordUsernameCommandId:
                HandleDiscordUsername(current, ownerHwnd);
                break;

            case UpdateCheckNowCommandId:
                UpdateCheckNowRequested?.Invoke();
                break;

            case UpdateCheckOnStartupBase:
                ApplySettings(current with { CheckUpdatesOnStartup = !(current.CheckUpdatesOnStartup ?? true) });
                break;
        }
    }

    private void HandleProviderShowToggle(int index, AppSettings current)
    {
        var entry = ProviderEntries[index];
        var hiddenList = current.HiddenProviders is [..] arr ? arr : [];
        var isHidden = hiddenList.Contains(entry.Name);

        var updated = isHidden
            ? current with { HiddenProviders = hiddenList.Where(n => n != entry.Name).ToArray() }
            : current with { HiddenProviders = [.. hiddenList, entry.Name] };

        ApplySettings(updated);
    }

    private void HandleProviderSetKey(int index, AppSettings current, nint ownerHwnd)
    {
        var entry = ProviderEntries[index];
        var currentValue = ResolveProviderKey(current, entry);
        var newValue = InputDialog.Show(ownerHwnd, $"{entry.Name} API Key",
            $"Enter {entry.Name} API Key\n\nLeave blank to disable it", currentValue);

        if (newValue is not null)
        {
            SaveProviderKey(current, entry, newValue);
        }
    }

    private static string ResolveProviderKey(AppSettings current, ProviderEntry entry)
    {
        var settingsValue = entry.Getter(current);
        if (!string.IsNullOrWhiteSpace(settingsValue))
            return settingsValue;
        return Environment.GetEnvironmentVariable(entry.CredentialName) ?? string.Empty;
    }

    private void SaveProviderKey(AppSettings current, ProviderEntry entry, string newValue)
    {
        var settingsValue = entry.Getter(current);
        var envValue = Environment.GetEnvironmentVariable(entry.CredentialName);
        if (string.IsNullOrWhiteSpace(settingsValue) && !string.IsNullOrWhiteSpace(envValue))
        {
            Environment.SetEnvironmentVariable(entry.CredentialName, newValue, EnvironmentVariableTarget.User);
            Environment.SetEnvironmentVariable(entry.CredentialName, newValue);
            RefreshRequested?.Invoke();
        }
        else
        {
            ApplySettings(entry.Setter(current, newValue));
        }
    }

    private void HandleBalanceHidingCommand(int index, AppSettings current)
    {
        var values = new[] { -1.0, 0.0, 1.0, 5.0, 10.0 };
        ApplySettings(current with { BalanceHidingThreshold = values[index] });
    }

    private void HandleTelegramToken(AppSettings current, nint ownerHwnd)
    {
        var telegram = current.Telegram ?? TelegramSettings.Default;
        var currentValue = telegram.Token ?? string.Empty;
        var newValue = InputDialog.Show(ownerHwnd, "Telegram Bot Token",
            "Enter Telegram Bot Token\n\nLeave blank to disable it", currentValue);

        if (newValue is not null)
        {
            ApplySettings(current with { Telegram = telegram with { Token = newValue } });
        }
    }

    private void HandleTelegramChatId(AppSettings current, nint ownerHwnd)
    {
        var telegram = current.Telegram ?? TelegramSettings.Default;
        var currentValue = telegram.ChatId != 0 ? telegram.ChatId.ToString() : string.Empty;
        var chatIdStr = InputDialog.Show(ownerHwnd, "Telegram Chat ID",
            "Enter Telegram Chat ID (numeric)\n\nLeave blank to disable it", currentValue);

        if (chatIdStr is not null)
        {
            var chatId = 0L;
            if (!string.IsNullOrWhiteSpace(chatIdStr) && long.TryParse(chatIdStr.Trim(), out var parsed))
            {
                chatId = parsed;
            }
            ApplySettings(current with { Telegram = telegram with { ChatId = chatId } });
        }
    }

    private void HandleDiscordWebhook(AppSettings current, nint ownerHwnd)
    {
        var discord = current.Discord ?? DiscordSettings.Default;
        var currentValue = discord.WebhookUrl ?? string.Empty;
        var newValue = InputDialog.Show(ownerHwnd, "Discord Webhook URL",
            "Enter Discord Webhook URL\n\nLeave blank to disable it", currentValue);

        if (newValue is not null)
        {
            ApplySettings(current with { Discord = discord with { WebhookUrl = newValue } });
        }
    }

    private void HandleDiscordUsername(AppSettings current, nint ownerHwnd)
    {
        var discord = current.Discord ?? DiscordSettings.Default;
        var currentValue = discord.Username ?? string.Empty;
        var newValue = InputDialog.Show(ownerHwnd, "Discord Username",
            "Enter Discord Username\n\nLeave blank to disable it", currentValue);

        if (newValue is not null)
        {
            ApplySettings(current with { Discord = discord with { Username = string.IsNullOrWhiteSpace(newValue) ? null : newValue } });
        }
    }

    private void ApplySettings(AppSettings updated)
    {
        settings.Write(updated);
        RefreshRequested?.Invoke();
    }

    private static nint BuildCheckedSubmenu(uint baseId, int[] values, int selected, Func<int, string> label)
    {
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == 0)
        {
            return 0;
        }

        for (var i = 0; i < values.Length; i++)
        {
            var flags = values[i] == selected
                ? NativeMethods.MfString | NativeMethods.MfChecked
                : NativeMethods.MfString;
            NativeMethods.AppendMenu(menu, flags, (nuint)(baseId + (uint)i), label(values[i]));
        }

        return menu;
    }
}
