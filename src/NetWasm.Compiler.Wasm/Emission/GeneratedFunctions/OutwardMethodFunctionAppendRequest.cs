using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed record OutwardMethodFunctionAppendRequest(
    IList<WasmFunctionDefinition> Functions,
    int ImportCount,
    IDictionary<string, int> AsyncHelperIndices,
    string GeneratedFunctionName,
    string ExportName,
    MethodDefinitionModel Method,
    JavaScriptAsyncMethodBinding? AsyncBinding,
    ManagedAsyncBoundaryNames? AsyncNames,
    ManagedAsyncBoundaryKinds? AsyncKinds,
    RuntimeInitializationPlan Initialization,
    bool HasFinalizers,
    bool ReportTerminalExceptions,
    ManagedBoundaryKind SynchronousKind,
    IFunctionIndexResolver FunctionIndices,
    ICollection<ManagedBoundaryPlanEntry> BoundaryEntries,
    EntityKey? ArgumentFactory = null);
