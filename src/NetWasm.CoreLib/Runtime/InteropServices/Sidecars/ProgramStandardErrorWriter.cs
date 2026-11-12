namespace System.Runtime.InteropServices.Sidecars
{
    using System.Runtime.InteropServices.WebAssembly;

    // Writes to the program's own standard error, as Environment.FailFast does, without Console.
    internal sealed class ProgramStandardErrorWriter : IStandardErrorWriter
    {
        public void Write(ReadOnlySpan<byte> bytes) => PlatformServices.StandardStreams.WriteError(bytes);
    }
}
