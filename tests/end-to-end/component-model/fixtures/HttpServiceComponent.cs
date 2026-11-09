using System.IO;
using System.Runtime.InteropServices.WebAssembly;
using System.Text;
using System.Threading.Tasks;

namespace NetWasm.Wit.Wasi.Http._0._3._2;

// A wasi:http 0.3 handler: it awaits a delay, then answers with a body streamed from a
// MemoryStream and a trailers future that resolves to none.
public static partial class ServiceExports
{
    public static async partial Task<WitResult<Response, WasiHttpTypesErrorCode>> Handle(Request request)
    {
        request.Dispose();
        await Task.Delay(5);
        var headers = WasiHttpTypesImports.FromList([("content-type", Encoding.UTF8.GetBytes("text/plain"))]).Ok;
        var (response, _) = WasiHttpTypesImports.ResponseNew(
            headers,
            new WitOption<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("hello from a NetWasm wasi:http handler\n"))),
            Task.FromResult(WitResult<WitOption<Fields>, WasiHttpTypesErrorCode>.FromOk(WitOption<Fields>.None)));
        return WitResult<Response, WasiHttpTypesErrorCode>.FromOk(response);
    }

    // The function entry nwc compiles a library from.
    public static int Run(int value) => value;
}
