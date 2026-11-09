namespace System.Runtime.InteropServices.WebAssembly
{
    // The length of a synchronous WIT export. The Component Model lets a synchronous task block
    // only after it returns, so a blocking wait inside one throws instead of the host trapping.
    // Generated bindings enter the scope through CanonicalAbi.EnterSynchronousExport.
    public readonly struct SynchronousExportScope : IDisposable
    {
        private readonly bool _entered;

        private SynchronousExportScope(bool entered)
        {
            _entered = entered;
        }

        // Synchronous exports entered and not yet returned; a synchronous export calling another
        // through an import cannot reenter the instance, but nesting stays balanced regardless.
        internal static int Depth { get; private set; }

        internal static SynchronousExportScope Enter()
        {
            Depth++;
            return new SynchronousExportScope(true);
        }

        public void Dispose()
        {
            if (_entered)
            {
                Depth--;
            }
        }
    }
}
