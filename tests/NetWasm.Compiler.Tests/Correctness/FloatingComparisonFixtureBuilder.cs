using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;

namespace NetWasm.Compiler.Tests.Correctness;

// Fixture authoring only: each selected instruction gets its own method so
// metadata-shape checks can independently verify the opcode under execution.
internal sealed class FloatingComparisonFixtureBuilder : IEmittedAssemblyBuilder
{
    private static readonly OpCode[] Operations =
    [
        OpCodes.Ceq, OpCodes.Cgt, OpCodes.Cgt_Un, OpCodes.Clt, OpCodes.Clt_Un,
        OpCodes.Beq, OpCodes.Bne_Un, OpCodes.Bgt, OpCodes.Bgt_Un,
        OpCodes.Bge, OpCodes.Bge_Un, OpCodes.Blt, OpCodes.Blt_Un,
        OpCodes.Ble, OpCodes.Ble_Un,
        OpCodes.Beq_S, OpCodes.Bne_Un_S, OpCodes.Bgt_S, OpCodes.Bgt_Un_S,
        OpCodes.Bge_S, OpCodes.Bge_Un_S, OpCodes.Blt_S, OpCodes.Blt_Un_S,
        OpCodes.Ble_S, OpCodes.Ble_Un_S,
    ];

    public ImmutableArray<byte> Build()
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("FloatingCilComparisons"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("FloatingCilComparisons.dll");
        var type = module.DefineType("FloatingCilComparisons.EntryPoint",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var singleValues = DefineValues(type, single: true);
        var doubleValues = DefineValues(type, single: false);
        var methods = new List<MethodBuilder>();
        foreach (var single in new[] { true, false })
        {
            foreach (var operation in Operations)
            {
                methods.Add(DefineComparison(type, single, operation));
            }
        }

        var run = type.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static,
            typeof(int), [typeof(int)]);
        var code = run.GetILGenerator();
        var labels = methods.Select(_ => code.DefineLabel()).ToArray();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldc_I4, 64);
        code.Emit(OpCodes.Div);
        code.Emit(OpCodes.Switch, labels);
        code.Emit(OpCodes.Ldc_I4_M1);
        code.Emit(OpCodes.Ret);
        for (var index = 0; index < methods.Count; index++)
        {
            var values = index < Operations.Length ? singleValues : doubleValues;
            code.MarkLabel(labels[index]);
            code.Emit(OpCodes.Ldarg_0);
            code.Emit(OpCodes.Ldc_I4, 8);
            code.Emit(OpCodes.Div);
            code.Emit(OpCodes.Ldc_I4, 8);
            code.Emit(OpCodes.Rem);
            code.Emit(OpCodes.Call, values);
            code.Emit(OpCodes.Ldarg_0);
            code.Emit(OpCodes.Ldc_I4, 8);
            code.Emit(OpCodes.Rem);
            code.Emit(OpCodes.Call, values);
            code.Emit(OpCodes.Call, methods[index]);
            code.Emit(OpCodes.Ret);
        }
        type.CreateType();
        using var stream = new MemoryStream();
        assembly.Save(stream);
        return [.. stream.ToArray()];
    }

    private static MethodBuilder DefineValues(TypeBuilder type, bool single)
    {
        var method = type.DefineMethod(single ? "SingleValue" : "DoubleValue",
            MethodAttributes.Private | MethodAttributes.Static,
            single ? typeof(float) : typeof(double), [typeof(int)]);
        var code = method.GetILGenerator();
        double[] values = [double.NaN, double.NegativeInfinity, -1, -0.0, 0, 1,
            double.PositiveInfinity, single ? float.Epsilon : double.Epsilon];
        var labels = values.Select(_ => code.DefineLabel()).ToArray();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Switch, labels);
        code.Emit(OpCodes.Br, labels[0]);
        for (var index = 0; index < values.Length; index++)
        {
            code.MarkLabel(labels[index]);
            if (single)
            {
                code.Emit(OpCodes.Ldc_R4, (float)values[index]);
            }
            else
            {
                code.Emit(OpCodes.Ldc_R8, values[index]);
            }
            code.Emit(OpCodes.Ret);
        }
        return method;
    }

    private static MethodBuilder DefineComparison(TypeBuilder type, bool single, OpCode operation)
    {
        var valueType = single ? typeof(float) : typeof(double);
        var method = type.DefineMethod((single ? "Single_" : "Double_") + operation.Name,
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(int), [valueType, valueType]);
        var code = method.GetILGenerator();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldarg_1);
        if (operation.FlowControl == FlowControl.Cond_Branch)
        {
            var taken = code.DefineLabel();
            code.Emit(operation, taken);
            code.Emit(OpCodes.Ldc_I4_0);
            code.Emit(OpCodes.Ret);
            code.MarkLabel(taken);
            code.Emit(OpCodes.Ldc_I4_1);
        }
        else
        {
            code.Emit(operation);
        }
        code.Emit(OpCodes.Ret);
        return method;
    }
}
