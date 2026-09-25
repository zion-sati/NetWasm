using System;
using System.Runtime.InteropServices.WebAssembly;

namespace NetWasm.Fixtures.ComponentModel;

public static class WasiStreamComponent
{
    [WitExport("netwasm:test-stream@1.0.0/acceptance", "write")]
    public static unsafe uint Write()
    {
        var output = GetStandardOutput();
        try
        {
            var readinessStorage = stackalloc long[2];
            var readiness = (byte*)readinessStorage;
            CheckWrite(output, (nuint)readiness);
            if (readiness[0] != 0)
            {
                ThrowStreamError(readiness + 8);
            }
            if (*(long*)(readiness + 8) < 4)
            {
                throw new InvalidOperationException();
            }

            var contents = stackalloc byte[4] { 1, 2, 3, 4 };
            var resultStorage = stackalloc int[3];
            var result = (byte*)resultStorage;
            WriteOutput(output, (nuint)contents, 4, (nuint)result);
            if (result[0] != 0)
            {
                ThrowStreamError(result + 4);
            }
        }
        finally
        {
            DropOutput(output);
        }

        return 42;
    }

    public static int Run(int value) => value;

    [WitImport("wasi:cli@0.2.11/stdout", "get-stdout")]
    private static extern int GetStandardOutput();

    [WitImport("wasi:io@0.2.11/streams", "[method]output-stream.check-write")]
    private static extern void CheckWrite(int handle, nuint result);

    [WitImport("wasi:io@0.2.11/streams", "[method]output-stream.write")]
    private static extern void WriteOutput(
        int handle,
        nuint contents,
        nuint length,
        nuint result);

    [WitImport("wasi:io@0.2.11/streams", "[resource-drop]output-stream")]
    private static extern void DropOutput(int handle);

    [WitImport("wasi:io@0.2.11/error", "[resource-drop]error")]
    private static extern void DropError(int handle);

    private static unsafe void ThrowStreamError(byte* payload)
    {
        if (payload[0] == 0)
        {
            DropError(*(int*)(payload + 4));
        }
        else if (payload[0] != 1)
        {
            throw new ArgumentException();
        }
        throw new InvalidOperationException();
    }
}
