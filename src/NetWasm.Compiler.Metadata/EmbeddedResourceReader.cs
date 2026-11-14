using System;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace NetWasm.Compiler.Metadata;

// Adapts an assembly's manifest resources to the bytes of one embedded in the image (ECMA-335 II.24.2.4: a length,
// then the data, at the resource's offset into the CLI header's resources directory). A resource another file holds,
// or an image without resource data, has none.
internal sealed class EmbeddedResourceReader : IEmbeddedResourceReader
{
    public byte[]? Read(PEReader image, MetadataReader reader, string name)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(name);
        foreach (var handle in reader.ManifestResources)
        {
            var resource = reader.GetManifestResource(handle);
            if (!resource.Implementation.IsNil || reader.GetString(resource.Name) != name)
                continue;
            // An image with metadata has its CLI header.
            var resources = image.PEHeaders.CorHeader!.ResourcesDirectory;
            if (resources.Size <= 0)
                return null;
            var data = image.GetSectionData(resources.RelativeVirtualAddress).GetReader();
            data.Offset = checked((int)resource.Offset);
            return data.ReadBytes(data.ReadInt32());
        }
        return null;
    }
}
