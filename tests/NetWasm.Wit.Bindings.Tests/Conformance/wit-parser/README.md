# wit-parser test inputs

`ui/` holds the WIT inputs of wit-parser's own tests (`tests/ui`), unchanged,
from the wit-parser 0.256.0 crate: the parser in wasm-tools 1.256.0, the version
`eng/toolchain.json` pins. The expected JSON outputs are left out. `ui/parse-fail`
holds the inputs wit-parser rejects.

Crate: https://static.crates.io/crates/wit-parser/wit-parser-0.256.0.crate,
SHA-256 `fa073dadefba859cb03f689a0c2e3efc51f883bf86b907eaa9709bfd9e3a00a9`.

wit-parser is available under Apache-2.0 WITH LLVM-exception, Apache-2.0 or MIT.
These files are used under MIT; `LICENSE-MIT` is the crate's license text.

Moving the wasm-tools pin moves this corpus to the matching wit-parser release.
