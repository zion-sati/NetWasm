using System.Collections.Immutable;
using Microsoft.Build.Framework;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tasks.MsBuild;

internal interface ICompilerReferenceOriginReader
{
    ImmutableArray<CompilationReferenceOrigin> Read(
        ITaskItem[] references, CompilationReferenceRole role);
}
