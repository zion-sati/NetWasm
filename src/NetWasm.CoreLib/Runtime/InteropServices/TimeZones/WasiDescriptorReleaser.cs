namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class WasiDescriptorReleaser : IDescriptorReleaser
    {
        public void Release(int handle) => WasiTimeZoneImports.DropDescriptor(handle);
    }
}
