namespace Gaolbreak;

internal sealed class SharedCaptureWriter
{
    public const string Key = "Gaolbreak.Capture.v1";

    public enum Field
    {
        FrameCounter,
        FgTexture, FgSrv, FgRtv, FgWidth, FgHeight,
        BgTexture, BgSrv, BgRtv, BgWidth, BgHeight,
        Count,
    }

    private readonly long[] data;

    public SharedCaptureWriter() =>
        data = Plugin.PluginInterface.GetOrCreateData(Key, () => new long[(int)Field.Count]);

    public void Write(UICapture capture)
    {
        data[(int)Field.FrameCounter]++;
        Write(Field.FgTexture, capture.FgCapture);
        Write(Field.BgTexture, capture.BgCapture);
    }

    private void Write(Field at, CaptureTarget t)
    {
        int i = (int)at;
        data[i + 0] = t.Tex?.NativePointer ?? 0;
        data[i + 1] = t.Srv?.NativePointer ?? 0;
        data[i + 2] = t.Rtv?.NativePointer ?? 0;
        data[i + 3] = t.Width;
        data[i + 4] = t.Height;
    }

    public void Dispose() => Plugin.PluginInterface.RelinquishData(Key);
}
