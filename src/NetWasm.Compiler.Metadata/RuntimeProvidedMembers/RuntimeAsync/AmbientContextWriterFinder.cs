// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

internal interface IAmbientContextWriterFinder
{
    // Whether an assembly the program compiles, the core library aside, refers to an API that changes an ambient context.
    bool Finds(MetadataCompilationSnapshot snapshot);
}

// The public APIs that change an ambient context, the only ways user code writes AmbientContextChanges.Made: an
// AsyncLocal<T>'s value, ExecutionContext.SuppressFlow and SynchronizationContext.SetSynchronizationContext directly, and
// the core library's members that set an AsyncLocal of their own (the current culture and UI culture through CultureInfo
// or Thread, Thread.CurrentPrincipal, AssemblyLoadContext.EnterContextualReflection, and Activity.Current through its
// setter, Start, Stop and ActivitySource's StartActivity). A runtime-async method separates its body
// (RuntimeAsyncBodyPlacement) only in a program that refers to none of them: where a context can change,
// RuntimeAsync.ContextsMayChange holds, every direct await goes through the callee's entry, and the separate body would
// only add a call and a second capture of the contexts. A reference that is never reached keeps one body, as before; the
// core library's own uses are not counted, and where one is reached the fact folds to true and costs the entries' path.
internal sealed class AmbientContextWriterFinder : IAmbientContextWriterFinder
{
    private const string CoreLibraryName = "NetWasm.CoreLib";

    // Each writer's member name, and the types that declare it.
    private static readonly FrozenDictionary<string, (string Namespace, string Name)[]> Writers =
        new Dictionary<string, (string Namespace, string Name)[]>(StringComparer.Ordinal)
        {
            ["set_Value"] = [("System.Threading", "AsyncLocal`1")],
            ["SuppressFlow"] = [("System.Threading", "ExecutionContext")],
            ["SetSynchronizationContext"] = [("System.Threading", "SynchronizationContext")],
            ["set_CurrentCulture"] = [("System.Globalization", "CultureInfo"), ("System.Threading", "Thread")],
            ["set_CurrentUICulture"] = [("System.Globalization", "CultureInfo"), ("System.Threading", "Thread")],
            ["set_CurrentPrincipal"] = [("System.Threading", "Thread")],
            ["EnterContextualReflection"] = [("System.Runtime.Loader", "AssemblyLoadContext")],
            ["set_Current"] = [("System.Diagnostics", "Activity")],
            ["Start"] = [("System.Diagnostics", "Activity")],
            ["Stop"] = [("System.Diagnostics", "Activity")],
            ["StartActivity"] = [("System.Diagnostics", "ActivitySource")],
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public bool Finds(MetadataCompilationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Assemblies.Any(assembly => assembly.Identity.Name != CoreLibraryName && RefersToAWriter(assembly.Reader));
    }

    private static bool RefersToAWriter(MetadataReader reader)
    {
        foreach (var handle in reader.MemberReferences)
        {
            var member = reader.GetMemberReference(handle);
            var name = reader.GetString(member.Name);
            if (Writers.TryGetValue(name, out var owners) &&
                owners.Any(owner => Declares(reader, member.Parent, owner.Namespace, owner.Name)))
            {
                return true;
            }
        }

        return false;
    }

    // Whether the member's parent is the named type, or an instantiation of it.
    private static bool Declares(MetadataReader reader, EntityHandle parent, string ownerNamespace, string owner)
    {
        if (parent.Kind == HandleKind.TypeSpecification)
        {
            var blob = reader.GetBlobReader(reader.GetTypeSpecification((TypeSpecificationHandle)parent).Signature);
            if (blob.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance) return false;
            blob.ReadSignatureTypeCode();
            parent = blob.ReadTypeHandle();
        }

        return parent.Kind == HandleKind.TypeReference &&
            reader.GetTypeReference((TypeReferenceHandle)parent) is var type &&
            reader.StringComparer.Equals(type.Namespace, ownerNamespace) &&
            reader.StringComparer.Equals(type.Name, owner);
    }
}
