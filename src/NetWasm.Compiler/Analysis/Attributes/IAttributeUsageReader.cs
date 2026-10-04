using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

internal readonly record struct AttributeUsage(bool Inherited, bool AllowMultiple);

internal interface IAttributeUsageReader
{
    AttributeUsage Read(CliTypeIdentity attributeType);
}
