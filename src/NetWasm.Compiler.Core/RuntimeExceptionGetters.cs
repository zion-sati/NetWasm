namespace NetWasm.Compiler.Core;

// CoreLib's System.RuntimeExceptionHelpers returns the exception each failed
// check throws, as NativeAOT's GetRuntimeException does: a new one, or, when
// even that allocation fails, the OutOfMemoryException frozen in the image. A
// check passes managed.throw its kind's tag where the exception would go, and
// managed.throw calls the kind's getter: no object lives at an address that
// small.
public static class RuntimeExceptionGetters
{
    public const string TypeName = "System.RuntimeExceptionHelpers";

    // The class whose GetInstance returns the frozen OutOfMemoryException.
    public const string PreallocatedOutOfMemoryTypeName = "System.PreallocatedOutOfMemoryException";

    // HResults.COR_E_OUTOFMEMORY, which new OutOfMemoryException(message: null)
    // gives the frozen exception.
    public const int OutOfMemoryHResult = unchecked((int)0x8007000E);

    // Tags run from 1, below this.
    public const int TagLimit = 16;

    public static string MethodName(ManagedExceptionKind kind) =>
        kind == ManagedExceptionKind.JSException ? "GetJSException" : $"Get{kind}Exception";

    public static int Tag(ManagedExceptionKind kind) => (int)kind + 1;
}
