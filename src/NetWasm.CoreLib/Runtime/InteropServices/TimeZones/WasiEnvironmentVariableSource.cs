namespace System.Runtime.InteropServices.TimeZones
{
    using System.Runtime.InteropServices.WebAssembly;

    internal sealed class WasiEnvironmentVariableSource(
        IWasiEnvironmentInvoker invoker) : IEnvironmentVariableSource
    {
        private readonly IWasiEnvironmentInvoker _invoker =
            invoker ?? throw new ArgumentNullException();

        public EnvironmentVariable[] Read()
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
                var variables = new EnvironmentVariable[(int)length];
                var stride = addressSize * 4;
                for (var index = 0; index < variables.Length; index++)
                {
                    var element = elements + unchecked((nuint)index) * stride;
                    var nameAddress = CanonicalAbi.ReadAddress(element, 0);
                    var nameLength = CanonicalAbi.ReadAddress(element, addressSize);
                    var valueAddress = CanonicalAbi.ReadAddress(element, addressSize * 2);
                    var valueLength = CanonicalAbi.ReadAddress(element, addressSize * 3);
                    try
                    {
                        variables[index] = new EnvironmentVariable(
                            CanonicalAbi.LiftString(nameAddress, nameLength),
                            CanonicalAbi.LiftString(valueAddress, valueLength));
                    }
                    finally
                    {
                        CanonicalAbi.Free(nameAddress);
                        if (valueLength != 0)
                        {
                            CanonicalAbi.Free(valueAddress);
                        }
                    }
                }
                if (length != 0)
                {
                    CanonicalAbi.Free(elements);
                }
                return variables;
            }
            finally
            {
                CanonicalAbi.Free(result);
            }
        }

    }
}
