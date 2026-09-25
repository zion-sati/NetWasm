using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.PortableExecutable;
using System.Text;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Metadata.Tests;

using static NetWasm.Compiler.Metadata.Tests.MetadataTestData;

public sealed class ManagedAssemblyTests
{
    [Fact]
    public void PreservesExplicitFieldOffsetsAndAbsentSequentialOffsets()
    {
        var builder = new PersistedAssemblyBuilder(new AssemblyName("LayoutMetadata"), typeof(object).Assembly);
        var module = builder.DefineDynamicModule("LayoutMetadata");
        var explicitType = module.DefineType("Fixture.Union", TypeAttributes.Public |
            TypeAttributes.ExplicitLayout | TypeAttributes.Sealed, typeof(ValueType));
        explicitType.DefineField("First", typeof(int), FieldAttributes.Public).SetOffset(0);
        explicitType.DefineField("Second", typeof(short), FieldAttributes.Public).SetOffset(2);
        _ = explicitType.CreateType();
        var packedType = module.DefineType("Fixture.Sequential", TypeAttributes.Public |
            TypeAttributes.SequentialLayout | TypeAttributes.Sealed, typeof(ValueType));
        packedType.DefineField("Value", typeof(int), FieldAttributes.Public);
        _ = packedType.CreateType();
        using var image = new MemoryStream();
        builder.Save(image);
        using var assembly = ManagedAssembly.Parse("memory/layout.dll", image.ToArray(),
            AssemblyIdentityAliases.Empty, new ValueTypeDefinitionStackKindResolver());
        Assert.Equal(0, assembly.Fields.Values.Single(field => field.Name == "First").ExplicitOffset);
        Assert.Equal(2, assembly.Fields.Values.Single(field => field.Name == "Second").ExplicitOffset);
        Assert.Null(assembly.Fields.Values.Single(field => field.Name == "Value").ExplicitOffset);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManagedAssemblyPreservesBeforeFieldInitMetadata(bool beforeFieldInit)
    {
        var builder = new PersistedAssemblyBuilder(new AssemblyName("InitializationMetadata"), typeof(object).Assembly);
        var module = builder.DefineDynamicModule("InitializationMetadata");
        var attributes = TypeAttributes.Public;
        if (beforeFieldInit) attributes |= TypeAttributes.BeforeFieldInit;
        _ = module.DefineType("Fixture.Owner", attributes).CreateType();
        using var image = new MemoryStream();
        builder.Save(image);
        using var assembly = ManagedAssembly.Parse("memory/initialization.dll", image.ToArray(),
            AssemblyIdentityAliases.Empty, new ValueTypeDefinitionStackKindResolver());

        Assert.Equal(beforeFieldInit, assembly.Types.Values.Single(type => type.FullName == "Fixture.Owner").IsBeforeFieldInit);
    }

    [Fact]
    public void ManagedAssemblyReadsDefinitionsReferencesBodiesAndBaseTypes()
    {
        using var assets = TestAssets.Create();
        using var assembly = ManagedAssemblyTestFactory.Load(assets.Application);

        Assert.Equal("Fixture.Application", assembly.Identity.Name);
        Assert.Equal(ExpectedReferences, assembly.References);
        var counter = assembly.Types.Values.Single(
            type => type.FullName == "NetWasm.Fixtures.Application.Counter");
        var add = counter.Methods
            .Select(key => assembly.Methods[key.MetadataToken])
            .Single(method => method.Name == "Add");
        var symbols = Symbols(assembly);
        Assert.Equal("NetWasm.Fixtures.Application.Counter::Add", symbols.Format(add));
        Assert.False(assembly.Metadata.BaseTypes[counter.Key.MetadataToken].IsNil);
        var bodies = new MetadataMethodBodyBlockReader(symbols);
        Assert.NotEmpty(
            ((IMetadataMethodBodyBlockReader)bodies)
                .Read(assembly.PortableExecutableReader, add).GetILBytes()!);
        Assert.Contains(
            assembly.Fields.Values,
            field => field.Name == "_value" && !field.IsStatic);
        var contentSha256 = assembly.ContentSha256;
        Assert.Equal(64, contentSha256.Length);
        Assert.Same(contentSha256, assembly.ContentSha256);
    }

    [Fact]
    public void ManagedAssemblyParsesAnInMemoryImage()
    {
        using var assets = TestAssets.Create();
        var image = File.ReadAllBytes(assets.Application);

        using var assembly = ManagedAssembly.Parse(
            "memory/application.dll",
            image,
            AssemblyIdentityAliases.Empty,
            new ValueTypeDefinitionStackKindResolver());

        Assert.Equal("Fixture.Application", assembly.Identity.Name);
    }

    [Fact]
    public void ManagedAssemblyRecognizesAnEnumWithALocalSystemEnumBase()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "LocalEnum.dll");
            var assembly = new PersistedAssemblyBuilder(
                new AssemblyName("LocalEnum"), typeof(object).Assembly);
            var module = assembly.DefineDynamicModule("LocalEnum");
            var enumBaseBuilder = module.DefineType("System.Enum",
                TypeAttributes.Public | TypeAttributes.Abstract,
                typeof(ValueType));
            var enumBase = enumBaseBuilder.CreateType();
            var enumBuilder = module.DefineType("Fixture.LocalEnum",
                TypeAttributes.Public | TypeAttributes.Sealed, enumBase);
            _ = enumBuilder.DefineField("value__", typeof(int),
                FieldAttributes.Public | FieldAttributes.SpecialName |
                FieldAttributes.RTSpecialName);
            _ = enumBuilder.CreateType();
            assembly.Save(path);

