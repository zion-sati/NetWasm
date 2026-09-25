using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed class StaticInitializerFunctionPlanner(
    ITypeDescriptorSource descriptors,
    ITypeRepository types) : IStaticInitializerFunctionPlanner
{
    public ModuleDataPlan Build(WasmEmissionRequest request, WasmModulePlan functions, ModuleDataPlan data)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentNullException.ThrowIfNull(data);
        if (data.StaticInitializerGuards.IsEmpty)
            return data;

        var exception = descriptors.TypeDescriptors.Single(descriptor =>
            types.GetTypeDefinition(descriptor.Type).FullName == "System.Exception");
        var metadata = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(metadata, exception.TypeId);
        var metadataAddress = checked((data.StaticDataEnd + 3) & ~3);
        var guards = data.StaticInitializerGuards.ToBuilder();
        var initializers = ImmutableArray.CreateBuilder<StaticInitializerFunction>();
        var next = functions.StaticInitializerFunctionBase;
        foreach (var (key, guard) in data.StaticInitializerGuards.OrderBy(pair => pair.Value.Address))
        {
            var initializerIndex = guard.Direct is EntityKey definition
                ? functions.FunctionIndices.DirectMethods[definition].Value
                : functions.FunctionIndices.ConstructedMethods[guard.Constructed!].Value;
            initializers.Add(new(key, next, initializerIndex, guard.Address));
            guards[key] = guard with { FunctionIndex = OptionalFunctionIndex.At(next++) };
        }
        return data with
        {
            StaticInitializerGuards = guards.ToImmutable(),
            DataSegments = data.DataSegments.Add(new(metadataAddress, [.. metadata])),
            StaticDataEnd = checked(metadataAddress + metadata.Length),
            StaticInitializerFunctions = new(initializers.ToImmutable(), metadataAddress, next),
        };
    }
}
