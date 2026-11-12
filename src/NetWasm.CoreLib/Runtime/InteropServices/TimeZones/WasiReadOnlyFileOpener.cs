namespace System.Runtime.InteropServices.TimeZones
{
    using System.Runtime.InteropServices.FileSystem;
    using System.Runtime.InteropServices.WebAssembly;
    using System.Text;

    internal sealed class WasiReadOnlyFileOpener(
        IWasiDescriptorOpenInvoker invoker) : IReadOnlyFileOpener
    {
        private readonly IWasiDescriptorOpenInvoker _invoker =
            invoker ?? throw new ArgumentNullException();

        public int Open(int directoryHandle, string relativePath)
        {
            if (relativePath == null)
            {
                throw new ArgumentNullException();
            }
            using var path = LowerString(relativePath);
            // result<descriptor, error-code>: a discriminant byte, then at the pointer size the
            // descriptor or the error-code variant, whose other(option<string>) case makes the
            // whole result five pointers.
            var pointer = WasiFileSystemLayout.Pointer;
            var result = CanonicalAbi.Allocate(WasiFileSystemLayout.SmallResultSize, pointer);
            try
            {
                _invoker.Invoke(directoryHandle, path.Address, path.Length, result);
                return CanonicalAbi.ReadByte(result, 0) switch
                {
                    0 => CanonicalAbi.ReadInt32(result, pointer),
                    1 => throw Failure("open", CanonicalAbi.ReadByte(result, pointer)),
                    _ => throw new PlatformNotSupportedException(
                        "WASI filesystem returned an invalid open result."),
                };
            }
            finally
            {
                CanonicalAbi.Free(result);
            }
        }

        private static CanonicalBuffer LowerString(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            var address = CanonicalAbi.Allocate(unchecked((nuint)bytes.Length), 1);
            for (var index = 0; index < bytes.Length; index++)
            {
                CanonicalAbi.WriteByte(address, unchecked((nuint)index), bytes[index]);
            }
            return new CanonicalBuffer(address, unchecked((nuint)bytes.Length));
        }

        private static PlatformNotSupportedException Failure(string operation, int code) =>
            new("WASI filesystem could not " + operation +
                " the timezone asset (error-code=" + code + ").");
    }
}
