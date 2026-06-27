using Dalamud.Plugin;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Gaolbreak;

internal sealed class DynamicConfigData
{
    public IReadOnlySet<uint> ForegroundWindowIds { get; init; } = new HashSet<uint>();
    public IReadOnlySet<string> ForegroundWindowPrefixes { get; init; } = new HashSet<string>();
    public IReadOnlyDictionary<string, IReadOnlySet<uint>> DefaultPins { get; init; } = new Dictionary<string, IReadOnlySet<uint>>();
}

internal sealed class DynamicConfig : IDisposable
{
    private const string ConfigName = "config_v1.yaml";
    private const string Url = $"https://raw.githubusercontent.com/sourpuh/ffxiv_gaolbreak/main/Content/{ConfigName}";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private readonly CancellationTokenSource cts = new();
    private volatile DynamicConfigData current;
    private bool disposed;

    public event Action? OnUpdated;

    public DynamicConfigData Current => current;

    public DynamicConfig(IDalamudPluginInterface plugin)
    {
        current = Parse(ReadEmbeddedDefault(plugin));
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var remote = await TryLoadGitHubAsync();
        Publish(remote);
    }

    private async Task<DynamicConfigData> TryLoadGitHubAsync()
    {
        using var resp = await Http.GetAsync(Url, cts.Token);
        if (!resp.IsSuccessStatusCode)
        {
            Plugin.Log.Warning($"Dynamic config: GitHub returned {(int)resp.StatusCode} {resp.StatusCode}");
            resp.EnsureSuccessStatusCode();
        }
        var raw = await resp.Content.ReadAsStringAsync(cts.Token);
        var data = Parse(raw);
        return data;
    }

    private void Publish(DynamicConfigData data)
    {
        if (disposed) return;
        current = data;
        _ = Plugin.Framework.RunOnFrameworkThread(() => { if (!disposed) OnUpdated?.Invoke(); });
    }

    public void Dispose()
    {
        disposed = true;
        cts.Cancel();
        cts.Dispose();
    }

    private sealed class Dto
    {
        public int Version { get; set; }
        public HashSet<uint>? ForegroundWindowIds { get; set; }
        public HashSet<string>? ForegroundWindowPrefixes { get; set; }
        public Dictionary<string, HashSet<uint>>? DefaultPins { get; set; }
    }

    private static DynamicConfigData Parse(string yaml)
    {
        var dto = Yaml.Deserialize<Dto>(yaml) ?? new Dto();
        if (dto.Version != 1) throw new InvalidDataException($"Unsupported Dynamic Config version {dto.Version}");
        return new DynamicConfigData
        {
            ForegroundWindowIds = dto.ForegroundWindowIds ?? [],
            ForegroundWindowPrefixes = dto.ForegroundWindowPrefixes ?? [],
            DefaultPins = (dto.DefaultPins ?? new()).ToDictionary(kvp => kvp.Key, kvp => (IReadOnlySet<uint>)kvp.Value),
        };
    }

    private static string ReadEmbeddedDefault(IDalamudPluginInterface plugin)
    {
        var path = Path.Combine(plugin.AssemblyLocation.Directory?.FullName!, ConfigName);
        using var reader = new StreamReader(path);
        return reader.ReadToEnd();

    }
}
