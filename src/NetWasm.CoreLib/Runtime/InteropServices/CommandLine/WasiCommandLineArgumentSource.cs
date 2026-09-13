namespace System.Runtime.InteropServices.CommandLine
{
    using System.Runtime.InteropServices.WebAssembly;

    internal sealed class WasiCommandLineArgumentSource(
        IWasiCommandLineInvoker invoker) : ICommandLineArgumentSource
    {
        private readonly IWasiCommandLineInvoker _invoker =
            invoker ?? throw new ArgumentNullException();

        public string[] Read()
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
                    CanonicalAbi.Free(elements);
                    throw new OutOfMemoryException();
                }

                try
                {
                    var arguments = new string[(int)length];
                    var stride = addressSize * 2;
                    for (var index = 0; index < arguments.Length; index++)
                    {
                        var element = elements + unchecked((nuint)index) * stride;
                        var address = CanonicalAbi.ReadAddress(element, 0);
                        var itemLength = CanonicalAbi.ReadAddress(element, addressSize);
                        try
                        {
                            arguments[index] = CanonicalAbi.LiftString(address, itemLength);
                        }
                        finally
                        {
                            CanonicalAbi.Free(address);
                        }
                    }
                    return arguments;
                }
                finally
                {
                    CanonicalAbi.Free(elements);
                }
            }
            finally
            {
                CanonicalAbi.Free(result);
            }
        }
    }
}
