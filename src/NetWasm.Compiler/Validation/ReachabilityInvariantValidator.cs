using System.Collections.Generic;
using System;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Validation.Delegates;

namespace NetWasm.Compiler.Validation;

internal sealed class ReachabilityInvariantValidator(
    ICompilerInvariantExceptionFactory exceptions,
    IDelegateBindingInvariantValidator delegateBindings) :
    IReachabilityInvariantValidator
{
    private readonly IDelegateBindingInvariantValidator _delegateBindings =
        delegateBindings ?? throw new ArgumentNullException(nameof(delegateBindings));

    public void Validate(
        IMethodInstanceResolver methodInstances,
        ITypeClassifier types,
        ReachableProgram program,
        IRuntimeIntrinsicRegistry intrinsics)
    {
        ArgumentNullException.ThrowIfNull(methodInstances);
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(intrinsics);
        if (program.EntryPoint is { } entryPoint &&
            !program.Methods.ContainsKey(entryPoint.Key))
        {
            throw exceptions.Create(
                "entry point is absent from the reachable method set");
        }
        foreach (var pair in program.Methods)
        {
            if (pair.Key != pair.Value.Method.Definition.Key)
            {
                throw exceptions.Create(
                    "reachable method key does not match its validated body");
            }
        }
        foreach (var export in program.Exports)
        {
            if (!program.Methods.ContainsKey(export.Method))
            {
                throw exceptions.Create(
                    $"export '{export.Name}' targets an unreachable method");
            }
        }
        foreach (var method in program.AllocatingMethods)
        {
            if (!program.Methods.ContainsKey(method))
            {
                throw exceptions.Create(
                    $"allocating method '{method}' is not reachable");
            }
        }
        foreach (var method in program.ConstructedAllocatingMethods)
        {
            if (!program.ConstructedMethods.ContainsKey(method))
            {
                throw exceptions.Create(
                    $"allocating constructed method '{method}' is not reachable");
            }
        }
        var dispatchImports = ImportKeys(program);
        foreach (var pair in program.DispatchCallSites)
        {
            foreach (var target in pair.Value.Targets)
            {
                var reachable = IsResolvable(program, target.Method, intrinsics, dispatchImports);
                if (!reachable)
                {
                    throw exceptions.Create(
                        $"dispatch site '{pair.Key}' targets an unreachable method");
                }
            }
        }
        foreach (var pair in program.ConstructedMethods)
        {
            if (!program.MethodInstances.TryGetValue(pair.Key, out var instance) ||
                instance.CanonicalName != pair.Key)
            {
                throw exceptions.Create(
                    $"constructed method '{pair.Key}' has no canonical method identity");
            }
        }
        ValidateReachableCalls(
            methodInstances,
            types,
            program,
            intrinsics);
        ValidateRuntimeRequirements(types, program);
        _delegateBindings.Validate(types, program);
    }

    private void ValidateReachableCalls(
        IMethodInstanceResolver methodInstances,
        ITypeClassifier types,
        ReachableProgram program,
        IRuntimeIntrinsicRegistry intrinsics)
    {
        var imports = ImportKeys(program);
        var bodies = program.Methods.Values.Concat(program.ConstructedMethods.Values).ToArray();
        // Each method's calls are checked on their own; the first failing method in order is
        // reported, as by the loop over them.
        OrderedParallel.For(bodies.Length, index =>
        {
            var method = bodies[index];
            var body = method.Body;
            var caller = method.Method.CanonicalName;
            foreach (var instruction in body.Instructions)
            {
                if (instruction.Operation == CilOperation.CallIndirect)
                {
                    if (program.CallableMethods.IsEmpty)
                    {
                        throw exceptions.Create(
                            "indirect call has no reachable callable target",
                            caller,
                            instruction.Offset);
                    }
                    continue;
                }
                if (instruction.Operation is not (
                        CilOperation.Call or
                        CilOperation.CallVirtual or
                        CilOperation.NewObject or
                        CilOperation.LoadFunction or
                        CilOperation.LoadVirtualFunction))
                {
                    continue;
                }
                var target = GetMethodInstance(methodInstances, instruction, caller);
                if (instruction.Operation is CilOperation.CallVirtual or
                        CilOperation.LoadVirtualFunction &&
                    program.DispatchCallSites.ContainsKey($"{caller}@{instruction.Offset:x8}"))
                {
                    continue;
                }
                if (instruction.Operation is CilOperation.LoadFunction &&
                    target.Definition.NativeCallback is not null &&
                    !program.NativeCallbacks.ContainsKey(target.CanonicalName))
                {
                    throw exceptions.Create(
                        $"function load omits native callback target '{target.CanonicalName}'",
                        caller,
                        instruction.Offset);
                }
                if (instruction.Operation is CilOperation.LoadFunction &&
                    target.Definition.NativeCallback is null &&
                    !program.CallableMethods.ContainsKey(target.CanonicalName))
                {
                    throw exceptions.Create(
                        $"function load omits callable target '{target.CanonicalName}'",
                        caller,
                        instruction.Offset);
                }
                if (!IsResolvable(program, target, intrinsics, imports) &&
                    !IsDelegateRuntimeMethod(types, target))
                {
                    throw exceptions.Create(
                        $"call target '{target.CanonicalName}' has no lowering target",
                        caller,
                        instruction.Offset);
                }
            }
        });
        foreach (var pair in program.CallableMethods)
        {
            if (pair.Key != pair.Value.CanonicalName ||
                !IsResolvable(program, pair.Value, intrinsics, imports))
            {
                throw exceptions.Create(
                    $"callable method '{pair.Key}' has no canonical reachable target");
            }
        }
    }

    private void ValidateRuntimeRequirements(
        ITypeClassifier types,
        ReachableProgram program)
    {
        foreach (var initializer in program.StaticInitializers)
        {
            if (!program.Methods.ContainsKey(initializer))
            {
                throw exceptions.Create(
                    $"static initializer '{initializer}' is not reachable");
            }
        }
        foreach (var initializer in program.ConstructedStaticInitializers)
        {
            if (!program.ConstructedMethods.ContainsKey(initializer))
            {
                throw exceptions.Create(
                    $"constructed static initializer '{initializer}' is not reachable");
            }
        }
        foreach (var finalizer in program.Finalizers)
        {
            var method = finalizer.Value;
            var allocatedTypeIsReachable = program.ConstructedTypes.Contains(finalizer.Key) ||
                program.MethodInstances.Values.Any(instance =>
                    instance.DeclaringType.Equals(finalizer.Key));
            var methodIsReachable = method.IsConstructed
                ? program.ConstructedMethods.ContainsKey(method.CanonicalName)
                : program.Methods.ContainsKey(method.Definition.Key);
            if (!allocatedTypeIsReachable || !methodIsReachable ||
                !program.MethodInstances.TryGetValue(method.CanonicalName, out var instance) ||
                instance != method)
            {
                throw exceptions.Create(
                    $"finalizer '{method.CanonicalName}' or its allocated type is unreachable");
            }
        }
        foreach (var pair in program.DispatchCallSites)
        {
            if (pair.Key != pair.Value.Key || !HasReachableCaller(program, pair.Value.Caller))
            {
                throw exceptions.Create(
                    $"dispatch site '{pair.Key}' has no canonical reachable caller");
            }
        }
        foreach (var pair in program.TypeTestSites)
        {
            if (pair.Key != pair.Value.Key || !HasReachableCaller(program, pair.Value.Caller))
            {
                throw exceptions.Create(
                    $"type-test site '{pair.Key}' has no canonical reachable caller");
            }
        }
        foreach (var callback in program.HostCallbacks)
        {
            if (!program.JSImportMethods.Any(method => method.Key == callback.ImportMethod) ||
                !IsDelegateRuntimeMethod(types, callback.Invoke))
            {
                throw exceptions.Create(
                    $"host callback '{callback.ExportName}' has an unreachable import or invoke target");
            }
        }
    }

    private MethodInstanceModel GetMethodInstance(
        IMethodInstanceResolver methodInstances,
        CilInstruction instruction,
        string caller) => instruction.Operand switch
        {
            CilOperand.MethodInstance method => method.Value,
            CilOperand.Entity method => methodInstances.ResolveMethodInstance(
                method.Key.Assembly,
                method.Key.MetadataToken,
                caller,
                instruction.Offset),
            _ => throw exceptions.Create(
                "reachable call has no method operand",
                caller,
                instruction.Offset),
        };

    // The JavaScript and WIT import methods, looked up per call target.
    private static HashSet<EntityKey> ImportKeys(ReachableProgram program)
    {
        var keys = new HashSet<EntityKey>();
        foreach (var method in program.JSImportMethods) keys.Add(method.Key);
        foreach (var method in program.WitImportMethods) keys.Add(method.Key);
        return keys;
    }

    // Reachable bodies are checked first: they resolve nearly every call.
    private static bool IsResolvable(
        ReachableProgram program,
        MethodInstanceModel method,
        IRuntimeIntrinsicRegistry intrinsics,
        HashSet<EntityKey> imports) =>
        (method.IsConstructed
            ? program.ConstructedMethods.ContainsKey(method.CanonicalName)
            : program.Methods.ContainsKey(method.Definition.Key)) ||
        intrinsics.TryGetIntrinsic(method.Definition.Key, out _) ||
        method.Definition.NativeImport is not null &&
        program.MethodInstances.TryGetValue(method.CanonicalName, out var native) &&
        native.HasEquivalentDescriptorFacts(method) ||
        imports.Contains(method.Definition.Key);

    private static bool IsDelegateRuntimeMethod(
        ITypeClassifier types,
        MethodInstanceModel method) =>
        method.Definition.Name is ".ctor" or "Invoke" &&
        types.IsDelegateType(method.Definition.DeclaringType);

    private static bool HasReachableCaller(ReachableProgram program, string caller) =>
        program.MethodInstances.ContainsKey(caller) &&
        (program.ConstructedMethods.ContainsKey(caller) ||
         program.Methods.ContainsKey(program.MethodInstances[caller].Definition.Key));
}
