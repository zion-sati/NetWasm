using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class CallEmissionKindResolverTests
{
    [Fact]
    public void ResolvesDelegateInvokeBeforeOtherCallKinds()
    {
        var program = new FakeProgram();
        var delegateType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Callback",
            isValueType: false);
        var definition = program.GetMethod(EntryKey) with
        {
            Name = "Invoke",
            IsStatic = false,
        };
        var request = CreateRequest(definition, delegateType);

        Assert.Equal(
            CallEmissionKind.DelegateInvoke,
            new CallEmissionKindResolver(
                new DelegateClassifier(),
                new FakeIntrinsics()).Resolve(request, GetCodeWriter(request.Instruction)));
    }

    [Fact]
    public void ResolvesPlannedVirtualDispatch()
    {
        var program = new FakeProgram();
        var method = program.GetMethod(EntryKey);
        var declaringType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Type",
            isValueType: false);
        var instance = new MethodInstanceModel(
            method,
            declaringType,
            [],
            method.Signature);
        var caller = instance with
        {
            Definition = method with { Name = "Caller" },
        };
        var instruction = CreateInstructionRequest(CilOperation.CallVirtual);
        instruction = instruction with
        {
            Header = instruction.Header with { MethodInstance = caller },
        };
        var site = new DispatchCallSiteModel(
            caller.CanonicalName,
            instruction.Instruction.Offset,
            instance,
            [new DispatchTargetModel(declaringType, instance)]);
        instruction = instruction with
        {
            Target = instruction.Target with
            {
                DispatchCallSites = ImmutableDictionary<string, DispatchCallSiteModel>.Empty.Add(
                    site.Key,
                    site),
            },
        };

        var request = new CallEmissionRequest(instruction, instance, 0, 1);
        var code = new RecordingInstructionWriter();

        Assert.Equal(
            CallEmissionKind.VirtualDispatch,
            new CallEmissionKindResolver(program, new FakeIntrinsics()).Resolve(
                request,
                code));
    }

    [Fact]
    public void ResolvesRegisteredIntrinsicBeforeAConservativeDispatchSite()
    {
        var program = new FakeProgram();
        var method = program.GetMethod(StringLengthKey);
        var declaringType = CliTypeIdentity.Named(
            Assembly,
            "System",
            "String",
            isValueType: false);
        var instance = new MethodInstanceModel(
            method,
            declaringType,
            [],
            method.Signature);
        var caller = instance with
        {
            Definition = method with { Name = "Caller" },
        };
        var instruction = CreateInstructionRequest(CilOperation.CallVirtual);
        instruction = instruction with
        {
            Header = instruction.Header with { MethodInstance = caller },
        };
        var site = new DispatchCallSiteModel(
            caller.CanonicalName,
            instruction.Instruction.Offset,
            instance,
            [new DispatchTargetModel(declaringType, instance)]);
        instruction = instruction with
        {
            Target = instruction.Target with
            {
                DispatchCallSites = ImmutableDictionary<string, DispatchCallSiteModel>.Empty.Add(
                    site.Key,
                    site),
            },
        };
        var request = new CallEmissionRequest(instruction, instance, 0, 1);

        Assert.Equal(
            CallEmissionKind.RuntimeIntrinsic,
            new CallEmissionKindResolver(program, new FakeIntrinsics()).Resolve(
                request,
                new RecordingInstructionWriter()));
    }

    [Fact]
    public void ResolvesRuntimeIntrinsicAndImportKinds()
    {
        var program = new FakeProgram();
        var resolver = new CallEmissionKindResolver(program, new FakeIntrinsics());
        var intrinsic = CreateRequest(
            program.GetMethod(StringLengthKey),
            CliTypeIdentity.Named(Assembly, "System", "String", isValueType: false));
        var importedDefinition = program.GetMethod(EntryKey) with
        {
            JSImport = new("run", "tests"),
        };
        var imported = CreateRequest(
            importedDefinition,
            CliTypeIdentity.Named(Assembly, "Test", "Type", isValueType: false));
        var intrinsicCode = new RecordingInstructionWriter();
        var importedCode = new RecordingInstructionWriter();

        Assert.Equal(
            CallEmissionKind.RuntimeIntrinsic,
            resolver.Resolve(intrinsic, intrinsicCode));
        Assert.Equal(
            CallEmissionKind.JavaScriptImport,
            resolver.Resolve(imported, importedCode));
    }

    [Fact]
    public void ResolvesAsyncBindingAndFallsBackToDirectCall()
    {
        var program = new FakeProgram();
        var resolver = new CallEmissionKindResolver(program, new FakeIntrinsics());
        var asyncRequest = CreateRequest(
            program.GetMethod(EntryKey),
            CliTypeIdentity.Named(Assembly, "Test", "Type", isValueType: false));
        asyncRequest = asyncRequest with
        {
            Instruction = asyncRequest.Instruction with
            {
                Target = asyncRequest.Instruction.Target with
                {
                    JavaScriptAsyncBindings = ImmutableDictionary<EntityKey, JavaScriptAsyncMethodBinding>.Empty.Add(
                        EntryKey,
                        null!),
                },
            },
        };
        var directRequest = CreateRequest(
            program.GetMethod(ConstructorKey),
            CliTypeIdentity.Named(Assembly, "Test", "Type", isValueType: false));
        var asyncCode = new RecordingInstructionWriter();
        var directCode = new RecordingInstructionWriter();

        Assert.Equal(
            CallEmissionKind.AsyncJSImport,
            resolver.Resolve(asyncRequest, asyncCode));
        Assert.Equal(
            CallEmissionKind.Direct,
            resolver.Resolve(directRequest, directCode));
    }

    [Fact]
    public void RejectsNullRequests()
    {
        var resolver = new CallEmissionKindResolver(new FakeProgram(), new FakeIntrinsics());

        Assert.Throws<ArgumentNullException>(() =>
            resolver.Resolve(null!, new RecordingInstructionWriter()));
    }

    private static CallEmissionRequest CreateRequest(
        MethodDefinitionModel definition,
        CliTypeIdentity declaringType)
    {
        var instruction = CreateInstructionRequest(CilOperation.Call);
        var method = new MethodInstanceModel(
            definition,
            declaringType,
            [],
            definition.Signature);
        return new CallEmissionRequest(
            instruction,
            method,
            0,
            definition.Signature.ParameterTypes.Length + (definition.IsStatic ? 0 : 1));
    }

    private sealed class DelegateClassifier : ITypeClassifier
    {
        public bool IsDelegateType(EntityKey type) =>
            type == TypeKey;
    }
}
