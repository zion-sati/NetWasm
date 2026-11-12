using System.Collections.Generic;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal interface INativeCallSitePlanner
{
    // Lowers each unmanaged calli signature as a DllImport's and gives each physical signature a
    // trampoline import, numbered from firstImportIndex.
    NativeCallSitePlan Plan(IEnumerable<NativeCallSiteUse> uses, int firstImportIndex);
}
