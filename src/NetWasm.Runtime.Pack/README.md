# NetWasm.Runtime.Pack

`NetWasm.Runtime.Pack` contains the SDK-owned runtime archive materializer.
The NetWasm SDK uses its packaged runtime manifests and native archives to
materialize the runtime module selected for an application’s Wasm target.
The required Emscripten, musl and compiler-rt archives are restored with this
package; an application build does not need a local emsdk cache. The package
manifest hashes the archives, and `runtime/LICENSE.txt` carries their notices.
Maintainers use the pinned Emscripten SDK when regenerating the package.
