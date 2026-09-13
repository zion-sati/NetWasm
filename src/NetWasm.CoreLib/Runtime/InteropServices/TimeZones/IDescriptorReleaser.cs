namespace System.Runtime.InteropServices.TimeZones
{
    internal interface IDescriptorReleaser
    {
        void Release(int handle);
    }
}
