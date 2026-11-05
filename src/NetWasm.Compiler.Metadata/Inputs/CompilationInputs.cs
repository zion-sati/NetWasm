using System;
using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text;

namespace NetWasm.Compiler.Metadata.Inputs;

/// <summary>A 128-bit content fingerprint (XXH3-128); not a security boundary.</summary>
public readonly record struct InputFingerprint(ulong First, ulong Second);

/// <summary>
/// What an assembly's compile output is built from, in detail: its shape (every metadata
/// table but method bodies) and each method body by definition token. Two versions with
/// the same shape differ only in the bodies whose fingerprints differ.
/// </summary>
public sealed record AssemblyInputDetail(
    InputFingerprint Shape,
    FrozenDictionary<int, InputFingerprint> Bodies);

/// <summary>An input assembly: its bytes' fingerprint, and its detail once it has been read.</summary>
public sealed record AssemblyInputs(string Name, InputFingerprint Content, AssemblyInputDetail? Detail)
{
    // What its users depend on: its detail's shape, or, read without detail, all its content.
    public InputFingerprint Shape => Detail?.Shape ?? Content;
}

/// <summary>What one analysed method added to the whole program, and its definition.</summary>
public readonly record struct MethodContributionRecord(string Assembly, int Token, InputFingerprint Fingerprint);

/// <summary>The input assemblies of one compile, and what each analysed method contributed.</summary>
public sealed record CompilationInputs(ImmutableArray<AssemblyInputs> Assemblies)
{
    public static CompilationInputs Empty { get; } = new([]);

    // By method canonical name; recorded after analysis.
    public FrozenDictionary<string, MethodContributionRecord> Contributions { get; init; } =
        FrozenDictionary<string, MethodContributionRecord>.Empty;

    public AssemblyInputs? Find(string name)
    {
        foreach (var assembly in Assemblies)
        {
            if (string.Equals(assembly.Name, name, StringComparison.Ordinal)) return assembly;
        }
        return null;
    }
}

public enum AssemblyInputChange
{
    Unchanged,
    // Only method bodies changed; the changed bodies are listed.
    BodiesChanged,
    // Types, members, signatures, references, attributes or data changed.
    ShapeChanged,
    // Added, or changed without a recorded detail to compare with.
    Unknown,
}

public sealed record AssemblyInputDelta(string Name, AssemblyInputChange Change, ImmutableArray<int> ChangedBodies);

/// <summary>How a compile's inputs differ from the last compile's.</summary>
public sealed record CompilationInputChanges(ImmutableArray<AssemblyInputDelta> Assemblies, int Removed)
{
    // Whether every input is the same, or differs only in method bodies.
    public bool IsBodiesOnly
    {
        get
        {
            if (Removed != 0) return false;
            foreach (var assembly in Assemblies)
            {
                if (assembly.Change is AssemblyInputChange.ShapeChanged or AssemblyInputChange.Unknown) return false;
            }
            return true;
        }
    }

    public static CompilationInputChanges Between(CompilationInputs previous, CompilationInputs current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        var deltas = ImmutableArray.CreateBuilder<AssemblyInputDelta>();
        foreach (var assembly in current.Assemblies)
        {
            var before = previous.Find(assembly.Name);
            if (before is not null && before.Content == assembly.Content)
            {
                deltas.Add(new(assembly.Name, AssemblyInputChange.Unchanged, []));
                continue;
            }
            if (before?.Detail is not { } old || assembly.Detail is not { } now)
            {
                deltas.Add(new(assembly.Name, AssemblyInputChange.Unknown, []));
                continue;
            }
            if (old.Shape != now.Shape || old.Bodies.Count != now.Bodies.Count)
            {
                deltas.Add(new(assembly.Name, AssemblyInputChange.ShapeChanged, []));
                continue;
            }
            var changed = ImmutableArray.CreateBuilder<int>();
            foreach (var (token, body) in now.Bodies)
            {
                if (!old.Bodies.TryGetValue(token, out var oldBody))
                {
                    deltas.Add(new(assembly.Name, AssemblyInputChange.ShapeChanged, []));
                    changed = null;
                    break;
                }
                if (oldBody != body) changed.Add(token);
            }
            if (changed is null) continue;
            changed.Sort();
            deltas.Add(new(assembly.Name, AssemblyInputChange.BodiesChanged, changed.ToImmutable()));
        }
        var removed = 0;
        foreach (var assembly in previous.Assemblies)
        {
            if (current.Find(assembly.Name) is null) removed++;
        }
        return new(deltas.ToImmutable(), removed);
    }

