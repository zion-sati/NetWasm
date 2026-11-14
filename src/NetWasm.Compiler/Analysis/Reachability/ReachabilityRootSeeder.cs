using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Analysis.ManagedCallSites;
using NetWasm.Compiler.Analysis.Delegates;

namespace NetWasm.Compiler.Analysis.Reachability;

internal sealed class ReachabilityRootSeeder(
    IMethodRepository methodRepository,
    ITypeDefinitionResolver typeDefinitions,
    ITypeIdentityResolver typeIdentities,
    ITypeFinder typeFinder) : IReachabilityRootSeeder
{
    private readonly IMethodRepository _methodRepository = methodRepository ??
        throw new ArgumentNullException(nameof(methodRepository));
    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly ITypeIdentityResolver _typeIdentities = typeIdentities ??
        throw new ArgumentNullException(nameof(typeIdentities));
    private readonly ITypeFinder _typeFinder = typeFinder ??
        throw new ArgumentNullException(nameof(typeFinder));

    public void Seed(ReachabilitySession session, MethodDefinitionModel? entryPoint, ReachabilityRoots roots,
        IReadOnlyList<ProgramExport> exports)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(exports);
        if (entryPoint is not null)
        {
            session.RejectOpenRoot(entryPoint, "entry point");
        }
        foreach (var type in roots.Types)
        {
            session.State.Types.Add(type);
            session.State.TypeDependencyPaths.TryAdd(_typeIdentities.GetTypeIdentity(type).CanonicalName,
                ImmutableStack<CompilerDependencyStep>.Empty);
        }
        foreach (var field in roots.Fields)
        {
            session.State.Fields.Add(field);
        }
        foreach (var method in roots.Methods)
        {
            var rootedMethod = _methodRepository.GetMethod(method);
            session.RejectOpenRoot(rootedMethod, "method root");
            session.Enqueue(rootedMethod);
        }
        foreach (var method in roots.InvokedMethods)
        {
            session.VisitInvokedRoot(_methodRepository.GetMethod(method), "invoked method root");
        }
        if (entryPoint is not null)
        {
            session.Enqueue(entryPoint);
        }
        foreach (var export in exports)
        {
            var exportedMethod = _methodRepository.GetMethod(export.Method);
            session.RejectOpenRoot(exportedMethod, "export");
            var javaScriptBoundaryParameters = exportedMethod.Signature
                .ParameterSignatureTypes
                .Where(type =>
                    type.CanonicalName == "primitive:string" ||
                    JavaScriptValueArray.IsSupported(type) ||
                    IsJSObject(type))
                .ToArray();
            if (exportedMethod.JSExport is not null &&
                IsJSObject(exportedMethod.Signature.ReturnSignatureType))
            {
                // The export boundary reads the result's host handle field.
                session.State.Types.Add(_typeDefinitions.ResolveTypeIdentity(exportedMethod.Signature.ReturnSignatureType).Key);
            }
            if (exportedMethod.JSExport is not null &&
                javaScriptBoundaryParameters.Length != 0)
            {
                session.AddImplicit(ManagedExceptionKind.JSException, "System.JSException");
                session.AddImplicit(
                    ManagedExceptionKind.OutOfMemory,
                    "System.OutOfMemoryException");
                foreach (var hostObject in javaScriptBoundaryParameters.Where(IsJSObject).Distinct())
                {
                    // The export boundary allocates these wrappers without a CIL newobj.
                    session.State.Types.Add(_typeDefinitions.ResolveTypeIdentity(hostObject).Key);
                    session.AddAllocatedType(hostObject);
                }
                foreach (var valueArray in javaScriptBoundaryParameters.Where(JavaScriptValueArray.IsSupported))
                {
                    session.AddConstructedType(valueArray);
                    session.AddAllocatedType(valueArray);
                    session.State.Types.Add(_typeFinder.FindType("System.Array").Key);
                    session.State.Types.Add(_typeFinder.FindType(JavaScriptValueArray.GetElementTypeName(valueArray)).Key);
                }
            }
            session.Enqueue(exportedMethod);
        }
    }

    private static bool IsJSObject(CliTypeIdentity type) =>
        type.FullName == "System.Runtime.InteropServices.JavaScript.JSObject";
}
