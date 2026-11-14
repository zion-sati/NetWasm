# NetWasm.System.IO.Pipelines

`NetWasm.System.IO.Pipelines` supplies the `System.IO.Pipelines` assembly for
the `netwasm0.1` profile. It is an independently versioned ported-library
package for applications that opt into the NetWasm pipelines surface.

The package carries the `NetWasm,Version=v0.1` asset and transitively supplies
the internal `System.Memory` support assembly required by its buffer and
sequence contracts. The package targets only `netwasm0.1`.
