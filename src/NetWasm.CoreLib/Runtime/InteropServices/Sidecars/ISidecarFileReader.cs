namespace System.Runtime.InteropServices.Sidecars
{
    internal interface ISidecarFileReader
    {
        // The whole file of that name under the sidecar mount; throws when it cannot be read.
        byte[] Read(string fileName);
    }
}
