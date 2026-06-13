namespace Gaolbreak;

internal sealed class HeartbeatWriter
{
    public const string Key = "Gaolbreak.Heartbeat.v1";

    private readonly long[] data;

    public HeartbeatWriter() =>
        data = Plugin.PluginInterface.GetOrCreateData(Key, () => new long[1]);

    public void Tick() => data[0]++;

    public void Dispose() => Plugin.PluginInterface.RelinquishData(Key);
}
