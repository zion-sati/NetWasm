using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal static class InteropTypeClassifier
{
    public static bool IsString(CliTypeIdentity type) =>
        type.CanonicalName == "primitive:string";

    public static bool IsByteArray(CliTypeIdentity type) =>
        type.Shape == CliTypeShape.SzArray &&
        type.ElementType!.CanonicalName == "primitive:u1";

    public static bool IsJSObject(CliTypeIdentity type) =>
        type.FullName == "System.Runtime.InteropServices.JavaScript.JSObject";

    public static bool IsSubscription(CliTypeIdentity type) =>
        type.FullName == "System.Runtime.InteropServices.JavaScript.JSSubscription";

    public static bool IsHostObject(CliTypeIdentity type) =>
        IsJSObject(type) || IsSubscription(type);
}
