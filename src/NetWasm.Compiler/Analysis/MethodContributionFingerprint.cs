using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.Inputs;

namespace NetWasm.Compiler.Analysis;

/// <summary>What one method adds to the whole program, and the definition it comes from.</summary>
internal readonly record struct MethodContribution(EntityKey Definition, InputFingerprint Fingerprint);

/// <summary>
/// Fingerprints what a method's analysis adds to the whole program: the types, strings,
/// methods, fields and entities it names and how, its dispatches by declaration, its
/// callable methods and descriptors, its exception needs and catch types, whether it
/// allocates by itself, the shape of its exception regions and its requirement flags. Each list is taken as a sorted set, and
/// nothing keyed by the method's own IL offsets is included, since those reach no other
/// method: an edit that only moves code keeps its contribution.
/// </summary>
internal static class MethodContributionFingerprint
{
    public static InputFingerprint Of(ReachableMethodAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        var hash = new XxHash128();
        var facts = analysis.Instructions;
        var names = new List<string>();
        void Set(int tag)
        {
            names.Sort(StringComparer.Ordinal);
            Int(hash, tag);
            string? last = null;
            foreach (var name in names)
            {
                if (string.Equals(name, last, StringComparison.Ordinal)) continue;
                Text(hash, name);
                last = name;
            }
            Int(hash, -1);
            names.Clear();
        }
        foreach (var type in facts.RuntimeTypes) names.Add(type.CanonicalName);
        Set(1);
        foreach (var type in facts.ConstructedTypes) names.Add(type.CanonicalName);
        Set(2);
        foreach (var type in facts.AllocatedTypes) names.Add(type.CanonicalName);
        Set(3);
        foreach (var type in facts.Types) names.Add(type.ToString());
        Set(4);
        names.AddRange(facts.Strings);
        Set(5);
        foreach (var method in facts.Methods) names.Add($"{(int)method.Operation}:{method.Method.CanonicalName}");
        Set(6);
        foreach (var entity in facts.Entities) names.Add($"{(int)entity.Operation}:{entity.Entity}");
        Set(7);
        foreach (var field in facts.Fields) names.Add(field.CanonicalName);
        Set(8);
        foreach (var dispatch in facts.Dispatches)
        {
            var declaration = dispatch.Declaration;
            names.Add($"{(int)declaration.Operation}:{declaration.Declaration.CanonicalName}:{declaration.ReceiverBound?.CanonicalName}");
        }
        Set(9);
        foreach (var method in facts.CallableMethods) names.Add(method.CanonicalName);
        Set(10);
        foreach (var method in facts.MethodDescriptors) names.Add(method.CanonicalName);
        Set(11);
        foreach (var field in facts.FieldDescriptors) names.Add(field.CanonicalName);
        Set(12);
        foreach (var method in facts.NativeCallbacks) names.Add(method.CanonicalName);
        Set(13);
        foreach (var site in facts.CallSites)
            names.Add($"{(int)site.Operation}:{site.TargetIdentity.CanonicalName}:{site.Target.CanonicalName}:{site.ConstrainedType?.CanonicalName}");
        Set(14);
        foreach (var requirement in analysis.Exceptions) names.Add($"{(int)requirement.Kind}:{requirement.TypeName}");
        Set(15);
        foreach (var type in analysis.CatchTypes) names.Add(type.ToString());
        Set(16);
        Int(hash, (facts.RequiresTypeFacts ? 1 : 0) |
            (facts.RequiresGenericArguments ? 4 : 0) | (facts.RequiresMemberNames ? 8 : 0) |
            (facts.RequiresDeclaringTypes ? 16 : 0) | (facts.RequiresGenericDefinitions ? 32 : 0) |
            (facts.RequiresAssemblies ? 64 : 0) | (facts.RequiresElementTypes ? 128 : 0) |
            (facts.RequiresTypeAttributes ? 256 : 0) | (facts.RequiresInterfaces ? 512 : 0));
        // Whether the body allocates by itself: with the methods it calls, that decides which
        // methods allocate, and so every caller's safepoints.
        var allocates = false;
        foreach (var instruction in analysis.Body.Body.Instructions)
        {
            if (!CilSafepointClassifier.RequiresUnconditionalRootDecision(instruction)) continue;
            allocates = true;
            break;
        }
        Int(hash, allocates ? 1 : 0);
        // Exception metadata is laid out per region, so the regions' shape is a contribution.
        var regions = analysis.Body.Body.ExceptionRegions;
        Int(hash, regions.Length);
        foreach (var region in regions)
        {
            Int(hash, (int)region.Kind);
            Text(hash, region.CatchTypeIdentity?.CanonicalName ?? "");
            Int(hash, region.FilterOffset is null ? 0 : 1);
        }
        var digest = hash.GetCurrentHashAsUInt128();
        return new((ulong)(digest >> 64), (ulong)digest);
    }

    private static void Int(XxHash128 hash, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        hash.Append(bytes);
    }

    private static void Text(XxHash128 hash, string value)
    {
        Int(hash, value.Length);
        hash.Append(MemoryMarshal.AsBytes(value.AsSpan()));
    }
}
