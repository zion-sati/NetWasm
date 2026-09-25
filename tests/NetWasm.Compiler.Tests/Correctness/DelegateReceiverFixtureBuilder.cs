using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class DelegateReceiverFixtureBuilder : IEmittedAssemblyBuilder
{
    public ImmutableArray<byte> Build()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("DelegateReceivers"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("DelegateReceivers.dll");
        var receiver = module.DefineType("DelegateReceivers.Receiver", TypeAttributes.Public | TypeAttributes.Sealed);
        var value = receiver.DefineField("Value", typeof(int), FieldAttributes.Public);
        var constructor = receiver.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [typeof(int)]);
        var code = constructor.GetILGenerator();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldarg_1);
        code.Emit(OpCodes.Stfld, value);
        code.Emit(OpCodes.Ret);
        var read = receiver.DefineMethod("Read", MethodAttributes.Public, typeof(int), Type.EmptyTypes);
        code = read.GetILGenerator();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldfld, value);
        code.Emit(OpCodes.Ret);

        var entry = module.DefineType("DelegateReceivers.EntryPoint",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        foreach (var open in new[] { false, true })
        {
            var delegateType = module.DefineType("DelegateReceivers." + (open ? "OpenReader" : "ClosedReader"),
                TypeAttributes.Public | TypeAttributes.Sealed, typeof(MulticastDelegate));
            var createDelegate = delegateType.DefineConstructor(
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.RTSpecialName,
                CallingConventions.Standard, [typeof(object), typeof(nint)]);
            createDelegate.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);
            var invoke = delegateType.DefineMethod("Invoke",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual,
                typeof(int), open ? [receiver] : Type.EmptyTypes);
            invoke.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);
            DefineEntry(entry, open ? "RunOpen" : "RunClosed", constructor, read, createDelegate, invoke, open, false);
            if (open)
            {
                DefineEntry(entry, "RunOpenNull", constructor, read, createDelegate, invoke, true, true);
            }
            delegateType.CreateType();
        }
        receiver.CreateType();
        entry.CreateType();
        using var stream = new MemoryStream();
        assembly.Save(stream);
        return [.. stream.ToArray()];
    }

    private static void DefineEntry(TypeBuilder entry, string name, ConstructorBuilder receiverConstructor,
        MethodBuilder read, ConstructorBuilder delegateConstructor, MethodBuilder invoke, bool open, bool missing)
    {
        var method = entry.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static,
            typeof(int), [typeof(int)]);
        var code = method.GetILGenerator();
        if (open)
        {
            code.Emit(OpCodes.Ldnull);
        }
        else
        {
            code.Emit(OpCodes.Ldarg_0);
            code.Emit(OpCodes.Newobj, receiverConstructor);
        }
        code.Emit(OpCodes.Ldftn, read);
        code.Emit(OpCodes.Newobj, delegateConstructor);
        if (open)
        {
            if (missing)
            {
                code.Emit(OpCodes.Ldnull);
            }
            else
            {
                code.Emit(OpCodes.Ldarg_0);
                code.Emit(OpCodes.Newobj, receiverConstructor);
            }
        }
        code.Emit(OpCodes.Callvirt, invoke);
        code.Emit(OpCodes.Ret);
    }
}
