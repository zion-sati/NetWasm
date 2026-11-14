# NetWasm.System.Net.Http.Json

`NetWasm.System.Net.Http.Json` supplies the metadata-first `System.Net.Http.Json`
surface for `netwasm0.1`. Use source-generated `JsonTypeInfo<T>` or
`JsonSerializerContext` metadata; reflection-based convenience overloads are
intentionally outside this profile.

The package uses the existing WASI HTTP response stream and the incremental
`System.Text.Json` reader. The initial profile accepts absent or UTF-8
`Content-Type` charsets and rejects unsupported response encodings.
