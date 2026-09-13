using System.Text.Json;

namespace NetWasm.Hosting.Generator;

internal interface IPreview2ShimIdentityReader
{
    Preview2ShimIdentity Read(string packageJson);
}

internal sealed class Preview2ShimIdentityReader : IPreview2ShimIdentityReader
{
    internal const string ExpectedPackage = "@bytecodealliance/preview2-shim";
    internal const string ExpectedVersion = "0.24.1";

    public Preview2ShimIdentity Read(string packageJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageJson);
        try
        {
            using var document = JsonDocument.Parse(packageJson);
            var root = document.RootElement;
            var package = root.GetProperty("name").GetString();
            var version = root.GetProperty("version").GetString();
            if (package != ExpectedPackage || version != ExpectedVersion)
            {
                throw new InvalidDataException(
                    $"Preview 2 shim must be {ExpectedPackage} {ExpectedVersion}.");
            }
            return new(package, version);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Preview 2 shim package metadata is invalid.", exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidDataException("Preview 2 shim package metadata is invalid.", exception);
        }
        catch (KeyNotFoundException exception)
        {
            throw new InvalidDataException("Preview 2 shim package metadata is invalid.", exception);
        }
    }
}
