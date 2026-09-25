using System.Text.Json;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class GeneratedCilManifestWriter : IGeneratedCilManifestWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        NewLine = "\n",
    };

    public void Write(
        string path,
        GeneratedCilProgram program,
        PatchedMethodBody? patched = null,
        CorpusArtifact? artifact = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(program);
        var manifest = new
        {
            GeneratorVersion = 1,
            program.Seed,
            CompilerVersion = artifact?.CompilerVersion,
            CompilerOptions = artifact?.CompilerOptions,
            ReturnType = program.ReturnType.ToString(),
            Arguments = program.Arguments.Select(value => value.ToString()),
            Locals = program.Locals.Select(value => value.ToString()),
            Operations = program.Operations.Select(value => value.ToString()),
            ExceptionRegions = program.ExceptionRegions.Select(region => new
            {
                region.Id,
                Kind = region.Kind.ToString(),
                region.TryStartBlock,
                region.TryEndBlock,
                region.HandlerStartBlock,
                region.HandlerEndBlock,
                region.FilterStartBlock,
                region.CatchTypeToken,
            }),
            Blocks = program.Blocks.Select(block => new
            {
                block.Id,
                EntryStack = block.EntryStack.Select(value => value.ToString()),
                block.ExceptionRegionPath,
                Instructions = block.Instructions.Select(instruction => new
                {
                    Operation = instruction.Operation.ToString(),
                    Operand = instruction.Operand.ToString(),
                }),
            }),
            patched?.MethodBodyCapacity,
            patched?.GeneratedCodeSize,
            patched?.AssemblySha256,
        };
        Directory.CreateDirectory(
            Path.GetDirectoryName(path) ?? throw new InvalidDataException(
                "generated CIL manifest path has no parent directory"));
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(manifest, SerializerOptions) + "\n");
    }
}
