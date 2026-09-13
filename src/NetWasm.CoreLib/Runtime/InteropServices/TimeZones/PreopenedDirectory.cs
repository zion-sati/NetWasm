namespace System.Runtime.InteropServices.TimeZones
{
    internal readonly struct PreopenedDirectory
    {
        internal PreopenedDirectory(int handle, string path)
        {
            Handle = handle;
            Path = path ?? throw new ArgumentNullException();
        }

        internal int Handle { get; }
        internal string Path { get; }
    }
}
