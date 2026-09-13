namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class LocalTimeOffsetResolverInstaller :
        ILocalTimeOffsetResolverInstaller
    {
        public void Install(ILocalTimeOffsetResolver resolver) =>
            PlatformServices.UseLocalTimeOffsetResolver(resolver);
    }
}
