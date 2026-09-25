using System.Collections.Immutable;
using System.Security.Cryptography;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class RoslynCorpusCompiler(
    CompilerCorrectnessEnvironment environment,
    IQualifiedProcessRunner processes,
    IOracleModePolicyRegistry oracleModes,
    ICorpusSourceNamesVerifier sourceNames,
    ICorpusSourceArtifactWriter sources) :
    IRoslynCorpusCompiler
{
    public CorpusCompilation Compile(
        CorpusFixture fixture,
        CilProfile profile,
        string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentException.ThrowIfNullOrEmpty(outputDirectory);
        if (profile is not (CilProfile.Debug or CilProfile.Release))
        {
            throw new ArgumentOutOfRangeException(nameof(profile), "Roslyn compilation requires Debug or Release CIL.");
        }
        ArgumentNullException.ThrowIfNull(fixture.Source);
        if (fixture.AdditionalSources.IsDefault)
        {
            throw new ArgumentException("Additional sources must be an initialized collection.", nameof(fixture));
        }
        foreach (var source in fixture.AdditionalSources)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(source.Content);
        }
        ImmutableArray<CorpusSourceFile> files = [new(fixture.Name + ".cs", fixture.Source), .. fixture.AdditionalSources];
        sourceNames.Verify([.. files.Select(source => source.Name)]);
        var oracleMode = oracleModes.Get(fixture.OracleMode);
        oracleMode.Validate(fixture);
        var sourceArtifacts = ImmutableArray.CreateBuilder<CorpusSourceArtifact>();
        foreach (var source in files)
        {
            sourceArtifacts.Add(sources.Write(source, outputDirectory));
        }
        var sourcePaths = sourceArtifacts.Select(source => source.Path).ToImmutableArray();
        var desktop = CompileDesktop(fixture, profile, sourcePaths, outputDirectory);
        var netWasm = oracleMode.SharesPortableExecutable
            ? desktop
            : CompileNetWasm(fixture, profile, sourcePaths, outputDirectory);
        var compilation = new CorpusCompilation(
            fixture,
            profile,
            desktop,
            netWasm,
            outputDirectory)
        {
            Sources = sourceArtifacts.ToImmutable(),
        };
        oracleMode.ValidateCompilation(compilation);
        return compilation;
    }

    private CorpusArtifact CompileDesktop(
        CorpusFixture fixture,
        CilProfile profile,
        ImmutableArray<string> sourcePaths,
        string outputDirectory)
    {
        var references = Directory.EnumerateFiles(
                environment.DesktopReferenceDirectory, "*.dll")
            .Order(StringComparer.Ordinal)
            .Select(path => "-reference:" + path)
            .ToImmutableArray();
        return Compile(
            fixture,
            profile,
            sourcePaths,
            outputDirectory,
            fixture.Name + ".Desktop",
            references);
    }

    private CorpusArtifact CompileNetWasm(
        CorpusFixture fixture,
        CilProfile profile,
        ImmutableArray<string> sourcePaths,
        string outputDirectory)
    {
        var references = ImmutableArray.CreateBuilder<string>(
            fixture.NetWasmReferencePaths.Length + 1);
        references.Add("-reference:" + environment.CoreLibPath);
        foreach (var referencePath in fixture.NetWasmReferencePaths)
        {
            references.Add("-reference:" + referencePath);
        }

        return Compile(
            fixture,
            profile,
            sourcePaths,
            outputDirectory,
            fixture.Name + ".NetWasm",
            references.ToImmutable());
    }

    private CorpusArtifact Compile(
        CorpusFixture fixture,
        CilProfile profile,
        ImmutableArray<string> sourcePaths,
        string outputDirectory,
        string assemblyName,
        ImmutableArray<string> references)
    {
        var assemblyPath = Path.Combine(outputDirectory, assemblyName + ".dll");
        var pdbPath = Path.Combine(outputDirectory, assemblyName + ".pdb");
        var options = ImmutableArray.Create(
            "-nologo",
            "-noconfig",
            "-nostdlib",
            "-langversion:latest",
            "-nullable:enable",
            "-warnaserror+",
            "-deterministic+",
            profile == CilProfile.Release ? "-optimize+" : "-optimize-",
            "-debug:portable",
            "-target:library",
            "-runtimemetadataversion:v4.0.30319",
            "-pathmap:" + outputDirectory + "=/corpus",
            "-out:" + assemblyPath,
            "-pdb:" + pdbPath);
        var arguments = ImmutableArray.CreateBuilder<string>();
        arguments.Add(environment.RoslynCompilerPath);
        foreach (var option in options)
        {
            arguments.Add(option);
        }
        if (fixture.AllowUnsafe)
        {
            arguments.Add("-unsafe+");
        }
        foreach (var reference in references)
        {
            arguments.Add(reference);
        }
        arguments.AddRange(sourcePaths);
        var process = processes.Run(new(
            environment.DotNetPath,
            arguments.ToImmutable(),
            environment.ProcessTimeout));
        if (!process.Succeeded)
        {
            throw new InvalidOperationException(
                $"Roslyn failed for {fixture.Name} {profile}: " +
                $"completion={process.Completion}, exit={process.ExitCode}, " +
                process.StandardOutput + process.StandardError,
                process.LaunchException);
        }
        return new(
            assemblyPath,
            pdbPath,
            Hash(assemblyPath),
            Hash(pdbPath),
            environment.SdkVersion,
            fixture.AllowUnsafe ? options.Add("-unsafe+") : options);
    }

    private static string Hash(string path) => Convert.ToHexString(
        SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
