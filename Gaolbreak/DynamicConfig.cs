using Dalamud.Plugin;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Gaolbreak;

internal sealed class DynamicConfigData
{
    public IReadOnlyDictionary<string, IReadOnlySet<uint>> DefaultPins { get; init; } = new Dictionary<string, IReadOnlySet<uint>>();
    public IReadOnlyDictionary<string, IReadOnlyList<Regex>> DefaultPinMatchers { get; init; } = new Dictionary<string, IReadOnlyList<Regex>>();
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

    public DynamicConfigData Current => current;

    public event Action? OnUpdated;

    public DynamicConfig(IDalamudPluginInterface plugin)
    {
        current = Parse(ReadEmbeddedDefault(plugin));
        OnUpdated?.Invoke();
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var remote = await TryLoadGitHubAsync();
        if (disposed) return;
        ApplyRemote(remote);
    }

    private void ApplyRemote(DynamicConfigData remote)
    {
#if !DEBUG
        current = remote;
#endif
        OnUpdated?.Invoke();
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

    public void Dispose()
    {
        disposed = true;
        cts.Cancel();
        cts.Dispose();
    }

    private sealed class Dto
    {
        public int Version { get; set; }
        public Dictionary<string, HashSet<uint>>? DefaultPins { get; set; }
        public Dictionary<string, List<string>>? DefaultPinPatterns { get; set; }
    }

    private static DynamicConfigData Parse(string yaml)
    {
        var dto = Yaml.Deserialize<Dto>(yaml) ?? new Dto();
        if (dto.Version != 1) throw new InvalidDataException($"Unsupported Dynamic Config version {dto.Version}");
        var addonMatchers = new Dictionary<string, IReadOnlyList<Regex>>();
        foreach (var (anchor, patterns) in dto.DefaultPinPatterns ?? new())
        {
            var matchers = new List<Regex>();
            foreach (var pattern in patterns)
            {
                try
                {
                    matchers.Add(new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant));
                }
                catch (Exception e)
                {
                    Plugin.Log.Warning(e, $"Bad pin pattern '{pattern}'");
                }
            }
            addonMatchers[anchor] = matchers;
        }
        return new DynamicConfigData
        {
            DefaultPins = (dto.DefaultPins ?? new()).ToDictionary(kvp => kvp.Key, kvp => (IReadOnlySet<uint>)kvp.Value),
            DefaultPinMatchers = addonMatchers,
        };
    }

    private static string ReadEmbeddedDefault(IDalamudPluginInterface plugin)
    {
        var path = Path.Combine(plugin.AssemblyLocation.Directory?.FullName!, ConfigName);
        return File.ReadAllText(path);

    }
}
