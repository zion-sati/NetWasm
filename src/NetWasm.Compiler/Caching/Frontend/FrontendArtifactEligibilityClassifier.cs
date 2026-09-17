using NetWasm.Compiler.Analysis;
using System;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;

namespace NetWasm.Compiler.Caching.Frontend;

// Chain-of-Responsibility policy: each visited value can conclusively reject the
// artifact; otherwise traversal continues until the complete admitted graph passes.
internal sealed class FrontendArtifactEligibilityClassifier :
    IFrontendArtifactEligibilityClassifier
{
    public FrontendArtifactEligibility Classify(FrontendArtifactEligibilityRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Analysis);
        ArgumentNullException.ThrowIfNull(request.StructuredMethod);

        var entry = request.EntryAssembly;
        var analysis = request.Analysis;
        var instructions = analysis.Instructions;
        var referencesEntry =
            References(analysis.Method, entry) ||
            References(analysis.Body.Body, entry) ||
            Any(analysis.CatchTypes, key => References(key, entry)) ||
            Any(instructions.RuntimeTypes, type => References(type, entry)) ||
            Any(instructions.ConstructedTypes, type => References(type, entry)) ||
            Any(instructions.AllocatedTypes, type => References(type, entry)) ||
            Any(instructions.Types, key => References(key, entry)) ||
            Any(instructions.Methods, item => References(item.Method, entry)) ||
            Any(instructions.Entities, item => References(item.Entity, entry)) ||
            Any(instructions.Fields, field => References(field, entry)) ||
            Any(instructions.Dispatches, item => References(item.Declaration, entry)) ||
            Any(instructions.CallableMethods, method => References(method, entry)) ||
            Any(instructions.CallSites, site => References(site, entry)) ||
            References(request.StructuredMethod, entry);

        return referencesEntry
            ? FrontendArtifactEligibility.ReferencesEntryAssembly
            : FrontendArtifactEligibility.Eligible;
    }

    private static bool References(StructuredMethod method, AssemblyIdentity entry) =>
        References(method.Header.Method, entry) ||
        References(method.Header.MethodInstance!, entry) ||
        method.Header.LocalSignatureTypes.Any(type => References(type, entry)) ||
        method.Header.Instructions.Any(instruction => References(instruction, entry)) ||
        method.Blocks.Values.Any(block =>
            block.Instructions.Any(instruction => References(instruction, entry)) ||
            block.Exit is StructuredTerminalExit terminal && References(terminal.Instruction, entry)) ||
        method.ExceptionGroups.Values.Any(group => group.Clauses.Any(clause =>
            clause.CatchType is EntityKey catchType && References(catchType, entry)));

    private static bool References(CilMethodBody body, AssemblyIdentity entry) =>
        References(body.Method, entry) ||
        References(body.MethodInstance!, entry) ||
        body.LocalSignatureTypes.Any(type => References(type, entry)) ||
        body.Instructions.Any(instruction => References(instruction, entry)) ||
        body.ExceptionRegions.Any(region =>
            region.CatchType is EntityKey catchType && References(catchType, entry));

    private static bool References(CilInstruction instruction, AssemblyIdentity entry) =>
        instruction.Operand switch
        {
            CilOperand.Entity entity => References(entity.Key, entry),
            CilOperand.MethodInstance method => References(method.Value, entry),
            CilOperand.FieldInstance field => References(field.Value, entry),
            CilOperand.TypeIdentity type => References(type.Value, entry),
            CilOperand.CallSite callSite => References(callSite.Signature, entry),
            _ => false,
        };

    private static bool References(DispatchDeclaration declaration, AssemblyIdentity entry) =>
        References(declaration.Declaration, entry);

    private static bool References(ManagedCallSite site, AssemblyIdentity entry) =>
        References(site.Target, entry) ||
        site.ConstrainedType is not null && References(site.ConstrainedType, entry);

    private static bool References(FieldInstanceModel field, AssemblyIdentity entry) =>
        References(field.Definition.Key, entry) ||
        References(field.Definition.DeclaringType, entry) ||
        References(field.Definition.SignatureType, entry) ||
        References(field.DeclaringType, entry) ||
        References(field.FieldType, entry);

    private static bool References(MethodInstanceModel method, AssemblyIdentity entry) =>
        References(method.Definition, entry) ||
        References(method.DeclaringType, entry) ||
        method.MethodArguments.Any(type => References(type, entry)) ||
        References(method.Signature, entry);

    private static bool References(MethodDefinitionModel method, AssemblyIdentity entry) =>
        References(method.Key, entry) ||
        References(method.DeclaringType, entry) ||
        References(method.Signature, entry);

    private static bool References(MethodSignatureModel signature, AssemblyIdentity entry) =>
        References(signature.ReturnSignatureType, entry) ||
        signature.ParameterSignatureTypes.Any(type => References(type, entry));

    private static bool References(CliTypeIdentity type, AssemblyIdentity entry) =>
        type.Assembly == entry ||
        type.ElementType is not null && References(type.ElementType, entry) ||
        type.TypeArguments.Any(argument => References(argument, entry)) ||
        type.StackStorageType is not null && References(type.StackStorageType, entry);

    private static bool References(EntityKey key, AssemblyIdentity entry) =>
        key.Assembly == entry;

    private static bool Any<T>(
        System.Collections.Immutable.ImmutableArray<T> values,
        Func<T, bool> predicate) => !values.IsDefaultOrEmpty && values.Any(predicate);
}
