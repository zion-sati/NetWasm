namespace System.Runtime.InteropServices.TimeZones
{
    using System.Runtime.InteropServices.Concurrency;
    using System.Runtime.InteropServices.FileSystem;
    using System.Runtime.InteropServices.WebAssembly;

    // Opens a file read-only, following no symlinks, and waits for open-at to
    // return its result<descriptor, error-code>.
    internal sealed class WasiDescriptorOpenInvoker : IWasiDescriptorOpenInvoker
    {
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
            // The path's address and length are pointer-sized, so the record is the target's
            // (wasm64's widens it from 20 to 32 bytes).
            var pointer = WasiFileSystemLayout.Pointer;
            using var parameters = _allocator.Allocate(WasiFileSystemLayout.OpenAtParametersSize, pointer);
            CanonicalAbi.WriteInt32(parameters.Address, 0, directoryHandle);
            CanonicalAbi.WriteByte(parameters.Address, 4, 0);
            CanonicalAbi.WriteAddress(parameters.Address, 8, path);
            CanonicalAbi.WriteAddress(parameters.Address, 8 + pointer, pathLength);
            CanonicalAbi.WriteByte(parameters.Address, 8 + pointer * 2, 0);
            CanonicalAbi.WriteByte(parameters.Address, 9 + pointer * 2, ReadFlag);
            _subtasks.AwaitReturn(_openAt.Invoke(parameters.Address, result));
        }
    }
}
