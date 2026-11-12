namespace System.Runtime.InteropServices.Sidecars
{
    using System.Runtime.InteropServices.WebAssembly;

    // Loads one symbols file: <program>.wasm<suffix>. A program keeps working without it (issue 225, owner
    // 2026-11-12): a file that is missing, unreadable, malformed or from another build is skipped with one line to
    // standard error, and the program runs as if built without it (consequence).
    internal sealed class SymbolSidecarLoader(
        ISidecarFileReader files,
        ISymbolSidecarParser parser,
        ISymbolSidecarInstaller installer,
        IStandardErrorWriter errors,
        string suffix,
        ushort kind,
        string consequence) : ISymbolSidecarLoader
    {
        private readonly ISidecarFileReader _files = files ?? throw new ArgumentNullException();
        private readonly ISymbolSidecarParser _parser = parser ?? throw new ArgumentNullException();
        private readonly ISymbolSidecarInstaller _installer = installer ?? throw new ArgumentNullException();
        private readonly IStandardErrorWriter _errors = errors ?? throw new ArgumentNullException();
        private readonly string _suffix = suffix ?? throw new ArgumentNullException();
        private readonly string _consequence = consequence ?? throw new ArgumentNullException();

        public void Load(SidecarDescriptor descriptor)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException();
            }
            var fileName = descriptor.ProgramFileName + _suffix;
            try
            {
                _installer.Install(_parser.Parse(_files.Read(fileName), kind, descriptor.BuildId));
            }
            catch (Exception exception)
            {
                _errors.Write(Text.Encoding.UTF8.GetBytes(
                    "NetWasm: skipped " + SidecarDescriptor.MountPath + "/" + fileName + " (" +
                    exception.Message + "); " + _consequence + ".\n"));
            }
        }
    }
}
