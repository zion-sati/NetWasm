namespace System.Runtime.InteropServices.TimeZones
{
    using System.Runtime.InteropServices.Concurrency;
    using System.Runtime.InteropServices.WebAssembly;

    // Opens a file read-only, following no symlinks, and waits for open-at to
    // return its result<descriptor, error-code>.
    internal sealed class WasiDescriptorOpenInvoker : IWasiDescriptorOpenInvoker
    {
        // descriptor, path-flags, path (address, length), open-flags,
        // descriptor-flags: a 20-byte, 4-aligned record.
        private const nuint ParametersSize = 20;
        private const byte ReadFlag = 1;

        private readonly IWasiOpenAtInvoker _openAt;
        private readonly ISubtaskReturnAwaiter _subtasks;
        private readonly ICanonicalBufferAllocator _allocator;

        internal WasiDescriptorOpenInvoker(
            IWasiOpenAtInvoker openAt,
            ISubtaskReturnAwaiter subtasks,
            ICanonicalBufferAllocator allocator)
        {
            _openAt = openAt ?? throw new ArgumentNullException();
            _subtasks = subtasks ?? throw new ArgumentNullException();
            _allocator = allocator ?? throw new ArgumentNullException();
        }

        public void Invoke(int directoryHandle, nuint path, nuint pathLength, nuint result)
        {
            using var parameters = _allocator.Allocate(ParametersSize, 4);
            CanonicalAbi.WriteInt32(parameters.Address, 0, directoryHandle);
            CanonicalAbi.WriteByte(parameters.Address, 4, 0);
            CanonicalAbi.WriteInt32(parameters.Address, 8, unchecked((int)path));
            CanonicalAbi.WriteInt32(parameters.Address, 12, unchecked((int)pathLength));
            CanonicalAbi.WriteByte(parameters.Address, 16, 0);
            CanonicalAbi.WriteByte(parameters.Address, 17, ReadFlag);
            _subtasks.AwaitReturn(_openAt.Invoke(parameters.Address, result));
        }
    }
}
