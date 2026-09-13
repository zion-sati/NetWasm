using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed class ValueTypeDefinitionStackKindResolver : IValueTypeDefinitionStackKindResolver
{
    public CliValueKind Resolve(string canonicalName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalName);

        return canonicalName switch
        {
            "System.Boolean" or
            "System.Byte" or
            "System.Char" or
            "System.Int16" or
            "System.Int32" or
            "System.SByte" or
            "System.UInt16" or
            "System.UInt32" => CliValueKind.I4,
            "System.Int64" or
            "System.UInt64" => CliValueKind.I8,
            "System.Single" => CliValueKind.F4,
            "System.Double" => CliValueKind.F8,
            "System.IntPtr" or
            "System.UIntPtr" => CliValueKind.NativeInt,
            "System.Void" => CliValueKind.Void,
            _ => CliValueKind.ValueType,
        };
    }
}
