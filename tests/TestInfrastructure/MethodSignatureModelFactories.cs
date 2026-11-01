using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Core;

// Signatures for test fixtures: the compiler builds its own from metadata.
internal static class MethodSignatureModelFactories
{
    extension(MethodSignatureModel)
    {
        public static MethodSignatureModel Create(CliValueKind returnType, params CliValueKind[] parameterTypes) =>
            new(returnType, [.. parameterTypes]);

        public static MethodSignatureModel Create(CliTypeIdentity returnType, params CliTypeIdentity[] parameterTypes) =>
            new(returnType, [.. parameterTypes]);
    }
}
