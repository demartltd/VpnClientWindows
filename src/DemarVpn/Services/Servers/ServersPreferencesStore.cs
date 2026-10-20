using System.Text.Json;
using DemarVpn.Domain.Vpn;

namespace DemarVpn.Services.Servers;

/// <summary>Persisted server list filters + favorites — parity Android ServersStore / Apple ServersPreferencesStore.</summary>
public static class ServersPreferencesStore
{
    private static readonly object Gate = new();
    private static readonly HashSet<ServerTier> AllTiers = [.. Enum.GetValues<ServerTier>()];
    private static readonly HashSet<ServerProtocol> AllProtocols = [.. Enum.GetValues<ServerProtocol>()];

    public static string FilePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DemarVpn", "servers_prefs.json");

    private static HashSet<ServerTier> _tiers = new(AllTiers);
    private static HashSet<ServerProtocol> _protocols = new(AllProtocols);
    private static HashSet<string> _favorites = new(StringComparer.Ordinal);
    private static bool _ready;

    public static event Action? Changed;

    public static IReadOnlySet<ServerTier> Tiers
    {
        get { EnsureLoaded(); lock (Gate) return new HashSet<ServerTier>(_tiers); }
    }

    public static IReadOnlySet<ServerProtocol> Protocols
    {
        get { EnsureLoaded(); lock (Gate) return new HashSet<ServerProtocol>(_protocols); }
    }

    public static IReadOnlySet<string> Favorites
    {
        get { EnsureLoaded(); lock (Gate) return new HashSet<string>(_favorites, StringComparer.Ordinal); }
    }

    public static bool IsFavorite(string id)
    {
        EnsureLoaded();
        lock (Gate) return _favorites.Contains(id);
    }

    public static void ToggleFavorite(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        EnsureLoaded();
        lock (Gate)
        {
            if (!_favorites.Remove(id)) _favorites.Add(id);
            SaveUnlocked();
        }
        Changed?.Invoke();
    }

    public static void SetTiers(IEnumerable<ServerTier> value) =>
        SetFilter(ref _tiers, value, AllTiers);

    public static void SetProtocols(IEnumerable<ServerProtocol> value) =>
        SetFilter(ref _protocols, value, AllProtocols);

    public static void ResetFilters(bool raiseChanged = true)
    {
        EnsureLoaded();
        lock (Gate)
        {
            if (_tiers.SetEquals(AllTiers) && _protocols.SetEquals(AllProtocols)) return;
            _tiers = new(AllTiers);
            _protocols = new(AllProtocols);
            SaveUnlocked();
        }
        if (raiseChanged) Changed?.Invoke();
    }

    /// <summary>True when any currently visible chip is off (Apple filtersActive).</summary>
    public static bool FiltersActive(
        IEnumerable<ServerTier> visibleTiers,
        IEnumerable<ServerProtocol> visibleProtocols)
    {
        EnsureLoaded();
        lock (Gate)
            return visibleTiers.Any(t => !_tiers.Contains(t))
                || visibleProtocols.Any(p => !_protocols.Contains(p));
    }

    private static void SetFilter<T>(
        ref HashSet<T> field, IEnumerable<T> value, HashSet<T> all) where T : struct, Enum
    {
        EnsureLoaded();
        lock (Gate)
        {
            var next = value.ToHashSet();
            if (next.Count == 0) next = new(all);
            if (field.SetEquals(next)) return;
            field = next;
            SaveUnlocked();
        }
        Changed?.Invoke();
    }

    private static void EnsureLoaded()
    {
        if (_ready) return;
        lock (Gate)
        {
            if (_ready) return;
            LoadUnlocked();
            _ready = true;
        }
    }

    private static void LoadUnlocked()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
            var root = doc.RootElement;
            if (root.TryGetProperty("tiers", out var tEl) && tEl.ValueKind == JsonValueKind.Array)
            {
                var parsed = ParseEnumSet<ServerTier>(tEl);
                if (parsed.Count > 0) _tiers = parsed;
            }
            if (root.TryGetProperty("protocols", out var pEl) && pEl.ValueKind == JsonValueKind.Array)
            {
                var parsed = ParseEnumSet<ServerProtocol>(pEl);
                if (parsed.Count > 0) _protocols = parsed;
            }
            if (root.TryGetProperty("favorites", out var fEl) && fEl.ValueKind == JsonValueKind.Array)
            {
                _favorites = new HashSet<string>(StringComparer.Ordinal);
                foreach (var el in fEl.EnumerateArray())
                    if (el.ValueKind == JsonValueKind.String && el.GetString() is { Length: > 0 } id)
                        _favorites.Add(id);
            }
        }
        catch { /* keep defaults */ }
    }

    private static void SaveUnlocked()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var json = JsonSerializer.Serialize(new
            {
                tiers = _tiers.Select(t => t.ToString()).ToArray(),
                protocols = _protocols.Select(p => p.ToString()).ToArray(),
                favorites = _favorites.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            });
            File.WriteAllText(FilePath, json);
        }
        catch { /* ignore */ }
    }

    private static HashSet<T> ParseEnumSet<T>(JsonElement arr) where T : struct, Enum
    {
        var set = new HashSet<T>();
        foreach (var el in arr.EnumerateArray())
            if (el.ValueKind == JsonValueKind.String
                && Enum.TryParse<T>(el.GetString(), ignoreCase: true, out var v))
                set.Add(v);
        return set;
    }
}
