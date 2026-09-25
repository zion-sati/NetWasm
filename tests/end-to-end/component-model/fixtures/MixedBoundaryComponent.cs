#nullable enable

using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.InteropServices.WebAssembly;
using System.Threading.Tasks;

namespace NetWasm.Fixtures.ComponentModel;

public static class PortableService
{
    [WitImport("netwasm:mixed@1.0.0/arithmetic", "add")]
    public static extern int Add(int left, int right);
}

public static class JavaScriptService
{
    [JSImport("multiply", "consumer.mixed")]
    public static extern int Multiply(int left, int right);

    [JSImport("increment_async", "consumer.mixed")]
    public static extern Task<int> IncrementAsync(int value);

    [JSImport("open_media_stream", "consumer.mixed")]
    public static extern JSObject OpenMediaStream();

    [JSImport("media_track_count", "consumer.mixed")]
    public static extern int MediaTrackCount(JSObject stream);

    [JSImport("stop_media_stream", "consumer.mixed")]
    public static extern void StopMediaStream(JSObject stream);
}

public static class MixedBoundaryComponent
{
    [WitExport("netwasm:mixed@1.0.0/acceptance", "run")]
    public static int Run(int value)
    {
        var timeContribution = DateTime.UtcNow.Ticks == 0 ? 0 : 1;
        return JavaScriptService.Multiply(
            PortableService.Add(value, timeContribution),
            2);
    }

    [JSExport("mixed_sync")]
    public static int MixedSync(int value) => Run(value);

    [JSExport("mixed_async")]
    public static async Task<int> MixedAsync(int value) =>
        await JavaScriptService.IncrementAsync(value) + 1;

    [JSExport("media_resource")]
    public static int MediaResource(int value)
    {
        var stream = JavaScriptService.OpenMediaStream();
        try
        {
            var tracks = JavaScriptService.MediaTrackCount(stream);
            JavaScriptService.StopMediaStream(stream);
            return value + tracks;
        }
        finally
        {
            stream.Dispose();
        }
    }
}
