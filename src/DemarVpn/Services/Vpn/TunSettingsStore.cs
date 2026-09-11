using System.Text.Json;
using DemarVpn.Domain.Vpn.Tun;
using DemarVpn.Services.Auth;
using DemarVpn.Services.Servers;

namespace DemarVpn.Services.Vpn;

public static class TunSettingsStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DemarVpn", "tun_settings.json");

    private static readonly object Gate = new();
    private static TunSettings _settings = TunSettings.Empty;
    private static TunCatalog _catalog = TunCatalog.Empty;
    private static string? _groupsJson;
    private static bool _ready;

    public static event Action? Changed;

    public static TunSettings Settings
    {
        get { lock (Gate) return _settings; }
    }

    public static TunCatalog Catalog
    {
        get { lock (Gate) return _catalog; }
    }

    public static void Init()
    {
        if (_ready) return;
        lock (Gate)
        {
            if (_ready) return;
            LoadFromDisk();
            _ready = true;
        }
    }

    public static void CacheGroups(TunCatalog catalog, string? rawJson)
    {
        lock (Gate)
        {
            _catalog = catalog;
            if (rawJson is not null) _groupsJson = rawJson;
            SaveToDisk();
        }
        var uid = AuthSessionStore.Current?.UserId;
        if (rawJson is not null && uid is not null)
            SessionCacheStore.SaveTun(rawJson, uid);
        Changed?.Invoke();
    }

    /// <summary>Apply TUN groups from session cache — parity Apple TunSettingsStore.hydrateFromCache.</summary>
    public static void HydrateFromCache(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return;
        lock (Gate)
        {
            _groupsJson = raw;
            _catalog = TunGroupsApi.Parse(raw);
            SaveToDisk();
        }
        Changed?.Invoke();
    }

    public static TunBypassOptions EffectiveBypass(bool isFree)
    {
        lock (Gate)
            return _settings.EffectiveBypass(_catalog, isFree);
    }

    public static string SettingsFingerprint(bool isFree)
    {
        lock (Gate)
            return _settings.Fingerprint(_catalog, isFree);
    }

    public static void SetRulesEnabled(bool enabled)
    {
        lock (Gate)
        {
            if (_settings.RulesEnabled == enabled) return;
            _settings = _settings with { RulesEnabled = enabled };
            SaveToDisk();
        }
        Changed?.Invoke();
    }

    public static void AddBypassExe(string exe)
    {
        var id = TunExeNames.Normalize(exe);
        if (!TunExeNames.IsExeName(id) || TunExeNames.IsSelf(id)) return;
        lock (Gate)
        {
            if (_settings.BypassExes.Contains(id, StringComparer.OrdinalIgnoreCase)) return;
            _settings = _settings with { BypassExes = _settings.BypassExes.Concat([id]).OrderBy(x => x).ToList() };
            SaveToDisk();
        }
        Changed?.Invoke();
    }

    public static void RemoveBypassExe(string exe)
    {
        var id = TunExeNames.Normalize(exe);
        lock (Gate)
        {
            var next = _settings.BypassExes.Where(x => !x.Equals(id, StringComparison.OrdinalIgnoreCase)).ToList();
            if (next.Count == _settings.BypassExes.Count) return;
            _settings = _settings with { BypassExes = next };
            SaveToDisk();
        }
        Changed?.Invoke();
    }

    public static void SetAppGroupEnabled(string groupId, bool enabled)
    {
        var id = groupId.Trim().ToLowerInvariant();
        if (id.Length == 0) return;
        lock (Gate)
        {
            var overrides = new Dictionary<string, bool>(_settings.AppGroupOverrides, StringComparer.OrdinalIgnoreCase)
            {
                [id] = enabled,
            };
            _settings = _settings with { AppGroupOverrides = overrides };
            SaveToDisk();
        }
        Changed?.Invoke();
    }

    public static void SetSiteGroupEnabled(string groupId, bool enabled)
    {
        var id = groupId.Trim().ToLowerInvariant();
        if (id.Length == 0) return;
        lock (Gate)
        {
            var overrides = new Dictionary<string, bool>(_settings.SiteGroupOverrides, StringComparer.OrdinalIgnoreCase)
            {
                [id] = enabled,
            };
            _settings = _settings with { SiteGroupOverrides = overrides };
            SaveToDisk();
        }
        Changed?.Invoke();
    }

    private static void LoadFromDisk()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var dto = JsonSerializer.Deserialize<PersistDto>(File.ReadAllText(FilePath), JsonOpts);
            if (dto is null) return;
            _settings = new TunSettings(
                dto.BypassExes ?? [],
                ToDict(dto.AppGroupOverrides),
                ToDict(dto.SiteGroupOverrides),
                dto.RulesEnabled ?? true);
            _groupsJson = dto.GroupsJson;
            if (!string.IsNullOrEmpty(_groupsJson))
                _catalog = TunGroupsApi.Parse(_groupsJson);
        }
        catch { /* ignore corrupt file */ }
    }

    private static void SaveToDisk()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var dto = new PersistDto
            {
                BypassExes = _settings.BypassExes.ToList(),
                AppGroupOverrides = _settings.AppGroupOverrides.ToDictionary(x => x.Key, x => x.Value),
                SiteGroupOverrides = _settings.SiteGroupOverrides.ToDictionary(x => x.Key, x => x.Value),
                RulesEnabled = _settings.RulesEnabled,
                GroupsJson = _groupsJson,
            };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(dto, JsonOpts));
        }
        catch { /* ignore */ }
    }

    private static Dictionary<string, bool> ToDict(Dictionary<string, bool>? src) =>
        src is null ? new(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, bool>(src, StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private sealed class PersistDto
    {
        public List<string>? BypassExes { get; set; }
        public Dictionary<string, bool>? AppGroupOverrides { get; set; }
        public Dictionary<string, bool>? SiteGroupOverrides { get; set; }
        public bool? RulesEnabled { get; set; }
        public string? GroupsJson { get; set; }
    }
}
