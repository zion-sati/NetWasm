namespace System.Runtime.InteropServices.Sidecars
{
    // What the compiler tells a program about its sidecars (issue 225): which symbols files it loads, the build
    // ID they must carry, and the program's file name, which their names extend (App.wasm.type-symbols). Every
    // sidecar is mounted read-only under MountPath by its file name.
    internal sealed class SidecarDescriptor(int flags, string buildId, string programFileName)
    {
        internal const string MountPath = "/netwasm-sidecars";
        internal const int TypeSymbolsFlag = 1;
        internal const int MethodSymbolsFlag = 2;

        internal bool LoadsTypeSymbols => (flags & TypeSymbolsFlag) != 0;

        internal bool LoadsMethodSymbols => (flags & MethodSymbolsFlag) != 0;

        internal string BuildId { get; } = buildId ?? throw new ArgumentNullException();

        internal string ProgramFileName { get; } = programFileName ?? throw new ArgumentNullException();

        // The compiler's encoding: i32 flags, i32 build ID length and its UTF-16 characters, i32 file name length
        // and its UTF-16 characters, from a 4-byte aligned address.
        internal static unsafe SidecarDescriptor Read(byte* address)
        {
            if (address == null)
            {
                throw new ArgumentNullException();
            }
            var buildIdLength = *(int*)(address + 4);
            var name = address + 8 + buildIdLength * sizeof(char);
            return new(
                *(int*)address,
                new string((char*)(address + 8), 0, buildIdLength),
                new string((char*)(name + 4), 0, *(int*)name));
        }
    }
}
