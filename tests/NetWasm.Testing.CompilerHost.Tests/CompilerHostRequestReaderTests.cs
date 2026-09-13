using System.Collections.Immutable;
using NetWasm.Testing.CompilerHost;

namespace NetWasm.Testing.CompilerHost.Tests;

public sealed class CompilerHostRequestReaderTests
{
    [Fact]
    public void ReadsARequestUsingTheInjectedDeserializerOptions()
    {
        var path = Path.Combine(Path.GetTempPath(), $"compiler-host-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """
                {
                  "ENTRYASSEMBLYPATH": "entry.dll",
                  "referencePaths": [],
                  "entryTypeName": "Application",
                  "entryMethodName": "Run",
                  "exports": [],
                  "target": 0,
                  "sourcePaths": [],
                  "referenceAssemblyAliases": {},
                  "modulePath": "module.wasm"
                }
                """);
            var reader = new CompilerHostRequestReader(
                new CompilerHostRequestDeserializer(
                    new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                    }));

            var request = reader.Read(path);

            Assert.Equal("entry.dll", request.EntryAssemblyPath);
            Assert.Equal("Application", request.EntryTypeName);
            Assert.Empty(request.ReferencePaths);
            Assert.Equal(ImmutableDictionary<string, string>.Empty, request.ReferenceAssemblyAliases);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
