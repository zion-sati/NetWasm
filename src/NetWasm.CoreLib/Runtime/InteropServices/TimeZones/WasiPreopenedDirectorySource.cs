namespace System.Runtime.InteropServices.TimeZones
{
    using System.Runtime.InteropServices.WebAssembly;

    internal sealed class WasiPreopenedDirectorySource(
        IWasiPreopensInvoker invoker) : IPreopenedDirectorySource
    {
        private readonly IWasiPreopensInvoker _invoker =
            invoker ?? throw new ArgumentNullException();

        public PreopenedDirectory[] Read()
        {
            var addressSize = unchecked((nuint)nuint.Size);
            var result = CanonicalAbi.Allocate(addressSize * 2, addressSize);
            try
            {
                _invoker.Invoke(result);
                var elements = CanonicalAbi.ReadAddress(result, 0);
                var length = CanonicalAbi.ReadAddress(result, addressSize);
                if (length > int.MaxValue)
                {
                    throw new OutOfMemoryException();
                }
                var directories = new PreopenedDirectory[(int)length];
                var stride = addressSize * 3;
                for (var index = 0; index < directories.Length; index++)
                {
                    var element = elements + unchecked((nuint)index) * stride;
                    var pathAddress = CanonicalAbi.ReadAddress(element, addressSize);
                    var pathLength = CanonicalAbi.ReadAddress(element, addressSize * 2);
                    try
                    {
                        directories[index] = new PreopenedDirectory(
                            CanonicalAbi.ReadInt32(element, 0),
                            CanonicalAbi.LiftString(pathAddress, pathLength));
                    }
                    finally
                    {
                        if (pathLength != 0)
                        {
                            CanonicalAbi.Free(pathAddress);
                        }
                    }
                }
                if (length != 0)
                {
                    CanonicalAbi.Free(elements);
                }
                return directories;
            }
            finally
            {
                CanonicalAbi.Free(result);
            }
        }

    }
}