    // A plain account: one line per assembly that is not unchanged.
    public string Describe()
    {
        var text = new StringBuilder();
        var unchanged = 0;
        foreach (var assembly in Assemblies)
        {
            if (assembly.Change == AssemblyInputChange.Unchanged)
            {
                unchanged++;
                continue;
            }
            text.Append(assembly.Name).Append(": ").Append(assembly.Change);
            if (assembly.Change == AssemblyInputChange.BodiesChanged)
            {
                text.Append(' ').Append(assembly.ChangedBodies.Length).Append(" (");
                for (var index = 0; index < assembly.ChangedBodies.Length; index++)
                {
                    if (index != 0) text.Append(", ");
                    text.Append("0x").Append(assembly.ChangedBodies[index].ToString("x8", System.Globalization.CultureInfo.InvariantCulture));
                }
                text.Append(')');
            }
            text.Append('\n');
        }
        text.Append(unchanged).Append(" unchanged, ").Append(Removed).Append(" removed; ")
            .Append(IsBodiesOnly ? "bodies only" : "shape changed").Append('\n');
        return text.ToString();
    }
}

/// <summary>The binary form of a compile's inputs, kept beside its compiled units.</summary>
public static class CompilationInputsFormat
{
    private const uint Magic = 0x4E49574E; // "NWIN"
    private const int Version = 2;

    public static byte[] Write(CompilationInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(inputs.Assemblies.Length);
            foreach (var assembly in inputs.Assemblies)
            {
                writer.Write(assembly.Name);
                Write(writer, assembly.Content);
                writer.Write(assembly.Detail is not null);
                if (assembly.Detail is not { } detail) continue;
                Write(writer, detail.Shape);
                writer.Write(detail.Bodies.Count);
                foreach (var (token, body) in detail.Bodies)
                {
                    writer.Write(token);
                    Write(writer, body);
                }
            }
            writer.Write(inputs.Contributions.Count);
            foreach (var (name, contribution) in inputs.Contributions)
            {
                writer.Write(name);
                writer.Write(contribution.Assembly);
                writer.Write(contribution.Token);
                Write(writer, contribution.Fingerprint);
            }
        }
        return stream.ToArray();
    }

    // The inputs written, or none for bytes of another format.
    public static CompilationInputs Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic ||
            BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]) != Version)
        {
            return CompilationInputs.Empty;
        }
        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            reader.ReadUInt32();
            reader.ReadInt32();
            var count = reader.ReadInt32();
            if (count < 0) return CompilationInputs.Empty;
            var assemblies = ImmutableArray.CreateBuilder<AssemblyInputs>(count);
            for (var index = 0; index < count; index++)
            {
                var name = reader.ReadString();
                var content = ReadFingerprint(reader);
                AssemblyInputDetail? detail = null;
                if (reader.ReadBoolean())
                {
                    var shape = ReadFingerprint(reader);
                    var bodyCount = reader.ReadInt32();
                    if (bodyCount < 0) return CompilationInputs.Empty;
                    var bodies = new Dictionary<int, InputFingerprint>(bodyCount);
                    for (var body = 0; body < bodyCount; body++)
                    {
                        var token = reader.ReadInt32();
                        bodies[token] = ReadFingerprint(reader);
                    }
                    detail = new(shape, bodies.ToFrozenDictionary());
                }
                assemblies.Add(new(name, content, detail));
            }
            var contributionCount = reader.ReadInt32();
            if (contributionCount < 0) return CompilationInputs.Empty;
            var contributions = new Dictionary<string, MethodContributionRecord>(contributionCount, StringComparer.Ordinal);
            for (var index = 0; index < contributionCount; index++)
            {
                var name = reader.ReadString();
                contributions[name] = new(reader.ReadString(), reader.ReadInt32(), ReadFingerprint(reader));
            }
            return new(assemblies.MoveToImmutable())
            {
                Contributions = contributions.ToFrozenDictionary(StringComparer.Ordinal),
            };
        }
        catch (EndOfStreamException)
        {
            return CompilationInputs.Empty;
        }
    }

    private static void Write(BinaryWriter writer, InputFingerprint fingerprint)
    {
        writer.Write(fingerprint.First);
        writer.Write(fingerprint.Second);
    }

    private static InputFingerprint ReadFingerprint(BinaryReader reader) => new(reader.ReadUInt64(), reader.ReadUInt64());
}
