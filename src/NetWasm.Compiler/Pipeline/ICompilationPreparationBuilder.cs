using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Pipeline;

internal interface ICompilationPreparationBuilder
{
    CompilationPreparation Prepare(
        MetadataCompilationSnapshot metadata,
        CompilerOptions options);
}