            using var parsed = ManagedAssemblyTestFactory.Load(path);

            var type = parsed.Types.Values.Single(candidate =>
                candidate.FullName == "Fixture.LocalEnum");
            Assert.True(type.IsEnum);
            Assert.Equal("primitive:i4", type.EnumUnderlyingType.CanonicalName);
            var handle = parsed.Reader.TypeDefinitions.Single(candidate =>
                parsed.Reader.GetString(parsed.Reader.GetTypeDefinition(candidate).Name) ==
                "LocalEnum");
            var identity = new SignatureTypeProvider(parsed.Identity, parsed.Reader)
                .GetTypeFromDefinition(parsed.Reader, handle, 0x11);
            Assert.Equal("primitive:i4", identity.StackStorageType?.CanonicalName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ManagedAssemblyResolvesLocalAndGlobalAttributeConstructorShapes()
    {
        using var assets = TestAssets.Create();
        var marker = assets.CompileSource(
            "GlobalMarker",
            """
            using System;
            public sealed class GlobalMarkerAttribute : Attribute { }
            """);
        var target = assets.CompileSource(
            "AttributeShapes",
            """
            using System;

            public sealed class LocalMarkerAttribute : Attribute { }

            [LocalMarker]
            [GlobalMarker]
            public sealed class EntryPoint { }
            """,
            marker);

        using var assembly = ManagedAssemblyTestFactory.Load(target);

        Assert.Contains(assembly.Types.Values, type => type.FullName == "EntryPoint");
    }

    [Fact]
    public void ManagedAssemblyRejectsInvalidArgumentsAndMalformedImages()
    {
        Assert.Throws<ArgumentException>(() => ManagedAssemblyTestFactory.Load(""));
        using var assets = TestAssets.Create();
        var malformed = Path.Combine(assets.Directory, "Malformed.dll");
        File.WriteAllBytes(malformed, [1, 2, 3, 4]);
        var exception = Assert.Throws<CompilerException>(
            () => ManagedAssemblyTestFactory.Load(malformed));
        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
        Assert.Contains("not a valid managed assembly", exception.Message);
    }

    [Fact]
    public void ManagedAssemblyReportsAMalformedMethodBodyAsACompilerDiagnostic()
    {
        using var assets = TestAssets.Create();
        int relativeVirtualAddress;
        using (var original = ManagedAssemblyTestFactory.Load(assets.Application))
        {
            relativeVirtualAddress = original.Methods.Values
                .First(method => method.HasBody)
                .RelativeVirtualAddress;
        }
        var bytes = File.ReadAllBytes(assets.Application);
        using (var pe = new PEReader(new MemoryStream(bytes, writable: false)))
        {
            var section = pe.PEHeaders.SectionHeaders.Single(candidate =>
            {
                var length = Math.Max(candidate.VirtualSize, candidate.SizeOfRawData);
                return relativeVirtualAddress >= candidate.VirtualAddress &&
                       relativeVirtualAddress < candidate.VirtualAddress + length;
            });
            var offset = section.PointerToRawData + relativeVirtualAddress -
                section.VirtualAddress;
            bytes[offset] = 0;
        }
        var malformed = Path.Combine(assets.Directory, "MalformedBody.dll");
        File.WriteAllBytes(malformed, bytes);
        using var assembly = ManagedAssemblyTestFactory.Load(malformed);
        var method = assembly.Methods.Values.First(candidate =>
            candidate.RelativeVirtualAddress == relativeVirtualAddress);

        var symbols = Symbols(assembly);
        var bodies = new MetadataMethodBodyBlockReader(symbols);
        var exception = Assert.Throws<CompilerException>(() =>
            ((IMetadataMethodBodyBlockReader)bodies)
                .Read(assembly.PortableExecutableReader, method));

        Assert.Equal(DiagnosticCode.InvalidCil, exception.Diagnostic.Code);
        Assert.Equal(symbols.Format(method), exception.Diagnostic.Method);
        Assert.Contains("method body is malformed", exception.Message);
    }

    [Fact]
    public void ManagedAssemblyReadsJSImportAndJSExportDeclarations()
    {
        using var assets = TestAssets.Create();
        var assemblyPath = assets.CompileSource(
            "InteropDeclarations",
            """
            public static class InteropDeclarations
            {
                [System.Runtime.InteropServices.JavaScript.JSImport("sum", "consumer.math")]
                public static extern int Sum(int left, int right);

                [System.Runtime.InteropServices.JavaScript.JSExport("published")]
                public static int Published(int value) => value;
            }
            """);
        using var assembly = ManagedAssemblyTestFactory.Load(assemblyPath);
        var type = assembly.Types.Values.Single(
            candidate => candidate.FullName == "InteropDeclarations");
        var methods = type.Methods
            .Select(key => assembly.Methods[key.MetadataToken])
            .ToDictionary(method => method.Name);

        Assert.Equal("sum", methods["Sum"].JSImport!.FunctionName);
        Assert.Equal("consumer.math", methods["Sum"].JSImport!.ModuleName);
        Assert.Equal("published", methods["Published"].JSExport!.ExportName);
    }

    [Fact]
    public void ManagedAssemblyReadsEverySupportedInteropDeclarationShape()
    {
        using var assets = TestAssets.Create();
        var assemblyPath = assets.CompileSource(
            "AllInteropDeclarations",
            """
            using System.Runtime.InteropServices.JavaScript;
            using System.Runtime.InteropServices.WebAssembly;

            public static class AllInteropDeclarations
            {
                [JSImport("local")] public static extern int ImportLocal();
                [JSImportPromise("promise", "consumer.async")] public static extern int ImportPromise();
                [JSExport] public static int ExportDefault(int value) => value;
                [WitImport("", "import-value")] public static extern int ImportValue();
                [WitExport("example:api", "export-value")] public static int ExportValue(int value) => value;
                [WitPostReturn("", "post-value")] public static void PostReturn() { }
            }
            """);
        using var assembly = ManagedAssemblyTestFactory.Load(assemblyPath);
        var methods = assembly.Types.Values
            .Single(type => type.FullName == "AllInteropDeclarations")
            .Methods
            .Select(key => assembly.Methods[key.MetadataToken])
            .ToDictionary(method => method.Name);

        Assert.Null(methods["ImportLocal"].JSImport!.ModuleName);
        Assert.True(methods["ImportPromise"].JSImport!.IsPromise);
        Assert.Null(methods["ExportDefault"].JSExport!.ExportName);
        Assert.Equal("import-value", methods["ImportValue"].WitImport!.FunctionName);
        Assert.Equal("example:api", methods["ExportValue"].WitExport!.InterfaceName);
        Assert.Equal("post-value", methods["PostReturn"].WitPostReturn!.FunctionName);
    }

    [Fact]
    public void ManagedAssemblyReadsInlineArrayLengthMetadata()
    {
        using var assets = TestAssets.Create();
        var assemblyPath = assets.CompileSource(
            "InlineArrayMetadata",
            """
            using System.Runtime.CompilerServices;

            [InlineArray(3)]
            public struct InlineValue
            {
                private int _element;
            }
            """);
        using var assembly = ManagedAssemblyTestFactory.Load(assemblyPath);

        var type = assembly.Types.Values.Single(candidate =>
            candidate.FullName == "InlineValue");
        Assert.Equal(3, type.InlineArrayLength);
    }

    [Fact]
    public void ManagedAssemblyRejectsMalformedInlineArrayAttributeProlog()
    {
        using var assets = TestAssets.Create();
        var validPath = assets.CompileSource(
            "MalformedInlineArrayProlog",
            """
            using System.Runtime.CompilerServices;

            [InlineArray(3)] public struct InlineValue { private int _element; }
            """);
        using var valid = ManagedAssemblyTestFactory.Load(validPath);
        var type = valid.Reader.TypeDefinitions.Single(handle =>
            valid.Reader.GetString(valid.Reader.GetTypeDefinition(handle).Name) ==
            "InlineValue");
        var attribute = valid.Reader.GetCustomAttributes(type)
            .Select(valid.Reader.GetCustomAttribute)
            .Single();
        var bytes = File.ReadAllBytes(validPath);
        var blob = valid.Reader.GetBlobBytes(attribute.Value);
        var offset = FindBlob(bytes, blob);
        bytes[offset] = 0;
        var malformedPath = Path.Combine(assets.Directory, "MalformedInlineArrayProlog.dll");
        File.WriteAllBytes(malformedPath, bytes);

        var exception = Assert.Throws<CompilerException>(() =>
            ManagedAssemblyTestFactory.Load(malformedPath));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
    }

    [Fact]
    public void ManagedAssemblyRejectsMalformedInlineArrayLengthAndNamedArguments()
    {
        using var assets = TestAssets.Create();
        var validPath = assets.CompileSource(
            "MalformedInlineArrayPayload",
            """
            using System.Runtime.CompilerServices;

            [InlineArray(3)] public struct InlineValue { private int _element; }
            """);
        using var valid = ManagedAssemblyTestFactory.Load(validPath);
        var type = valid.Reader.TypeDefinitions.Single(handle =>
            valid.Reader.GetString(valid.Reader.GetTypeDefinition(handle).Name) ==
            "InlineValue");
        var attribute = valid.Reader.GetCustomAttributes(type)
            .Select(valid.Reader.GetCustomAttribute)
            .Single();
        var blob = valid.Reader.GetBlobBytes(attribute.Value);

        var invalidLength = File.ReadAllBytes(validPath);
        var lengthOffset = FindBlob(invalidLength, blob) + sizeof(ushort);
        Array.Clear(invalidLength, lengthOffset, sizeof(int));
        var invalidLengthPath = Path.Combine(assets.Directory, "MalformedInlineArrayLength.dll");
        File.WriteAllBytes(invalidLengthPath, invalidLength);
        Assert.Throws<CompilerException>(() => ManagedAssemblyTestFactory.Load(invalidLengthPath));

        var invalidNamedArguments = File.ReadAllBytes(validPath);
        var namedArgumentsOffset = FindBlob(invalidNamedArguments, blob) + blob.Length - sizeof(ushort);
        invalidNamedArguments[namedArgumentsOffset] = 1;
        var invalidNamedArgumentsPath = Path.Combine(
            assets.Directory, "MalformedInlineArrayNamedArguments.dll");
        File.WriteAllBytes(invalidNamedArgumentsPath, invalidNamedArguments);
        Assert.Throws<CompilerException>(() => ManagedAssemblyTestFactory.Load(invalidNamedArgumentsPath));
    }

    [Fact]
    public void ManagedAssemblyRejectsMultipleInlineArrayAttributes()
    {
        using var assets = TestAssets.Create();
        var path = assets.CompileSource(
            "DuplicateInlineArray",
            """
            using System;

            namespace System.Runtime.CompilerServices
            {
                [AttributeUsage(AttributeTargets.Struct, AllowMultiple = true)]
                public sealed class InlineArrayAttribute(int length) : Attribute
                {
                    public int Length { get; } = length;
                }
            }

            [System.Runtime.CompilerServices.InlineArray(3)]
            [System.Runtime.CompilerServices.InlineArray(4)]
            public struct InlineValue { private int _element; }
            """);

        var exception = Assert.Throws<CompilerException>(() => ManagedAssemblyTestFactory.Load(path));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
        Assert.Contains("multiple InlineArray", exception.Message);
    }

    [Fact]
    public void ManagedAssemblyRejectsMalformedInteropAttributePayloads()
    {
        using var assets = TestAssets.Create();
        var validPath = assets.CompileSource(
            "MalformedInteropPayload",
            """
            using System.Runtime.InteropServices.JavaScript;
            using System.Runtime.InteropServices.WebAssembly;

            public static class EntryPoint
            {
                [JSImport("sum")] public static extern int Import(int value);
                [JSExport("export-unique")] public static int Export(int value) => value;
                [WitImport("", "import")] public static extern int WitImport(int value);
            }
            """);
        using var valid = ManagedAssemblyTestFactory.Load(validPath);

        var importMethod = valid.Reader.MethodDefinitions.Single(handle =>
            valid.Reader.GetString(valid.Reader.GetMethodDefinition(handle).Name) == "Import");
        var importAttribute = valid.Reader.GetCustomAttributes(importMethod)
            .Select(valid.Reader.GetCustomAttribute)
            .Single();
        var exportMethod = valid.Reader.MethodDefinitions.Single(handle =>
            valid.Reader.GetString(valid.Reader.GetMethodDefinition(handle).Name) == "Export");
        var exportAttribute = valid.Reader.GetCustomAttributes(exportMethod)
            .Select(valid.Reader.GetCustomAttribute)
            .Single();
        var witMethod = valid.Reader.MethodDefinitions.Single(handle =>
            valid.Reader.GetString(valid.Reader.GetMethodDefinition(handle).Name) == "WitImport");
        var witAttribute = valid.Reader.GetCustomAttributes(witMethod)
            .Select(valid.Reader.GetCustomAttribute)
            .Single();
        var image = File.ReadAllBytes(validPath);

        var importBlob = valid.Reader.GetBlobBytes(importAttribute.Value);
        var importOffset = FindBlob(image, importBlob);
        image[importOffset] = 0;
        var invalidImportProlog = Path.Combine(assets.Directory, "MalformedJSImportProlog.dll");
        File.WriteAllBytes(invalidImportProlog, image);
        Assert.Throws<CompilerException>(() => ManagedAssemblyTestFactory.Load(invalidImportProlog));

        image = File.ReadAllBytes(validPath);
        importOffset = FindBlob(image, importBlob);
        image[importOffset + importBlob.Length - sizeof(ushort)] = 1;
        var invalidImportArguments = Path.Combine(assets.Directory, "MalformedJSImportArguments.dll");
        File.WriteAllBytes(invalidImportArguments, image);
        Assert.Throws<CompilerException>(() => ManagedAssemblyTestFactory.Load(invalidImportArguments));

        var exportBlob = valid.Reader.GetBlobBytes(exportAttribute.Value);
        image = File.ReadAllBytes(validPath);
        var exportOffset = FindBlob(image, exportBlob);
        image[exportOffset] = 0;
        var invalidExportProlog = Path.Combine(assets.Directory, "MalformedJSExportProlog.dll");
        File.WriteAllBytes(invalidExportProlog, image);
        Assert.Throws<CompilerException>(() => ManagedAssemblyTestFactory.Load(invalidExportProlog));

        image = File.ReadAllBytes(validPath);
        exportOffset = FindBlob(image, exportBlob);
        image[exportOffset + exportBlob.Length - sizeof(ushort)] = 1;
        var invalidExportArguments = Path.Combine(assets.Directory, "MalformedJSExportArguments.dll");
        File.WriteAllBytes(invalidExportArguments, image);
        Assert.Throws<CompilerException>(() => ManagedAssemblyTestFactory.Load(invalidExportArguments));

        var witBlob = valid.Reader.GetBlobBytes(witAttribute.Value);
        image = File.ReadAllBytes(validPath);
        var witOffset = FindBlob(image, witBlob);
        image[witOffset] = 0;
        var invalidWitProlog = Path.Combine(assets.Directory, "MalformedWitProlog.dll");
        File.WriteAllBytes(invalidWitProlog, image);
        Assert.Throws<CompilerException>(() => ManagedAssemblyTestFactory.Load(invalidWitProlog));

        image = File.ReadAllBytes(validPath);
        witOffset = FindBlob(image, witBlob);
        var functionBytes = Encoding.UTF8.GetBytes("import");
        var functionOffset = FindBytes(image, functionBytes, witOffset, witBlob.Length);
        image[functionOffset - 1] = 0;
        var invalidWitNames = Path.Combine(assets.Directory, "MalformedWitNames.dll");
        File.WriteAllBytes(invalidWitNames, image);
        Assert.Throws<CompilerException>(() => ManagedAssemblyTestFactory.Load(invalidWitNames));
    }

    [Theory]
    [InlineData("System.Runtime.InteropServices.JavaScript.JSImport(\"one\")", "System.Runtime.InteropServices.JavaScript.JSImport(\"two\")")]
    [InlineData("System.Runtime.InteropServices.JavaScript.JSExport(\"one\")", "System.Runtime.InteropServices.JavaScript.JSExport(\"two\")")]
    [InlineData("System.Runtime.InteropServices.WebAssembly.WitImport(\"\", \"one\")", "System.Runtime.InteropServices.WebAssembly.WitImport(\"\", \"two\")")]
    [InlineData("System.Runtime.InteropServices.WebAssembly.WitExport(\"api\", \"one\")", "System.Runtime.InteropServices.WebAssembly.WitExport(\"api\", \"two\")")]
    [InlineData("System.Runtime.InteropServices.WebAssembly.WitPostReturn(\"\", \"one\")", "System.Runtime.InteropServices.WebAssembly.WitPostReturn(\"\", \"two\")")]
    public void ManagedAssemblyRejectsDuplicateInteropDeclarations(
        string first,
        string second)
    {
        using var assets = TestAssets.Create();
        var path = assets.CompileSource(
            "DuplicateInterop",
            """
            using System;

            namespace System.Runtime.InteropServices.JavaScript
            {
                [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                public sealed class JSImportAttribute : Attribute
                {
                    public JSImportAttribute(string first) { }
                }
                [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                public sealed class JSExportAttribute : Attribute
                {
                    public JSExportAttribute(string first) { }
                }
            }

            namespace System.Runtime.InteropServices.WebAssembly
            {
                [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                public sealed class WitImportAttribute : Attribute
                {
                    public WitImportAttribute(string first, string second) { }
                }
                [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                public sealed class WitExportAttribute : Attribute
                {
                    public WitExportAttribute(string first, string second) { }
                }
                [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                public sealed class WitPostReturnAttribute : Attribute
                {
                    public WitPostReturnAttribute(string first, string second) { }
                }
            }

            public static class EntryPoint
            {
                [__FIRST__] [__SECOND__]
                public static void Run() { }
            }
            """
                .Replace("__FIRST__", first, StringComparison.Ordinal)
                .Replace("__SECOND__", second, StringComparison.Ordinal));

        var exception = Assert.Throws<CompilerException>(() => ManagedAssemblyTestFactory.Load(path));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
        Assert.Contains("multiple", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManagedAssemblyRejectsMethodsWithMultipleInteropBoundaries()
    {
        using var assets = TestAssets.Create();
        var assemblyPath = assets.CompileSource(
            "InvalidInteropDeclarations",
            """
            public static class InvalidInteropDeclarations
            {
                [System.Runtime.InteropServices.JavaScript.JSImport("sum")]
                [System.Runtime.InteropServices.JavaScript.JSExport("published")]
                public static extern int Both(int value);
            }
            """);

        var exception = Assert.Throws<CompilerException>(() =>
            ManagedAssemblyTestFactory.Load(assemblyPath));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
        Assert.Contains("more than one host/component interop boundary", exception.Message);
    }

    [Fact]
    public void ManagedAssemblyRecognizesCoreLibValueTypesWhoseBaseIsDefinedLocally()
    {
        using var assets = TestAssets.Create();
        using var coreLib = ManagedAssemblyTestFactory.Load(assets.CoreLib);

        Assert.True(coreLib.Types.Values.Single(type => type.FullName == "System.Byte").IsValueType);
        Assert.True(coreLib.Types.Values.Single(type => type.FullName == "System.Int32").IsValueType);
        Assert.True(coreLib.Types.Values.Single(type => type.FullName == "System.Char").IsValueType);
    }

    [Fact]
    public void ManagedAssemblyReadsFieldRvaInitializers()
    {
        using var assets = TestAssets.Create();
        using var assembly = ManagedAssemblyTestFactory.Load(assets.CoreLib);

        Assert.Contains(assembly.Fields.Values, field => !field.InitialData.IsDefaultOrEmpty);
        Assert.Contains(
            assembly.Fields.Values,
            field => !field.InitialData.IsDefaultOrEmpty &&
                field.SignatureType.FullName is string fullName &&
                assembly.Types.Values.Any(type =>
                type.FullName == fullName && type.DeclaredSize > 0));
    }

    [Fact]
    public void ManagedAssemblyKeepsItsPortableExecutableResourceUnavailableAfterDispose()
    {
        using var assets = TestAssets.Create();
        var assembly = ManagedAssemblyTestFactory.Load(assets.Application);
        assembly.Dispose();

        Assert.Throws<ObjectDisposedException>(() => assembly.PortableExecutableReader);
        assembly.Dispose();
    }

    private static MetadataSymbolFormatter Symbols(ManagedAssembly assembly) =>
        new MetadataSymbolFormatter(
            new MetadataTypeRepository(
                assembly.Types.Values.ToImmutableDictionary(type => type.Key),
                MetadataActorTestData.CreateAvailabilityValidator()));

    private static int FindBlob(byte[] image, byte[] blob) =>
        FindBytes(image, blob, 0, image.Length);

    private static int FindBytes(byte[] image, byte[] needle, int start, int length)
    {
        var end = Math.Min(image.Length - needle.Length, start + length);
        for (var offset = start; offset <= end; offset++)
        {
            if (image.AsSpan(offset, needle.Length).SequenceEqual(needle))
            {
                return offset;
            }
        }
        throw new InvalidOperationException("metadata blob was not found in the image");
    }

}
