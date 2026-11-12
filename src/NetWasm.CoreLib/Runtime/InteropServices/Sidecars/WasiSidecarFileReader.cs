namespace System.Runtime.InteropServices.Sidecars
{
    using System.Collections.Generic;
    using System.Runtime.InteropServices.TimeZones;

    // Reads a sidecar through wasi:filesystem from the preopened sidecar mount, a chunk at a time until the
    // reader returns a short chunk at the end of the file.
    internal sealed class WasiSidecarFileReader(
        IPreopenedDirectorySource preopens,
        IReadOnlyFileOpener opener,
        IReadOnlyFileReader reader) : ISidecarFileReader
    {
        internal const int ChunkSize = 64 * 1024;
        private readonly IPreopenedDirectorySource _preopens =
            preopens ?? throw new ArgumentNullException();
        private readonly IReadOnlyFileOpener _opener =
            opener ?? throw new ArgumentNullException();
        private readonly IReadOnlyFileReader _reader =
            reader ?? throw new ArgumentNullException();

        public byte[] Read(string fileName)
        {
            if (fileName == null)
            {
                throw new ArgumentNullException();
            }
            if (fileName.Length == 0 || fileName.Contains('/') || fileName.Contains(".."))
            {
                throw new PlatformNotSupportedException(
                    "the sidecar name '" + fileName + "' is not a file name");
            }
            foreach (var directory in _preopens.Read())
            {
                if (directory.Path == SidecarDescriptor.MountPath)
                {
                    return ReadAll(_opener.Open(directory.Handle, fileName));
                }
            }
            throw new PlatformNotSupportedException(
                "the host mounts no " + SidecarDescriptor.MountPath + " directory");
        }

        private byte[] ReadAll(int handle)
        {
            var chunks = new List<byte[]>();
            var total = 0L;
            while (true)
            {
                var chunk = _reader.Read(handle, total, ChunkSize);
                chunks.Add(chunk);
                total += chunk.Length;
                if (chunk.Length < ChunkSize)
                {
                    break;
                }
            }
            var contents = new byte[checked((int)total)];
            var offset = 0;
            foreach (var chunk in chunks)
            {
                chunk.CopyTo(contents, offset);
                offset += chunk.Length;
            }
            return contents;
        }
    }
}
