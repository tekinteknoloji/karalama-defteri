namespace KaralamaDefteri;

internal enum Feature
{
    Authenticator, DesktopPins, Tags, Checklist, CloudSync, Board, BoardDeadlines
}

internal sealed class FeatureFlags(NoteStore store)
{
    private sealed record Selection(bool Enabled, string Updated);
    private const string SyncKey = "features.sync";
    private static readonly Guid SyncId = new("d99324ed-0751-40a3-ba56-e988244ab627");

    internal bool SyncSelections
    {
        get => store.Setting(SyncKey) != "0";
        set => store.Set(SyncKey, value ? "1" : "0");
    }

    internal bool Selected(Feature feature) => Read(feature)?.Enabled ?? feature is not (Feature.Authenticator or Feature.Board or Feature.BoardDeadlines);
    internal bool Available(Feature feature) => feature switch
    {
        Feature.Authenticator => store.IsEncrypted,
        Feature.BoardDeadlines => Enabled(Feature.Board),
        _ => true
    };
    internal bool Enabled(Feature feature) => Available(feature) && Selected(feature);

    internal void Set(Feature feature, bool enabled) => Write(feature, new(enabled, NoteStore.Now()));

    private static string Key(Feature feature) => $"features.{feature}";
    private Selection? Read(Feature feature) => store.Setting(Key(feature)) is { } value
        ? System.Text.Json.JsonSerializer.Deserialize<Selection>(value) : null;
    private void Write(Feature feature, Selection selection) =>
        store.Set(Key(feature), System.Text.Json.JsonSerializer.Serialize(selection));

    internal void Sync(ISyncProvider provider, SyncEncryption crypto)
    {
        if (!SyncSelections) return;
        var remote = provider.GetBlob(SyncId) is { } bytes
            ? crypto.Open<Dictionary<Feature, Selection>>(SyncId, "features", bytes) : [];
        var changed = false;
        foreach (var feature in Enum.GetValues<Feature>())
        {
            var local = Read(feature);
            remote.TryGetValue(feature, out var other);
            var order = string.CompareOrdinal(local?.Updated, other?.Updated);
            if (order > 0 && local != null)
            {
                remote[feature] = local;
                changed = true;
            }
            else if (order < 0 && other != null) Write(feature, other);
        }
        if (changed) provider.PutBlob(SyncId, crypto.Seal(SyncId, "features", remote));
    }
}
