namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class TimeZoneAssetFileOpener(
        IPreopenedDirectorySource preopens,
        IReadOnlyFileOpener opener,
        IReadOnlyFileReader reader) : ITimeZoneAssetFileOpener
    {
        private readonly IPreopenedDirectorySource _preopens =
            preopens ?? throw new ArgumentNullException();
        private readonly IReadOnlyFileOpener _opener =
            opener ?? throw new ArgumentNullException();
        private readonly IReadOnlyFileReader _reader =
            reader ?? throw new ArgumentNullException();

        // The descriptor stays open for the process: zones are read when first used.
        public ITimeZoneAssetFile Open(string assetName)
        {
            if (assetName == null)
            {
                throw new ArgumentNullException();
            }
            if (assetName.Length == 0 || assetName[0] == '/' || assetName.Contains(".."))
            {
                throw new PlatformNotSupportedException(
                    "Timezone asset name is not a normalized relative path.");
            }
            foreach (var directory in _preopens.Read())
            {
                if (directory.Path == Sidecars.SidecarDescriptor.MountPath)
                {
                    return new TimeZoneAssetFile(
                        _opener.Open(directory.Handle, assetName),
                        _reader);
                }
            }
            throw new PlatformNotSupportedException(
                "Timezone asset mount '" + Sidecars.SidecarDescriptor.MountPath + "' is unavailable.");
        }
    }
}
