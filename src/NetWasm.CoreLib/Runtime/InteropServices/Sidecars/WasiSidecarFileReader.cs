// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.Sidecars
{
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
            if (!IsFileName(fileName))
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

        // A name within the mount: not empty, no directory separator and no "..".
        private static bool IsFileName(string name)
        {
            if (name.Length == 0)
            {
                return false;
            }
            for (var index = 0; index < name.Length; index++)
            {
                if (name[index] == '/' || name[index] == '.' && index + 1 < name.Length && name[index + 1] == '.')
                {
                    return false;
                }
            }
            return true;
        }

        // The buffer doubles as chunks arrive and is trimmed to the file's length at the end.
        private byte[] ReadAll(int handle)
        {
            var contents = new byte[ChunkSize];
            var total = 0;
            while (true)
            {
                var chunk = _reader.Read(handle, total, ChunkSize);
                if (chunk.Length > contents.Length - total)
                {
                    contents = Copy(contents, total, Math.Max(checked(contents.Length * 2), checked(total + chunk.Length)));
                }
                chunk.AsSpan().CopyTo(contents.AsSpan(total));
                total += chunk.Length;
                if (chunk.Length < ChunkSize)
                {
                    break;
                }
            }
            return total == contents.Length ? contents : Copy(contents, total, total);
        }

        private static byte[] Copy(byte[] source, int length, int capacity)
        {
            var copy = new byte[capacity];
            source.AsSpan(0, length).CopyTo(copy);
            return copy;
        }
    }
}
