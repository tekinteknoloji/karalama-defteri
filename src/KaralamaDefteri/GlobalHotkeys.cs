using System.Text.Json;
using System.Windows.Input;

namespace KaralamaDefteri;

internal sealed record HotkeyBinding(bool Enabled, ModifierKeys Modifiers, Key Key)
{
    public string Display => string.Concat(
        Modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl+" : "",
        Modifiers.HasFlag(ModifierKeys.Alt) ? "Alt+" : "",
        Modifiers.HasFlag(ModifierKeys.Shift) ? "Shift+" : "",
        Modifiers.HasFlag(ModifierKeys.Windows) ? "Win+" : "",
        Key.ToString());

    public static bool CanRegister(ModifierKeys modifiers, Key key) =>
        (modifiers & ~(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows)) == 0 &&
        (modifiers != ModifierKeys.None && modifiers != ModifierKeys.Shift || key is >= Key.F1 and <= Key.F24) &&
        Enum.IsDefined(key) && key is not (Key.None or Key.System or Key.ImeProcessed or Key.DeadCharProcessed
            or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin) && KeyInterop.VirtualKeyFromKey(key) != 0;
}

public partial class App
{
    private const string HotkeySetting = "globalHotkeys";
    internal IReadOnlyList<HotkeyBinding> HotkeyBindings { get; private set; } = DefaultHotkeys();
    internal bool IsHotkeyRegistered(int index) => hotkeys.Contains(index + 1);

    private static HotkeyBinding[] DefaultHotkeys() =>
        new[] { Key.N, Key.V, Key.K, Key.Space, Key.O }.Select(key => new HotkeyBinding(key != Key.O, ModifierKeys.Control | ModifierKeys.Alt, key)).ToArray();

    private void LoadHotkeys()
    {
        HotkeyBindings = ParseHotkeys(store?.Setting(HotkeySetting)) ?? DefaultHotkeys();
        CacheLockedShortcuts();
    }

    private static HotkeyBinding[]? ParseHotkeys(string? json)
    {
        if (json == null) return null;
        try
        {
            if (JsonSerializer.Deserialize<HotkeyBinding[]>(json) is { Length: 4 or 5 } values &&
                values.All(value => value != null && HotkeyBinding.CanRegister(value.Modifiers, value.Key)))
                return values.Length == 4 ? [.. values, DefaultHotkeys()[4]] : values;
        }
        catch (JsonException) { }
        return null;
    }

    private static IReadOnlyList<HotkeyBinding> LoadLockedShortcuts()
    {
        try
        {
            return File.Exists(AppPaths.LockedShortcuts)
                ? ParseHotkeys(File.ReadAllText(AppPaths.LockedShortcuts)) ?? DefaultHotkeys()
                : DefaultHotkeys();
        }
        catch (IOException) { return DefaultHotkeys(); }
        catch (UnauthorizedAccessException) { return DefaultHotkeys(); }
    }

    internal const int AuthenticatorHotkeyIndex = 4;

    internal IReadOnlyList<HotkeyBinding> EffectiveHotkeys => center?.Features.Enabled(Feature.Authenticator) == true
        ? HotkeyBindings
        : HotkeyBindings.Select((binding, index) => index == AuthenticatorHotkeyIndex
            ? binding with { Enabled = false } : binding).ToArray();

    internal void RefreshFeatureHotkeys()
    {
        CacheLockedShortcuts();
        if (source != null) RegisterConfiguredHotkeys();
    }

    private void CacheLockedShortcuts()
    {
        try
        {
            if (store!.IsEncrypted)
                Center.SaveSecurityFile(AppPaths.LockedShortcuts, JsonSerializer.SerializeToUtf8Bytes(EffectiveHotkeys));
            else
                File.Delete(AppPaths.LockedShortcuts);
        }
        catch (IOException) { center?.Notify("Kilit ekranı kısayolları güncellenemedi."); }
        catch (UnauthorizedAccessException) { center?.Notify("Kilit ekranı kısayolları güncellenemedi."); }
    }

    internal void SetHotkey(int index, HotkeyBinding binding)
    {
        var updated = HotkeyBindings.ToArray();
        updated[index] = binding;
        SaveHotkeys(updated);
    }

    internal void ResetHotkeys() => SaveHotkeys(DefaultHotkeys());

    private void SaveHotkeys(HotkeyBinding[] bindings)
    {
        store!.Set(HotkeySetting, JsonSerializer.Serialize(bindings));
        HotkeyBindings = bindings;
        CacheLockedShortcuts();
        RegisterConfiguredHotkeys();
    }

    internal void SuspendHotkeys()
    {
        var handle = source!.Handle;
        foreach (var id in hotkeys) UnregisterHotKey(handle, id);
        hotkeys.Clear();
    }

    internal void RegisterConfiguredHotkeys()
    {
        SuspendHotkeys();
        var handle = source!.Handle;
        var bindings = EffectiveHotkeys;
        for (var index = 0; index < bindings.Count; index++)
        {
            var binding = bindings[index];
            // WPF ModifierKeys and Win32 MOD_* use the same low four bits.
            if (binding.Enabled && RegisterHotKey(handle, index + 1, 0x4000u | (uint)binding.Modifiers,
                    (uint)KeyInterop.VirtualKeyFromKey(binding.Key)))
                hotkeys.Add(index + 1);
        }
    }
}
