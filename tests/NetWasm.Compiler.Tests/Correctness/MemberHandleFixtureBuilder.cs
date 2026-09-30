using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class MemberHandleFixtureBuilder : IEmittedAssemblyBuilder
{
    public ImmutableArray<byte> Build()
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("MemberHandles"),
            typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("MemberHandles.dll");

        var baseOwner = module.DefineType("MemberHandles.BaseOwner", TypeAttributes.Public);
        var baseConstructor = DefineDefaultConstructor(baseOwner, typeof(object).GetConstructor(Type.EmptyTypes)!);
        var baseField = baseOwner.DefineField("Value", typeof(int), FieldAttributes.Public);
        var baseMethod = baseOwner.DefineMethod(
            "Read",
            MethodAttributes.Public,
            typeof(int),
            Type.EmptyTypes);
        var code = baseMethod.GetILGenerator();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldfld, baseField);
        code.Emit(OpCodes.Ret);

        var derivedOwner = module.DefineType(
            "MemberHandles.DerivedOwner",
            TypeAttributes.Public,
            baseOwner);
        DefineDefaultConstructor(derivedOwner, baseConstructor);

        var unrelatedOwner = module.DefineType("MemberHandles.UnrelatedOwner", TypeAttributes.Public);
        DefineDefaultConstructor(unrelatedOwner, typeof(object).GetConstructor(Type.EmptyTypes)!);

        var genericOwner = module.DefineType("MemberHandles.GenericOwner`1", TypeAttributes.Public);
        var genericParameter = genericOwner.DefineGenericParameters("T")[0];
        var genericConstructorDefinition = DefineDefaultConstructor(
            genericOwner,
            typeof(object).GetConstructor(Type.EmptyTypes)!);
        var genericFieldDefinition = genericOwner.DefineField(
            "Value",
            genericParameter,
            FieldAttributes.Public);
        var genericMethodDefinition = genericOwner.DefineMethod(
            "Read",
            MethodAttributes.Public,
            genericParameter,
            Type.EmptyTypes);
        code = genericMethodDefinition.GetILGenerator();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldfld, genericFieldDefinition);
        code.Emit(OpCodes.Ret);
        var closedGenericOwner = genericOwner.MakeGenericType(typeof(int));
        var closedGenericConstructor = TypeBuilder.GetConstructor(
            closedGenericOwner,
            genericConstructorDefinition);
        var closedGenericField = TypeBuilder.GetField(closedGenericOwner, genericFieldDefinition);
        var closedGenericMethod = TypeBuilder.GetMethod(closedGenericOwner, genericMethodDefinition);
        var methodCarrier = module.DefineType(
            "MemberHandles.MethodCarrier",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var genericMethod = methodCarrier.DefineMethod(
            "Identity",
            MethodAttributes.Public | MethodAttributes.Static);
        var methodParameter = genericMethod.DefineGenericParameters("T")[0];
        genericMethod.SetReturnType(methodParameter);
        genericMethod.SetParameters(methodParameter);
        code = genericMethod.GetILGenerator();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ret);
        var closedGenericMethodOnNonGenericType = genericMethod.MakeGenericMethod(typeof(int));
        var byReferenceType = typeof(int).MakeByRefType();
        var byReferenceMethod = methodCarrier.DefineMethod(
            "ByReferenceIdentity",
            MethodAttributes.Public | MethodAttributes.Static,
            byReferenceType,
            [byReferenceType]);
        code = byReferenceMethod.GetILGenerator();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ret);
        var pointerType = typeof(int).MakePointerType();
        var pointerMethod = methodCarrier.DefineMethod(
            "PointerIdentity",
            MethodAttributes.Public | MethodAttributes.Static,
            pointerType,
            [pointerType]);
        code = pointerMethod.GetILGenerator();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ret);
        baseOwner.CreateType();
        derivedOwner.CreateType();
        unrelatedOwner.CreateType();
        genericOwner.CreateType();
        methodCarrier.CreateType();

        var entry = module.DefineType(
            "MemberHandles.EntryPoint",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        MethodBuilder[] scenarios =
        [
            DefineMethodScenario(entry, "NonGenericMethodOneArgument", baseMethod),
            DefineMethodScenario(entry, "GenericMethodOneArgument", closedGenericMethod),
            DefineFieldScenario(entry, "GenericFieldOneArgument", closedGenericField),
            DefineMethodScenario(entry, "GenericConstructorOneArgument", closedGenericConstructor),
            DefineMethodScenario(entry, "GenericMethodExactType", closedGenericMethod, closedGenericOwner),
            DefineFieldScenario(entry, "GenericFieldExactType", closedGenericField, closedGenericOwner),
            DefineMethodScenario(entry, "GenericConstructorExactType", closedGenericConstructor, closedGenericOwner),
            DefineMethodScenario(entry, "GenericMethodDefaultType", closedGenericMethod, defaultContext: true),
            DefineFieldScenario(entry, "GenericFieldDefaultType", closedGenericField, defaultContext: true),
            DefineMethodScenario(entry, "InheritedMethodDerivedType", baseMethod, derivedOwner),
            DefineMethodScenario(entry, "MethodUnrelatedType", baseMethod, unrelatedOwner),
            DefineFieldScenario(entry, "InheritedFieldDerivedType", baseField, derivedOwner),
            DefineMethodScenario(entry, "ConstructedGenericMethodOneArgument", closedGenericMethodOnNonGenericType),
            DefineSignatureTypeScenario(
                entry,
                "ByReferenceSignatureTypes",
                byReferenceMethod,
                nameof(Type.IsByRef)),
            DefineSignatureTypeScenario(
                entry,
                "PointerSignatureTypes",
                pointerMethod,
                nameof(Type.IsPointer)),
        ];
        DefineDispatcher(entry, "Run", scenarios, []);
        DefineDispatcher(entry, "DesktopRun", scenarios, [7, 8, 9]);

        entry.CreateType();

        using var stream = new MemoryStream();
        assembly.Save(stream);
        var image = stream.ToArray();
        PatchMemberTokenOpcodes(image, scenarios.Select(scenario => scenario.Name).ToHashSet(StringComparer.Ordinal));
        return [.. image];
    }

    private static ConstructorBuilder DefineDefaultConstructor(
        TypeBuilder owner,
        ConstructorInfo baseConstructor)
    {
        var constructor = owner.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes);
        var code = constructor.GetILGenerator();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Call, baseConstructor);
        code.Emit(OpCodes.Ret);
        return constructor;
    }

    private static MethodBuilder DefineMethodScenario(
        TypeBuilder entry,
        string name,
        MethodBase member,
        Type? declaringType = null,
        bool defaultContext = false)
    {
        var method = entry.DefineMethod(
            name,
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(int),
            Type.EmptyTypes);
        var code = method.GetILGenerator();
        switch (member)
        {
            case MethodInfo methodInfo:
                code.Emit(OpCodes.Ldftn, methodInfo);
                break;
            case ConstructorInfo constructorInfo:
                code.Emit(OpCodes.Newobj, constructorInfo);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(member));
        }
        EmitDeclaringTypeContext(code, declaringType, defaultContext);
        var materializer = typeof(MethodBase).GetMethod(
            nameof(MethodBase.GetMethodFromHandle),
            declaringType is null && !defaultContext
                ? [typeof(RuntimeMethodHandle)]
                : [typeof(RuntimeMethodHandle), typeof(RuntimeTypeHandle)])!;
        code.Emit(
            OpCodes.Call,
            materializer);
        EmitSuccess(code);
        return method;
    }

    private static MethodBuilder DefineFieldScenario(
        TypeBuilder entry,
        string name,
        FieldInfo field,
        Type? declaringType = null,
        bool defaultContext = false)
    {
        var method = entry.DefineMethod(
            name,
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(int),
            Type.EmptyTypes);
        var code = method.GetILGenerator();
        code.Emit(OpCodes.Ldsflda, field);
        EmitDeclaringTypeContext(code, declaringType, defaultContext);
        var materializer = typeof(FieldInfo).GetMethod(
            nameof(FieldInfo.GetFieldFromHandle),
            declaringType is null && !defaultContext
                ? [typeof(RuntimeFieldHandle)]
                : [typeof(RuntimeFieldHandle), typeof(RuntimeTypeHandle)])!;
        code.Emit(
            OpCodes.Call,
            materializer);
        EmitSuccess(code);
        return method;
    }

    private static MethodBuilder DefineSignatureTypeScenario(
        TypeBuilder entry,
        string name,
        MethodInfo member,
        string typeShapeProperty)
    {
        var method = entry.DefineMethod(
            name,
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(int),
            Type.EmptyTypes);
        var code = method.GetILGenerator();
        var descriptor = code.DeclareLocal(typeof(MethodInfo));
        var failure = code.DefineLabel();
        code.Emit(OpCodes.Ldftn, member);
        code.Emit(
            OpCodes.Call,
            typeof(MethodBase).GetMethod(
                nameof(MethodBase.GetMethodFromHandle),
                [typeof(RuntimeMethodHandle)])!);
        code.Emit(OpCodes.Castclass, typeof(MethodInfo));
        code.Emit(OpCodes.Stloc, descriptor);
        code.Emit(OpCodes.Ldloc, descriptor);
        code.Emit(OpCodes.Callvirt, typeof(MethodInfo).GetProperty(
            nameof(MethodInfo.ReturnType))!.GetMethod!);
        code.Emit(OpCodes.Callvirt, typeof(Type).GetProperty(typeShapeProperty)!.GetMethod!);
        code.Emit(OpCodes.Brfalse, failure);
        code.Emit(OpCodes.Ldloc, descriptor);
        code.Emit(OpCodes.Callvirt, typeof(MethodBase).GetMethod(
            nameof(MethodBase.GetParameters), Type.EmptyTypes)!);
        code.Emit(OpCodes.Ldc_I4_0);
        code.Emit(OpCodes.Ldelem_Ref);
        code.Emit(OpCodes.Callvirt, typeof(ParameterInfo).GetProperty(
            nameof(ParameterInfo.ParameterType))!.GetMethod!);
        code.Emit(OpCodes.Callvirt, typeof(Type).GetProperty(typeShapeProperty)!.GetMethod!);
        code.Emit(OpCodes.Brfalse, failure);
        code.Emit(OpCodes.Ldloc, descriptor);
        code.Emit(OpCodes.Callvirt, typeof(MethodInfo).GetProperty(
            nameof(MethodInfo.ReturnType))!.GetMethod!);
        code.Emit(OpCodes.Callvirt, typeof(Type).GetProperty(nameof(Type.IsClass))!.GetMethod!);
        code.Emit(OpCodes.Brfalse, failure);
        code.Emit(OpCodes.Ldloc, descriptor);
        code.Emit(OpCodes.Callvirt, typeof(MethodInfo).GetProperty(
            nameof(MethodInfo.ReturnType))!.GetMethod!);
        code.Emit(OpCodes.Callvirt, typeof(Type).GetProperty(nameof(Type.IsSealed))!.GetMethod!);
        code.Emit(OpCodes.Brtrue, failure);
        code.Emit(OpCodes.Ldloc, descriptor);
        code.Emit(OpCodes.Callvirt, typeof(MethodInfo).GetProperty(
            nameof(MethodInfo.ReturnType))!.GetMethod!);
        code.Emit(OpCodes.Callvirt, typeof(Type).GetProperty(nameof(Type.BaseType))!.GetMethod!);
        code.Emit(OpCodes.Brtrue, failure);
        code.Emit(OpCodes.Ldc_I4_1);
        code.Emit(OpCodes.Ret);
        code.MarkLabel(failure);
        code.Emit(OpCodes.Ldc_I4_0);
        code.Emit(OpCodes.Ret);
        return method;
    }

    private static void EmitDeclaringTypeContext(
        ILGenerator code,
        Type? declaringType,
        bool defaultContext)
    {
        if (declaringType is not null)
        {
            code.Emit(OpCodes.Ldtoken, declaringType);
            return;
        }
        if (!defaultContext)
        {
            return;
        }
        var context = code.DeclareLocal(typeof(RuntimeTypeHandle));
        code.Emit(OpCodes.Ldloca_S, context);
        code.Emit(OpCodes.Initobj, typeof(RuntimeTypeHandle));
        code.Emit(OpCodes.Ldloc, context);
    }

    private static void EmitSuccess(ILGenerator code)
    {
        code.Emit(OpCodes.Pop);
        code.Emit(OpCodes.Ldc_I4_1);
        code.Emit(OpCodes.Ret);
    }

    private static void DefineDispatcher(
        TypeBuilder entry,
        string name,
        MethodBuilder[] scenarios,
        HashSet<int> profileRejectedInputs)
    {
        var run = entry.DefineMethod(
            name,
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(int),
            [typeof(int)]);
        var code = run.GetILGenerator();
        var labels = scenarios.Select(_ => code.DefineLabel()).ToArray();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Switch, labels);
        code.Emit(OpCodes.Ldc_I4_M1);
        code.Emit(OpCodes.Ret);
        for (var index = 0; index < scenarios.Length; index++)
        {
            code.MarkLabel(labels[index]);
            code.Emit(OpCodes.Call, scenarios[index]);
            if (!profileRejectedInputs.Contains(index))
            {
                code.Emit(OpCodes.Ret);
                continue;
            }
            code.Emit(OpCodes.Pop);
            code.Emit(OpCodes.Newobj, typeof(ArgumentException).GetConstructor(Type.EmptyTypes)!);
            code.Emit(OpCodes.Throw);
        }
    }

    private static void PatchMemberTokenOpcodes(byte[] image, HashSet<string> scenarioNames)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var entry = metadata.TypeDefinitions
            .Select(handle => (Handle: handle, Definition: metadata.GetTypeDefinition(handle)))
            .Single(pair => metadata.GetString(pair.Definition.Name) == "EntryPoint");
        foreach (var methodHandle in entry.Definition.GetMethods())
        {
            var method = metadata.GetMethodDefinition(methodHandle);
            var name = metadata.GetString(method.Name);
            if (!scenarioNames.Contains(name))
            {
                continue;
            }
            var offset = GetInstructionOffset(image, pe.PEHeaders, method.RelativeVirtualAddress);
            if (image[offset] == 0xfe && image[offset + 1] == 0x06)
            {
                image.AsSpan(offset + 2, 4).CopyTo(image.AsSpan(offset + 1, 4));
                image[offset] = 0xd0;
                image[offset + 5] = 0x00;
                continue;
            }
            if (image[offset] is not (0x73 or 0x7f))
            {
                throw new InvalidOperationException("Member token placeholder opcode is invalid.");
            }
            image[offset] = 0xd0;
        }
    }

    private static int GetInstructionOffset(byte[] image, PEHeaders headers, int relativeVirtualAddress)
    {
        var section = headers.SectionHeaders.Single(candidate =>
            relativeVirtualAddress >= candidate.VirtualAddress &&
            relativeVirtualAddress < candidate.VirtualAddress + candidate.SizeOfRawData);
        var body = section.PointerToRawData + relativeVirtualAddress - section.VirtualAddress;
        var format = image[body] & 0x3;
        return format switch
        {
            0x2 => body + 1,
            0x3 => body + (BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(body, 2)) >> 12) * 4,
            _ => throw new InvalidOperationException("Emitted method body header is invalid."),
        };
    }
}
