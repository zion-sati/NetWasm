using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// The calls that tell reachability the program reads type names, delegate
// Invoke methods, generic arguments or member names, so the compiler emits their
// runtime facts. A call that inlining removed would hide that need, so the
// inliner keeps these calls.
internal static class ReachabilityMarkers
{
    public static RuntimeTypeNamePayload TypeNamePayload(MethodInstanceModel method) =>
        method.DeclaringType.FullName != "System.Type"
            ? RuntimeTypeNamePayload.None
            : method.Definition.Name switch
            {
                "GetRuntimeName" => RuntimeTypeNamePayload.Name,
                "GetRuntimeNamespace" => RuntimeTypeNamePayload.Namespace,
                "GetRuntimeFullName" => RuntimeTypeNamePayload.FullName,
                "GetRuntimeDisplayName" => RuntimeTypeNamePayload.DisplayName,
                _ => RuntimeTypeNamePayload.None,
            };

    public static bool RequiresDelegateInvoke(MethodInstanceModel method) =>
        method.DeclaringType.FullName == "System.Type" && method.Definition.Name == "GetDelegateInvokeMethod";

    public static bool RequiresGenericArguments(MethodInstanceModel method) =>
        method.DeclaringType.FullName == "System.Type" && method.Definition.Name == "GetGenericArguments";

    public static bool RequiresMemberNames(MethodInstanceModel method) =>
        method.DeclaringType.FullName == "System.Reflection.MemberInfo" && method.Definition.Name == "get_Name";

    public static bool IsMarker(MethodInstanceModel method) =>
        TypeNamePayload(method) != RuntimeTypeNamePayload.None ||
        RequiresDelegateInvoke(method) ||
        RequiresGenericArguments(method) ||
        RequiresMemberNames(method);
}
