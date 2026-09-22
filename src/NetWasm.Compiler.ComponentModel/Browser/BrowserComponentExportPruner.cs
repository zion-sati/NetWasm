using System;

namespace NetWasm.Compiler.ComponentModel.Browser;

internal static class BrowserComponentExportPruner
{
    private const string InputPath = "merged.wasm";
    private const string OutputPath = "sanitized.wasm";

    public static byte[] Retain(ReadOnlyMemory<byte> module, string prefix)
    {
        var reader = new VirtualModuleReader(module.ToArray());
        var writer = new VirtualModuleWriter();
        new WasmCoreModuleExportEditor(new VirtualModuleExistence(), reader, writer)
            .RetainComponentExports(InputPath, OutputPath, prefix);
        return writer.Module;
    }

    internal sealed class VirtualModuleExistence : IFileExistence
    {
        public bool Exists(string path) => string.Equals(path, InputPath, StringComparison.Ordinal);
    }

    internal sealed class VirtualModuleReader(byte[] module) : IByteFileReader
    {
        public byte[] Read(string path) => string.Equals(path, InputPath, StringComparison.Ordinal)
            ? module
            : throw new InvalidOperationException("The export editor requested an unexpected virtual input.");
    }

    internal sealed class VirtualModuleWriter : IByteFileWriter
    {
        private byte[]? _module;

        public byte[] Module => _module ?? throw new InvalidOperationException(
            "The export editor returned no module.");

        public void Write(string path, byte[] content)
        {
            if (!string.Equals(path, OutputPath, StringComparison.Ordinal) || _module is not null)
            {
                throw new InvalidOperationException("The export editor requested an unexpected virtual output.");
            }
            _module = content.AsSpan().ToArray();
        }
    }
}
