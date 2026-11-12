using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// The calls that tell reachability the program reads generic arguments, generic definitions, declaring types, assemblies or member names, so the compiler emits their runtime facts. A call that inlining removed would hide that need, so the
// inliner keeps these calls.
internal static class ReachabilityMarkers
{
    public static bool RequiresGenericArguments(MethodInstanceModel method) =>
        method.DeclaringType.FullName == "System.Type" && method.Definition.Name == "GetGenericArguments";

    // The table reader Type.DeclaringType calls, which overrides MemberInfo's, so a call through
    // MemberInfo reaches it as well.
    public static bool RequiresDeclaringTypes(MethodInstanceModel method) =>
        method.DeclaringType.FullName == "System.RuntimeTypeTable" && method.Definition.Name == "DeclaringType";

    // The table reader Type.GetGenericTypeDefinition calls.
    public static bool RequiresGenericDefinitions(MethodInstanceModel method) =>
        method.DeclaringType.FullName == "System.RuntimeTypeTable" && method.Definition.Name == "GenericDefinition";

    // The table readers Type.Assembly and Assembly.GetEntryAssembly call.
    public static bool RequiresAssemblies(MethodInstanceModel method) =>
        method.DeclaringType.FullName == "System.RuntimeTypeTable" &&
        method.Definition.Name is "Assembly" or "EntryAssembly" or "AssemblyCapacity";

    // The table reader Type.GetElementType calls.
    public static bool RequiresElementTypes(MethodInstanceModel method) =>
        method.DeclaringType.FullName == "System.RuntimeTypeTable" && method.Definition.Name == "ElementType";

    // The table reader Type.Attributes and the queries built on it call.
    public static bool RequiresTypeAttributes(MethodInstanceModel method) =>
        method.DeclaringType.FullName == "System.RuntimeTypeTable" && method.Definition.Name == "Attributes";

    // The table reader Type.GetInterfaces calls.
    public static bool RequiresInterfaces(MethodInstanceModel method) =>
        method.DeclaringType.FullName == "System.RuntimeTypeTable" && method.Definition.Name == "Interfaces";

    public static bool RequiresMemberNames(MethodInstanceModel method) =>
        method.DeclaringType.FullName == "System.Reflection.MemberInfo" && method.Definition.Name == "get_Name";

    public static bool IsMarker(MethodInstanceModel method) =>
        RequiresGenericArguments(method) ||
        RequiresDeclaringTypes(method) ||
        RequiresGenericDefinitions(method) ||
        RequiresAssemblies(method) ||
        RequiresElementTypes(method) ||
        RequiresTypeAttributes(method) ||
        RequiresInterfaces(method) ||
        RequiresMemberNames(method);
}
