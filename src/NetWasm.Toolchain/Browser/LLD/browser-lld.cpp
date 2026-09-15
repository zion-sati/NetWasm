// Browser-only Wasm linker entry point. LLVM retains its Apache-2.0 WITH
// LLVM-exception license; this adapter uses NetWasm Community License 1.0.
#include "lld/Common/Driver.h"
#include "llvm/Support/raw_ostream.h"

LLD_HAS_DRIVER(wasm)

// Bit 0: link failed. Bit 1: LLD cannot safely run again. Hosts must discard
// the module after any failure, including a trap thrown before returning.
extern "C" int netwasm_lld_link(int argc, const char **argv) {
  auto result = lld::lldMain({argv, static_cast<size_t>(argc)}, llvm::outs(),
                           llvm::errs(), {{lld::Wasm, &lld::wasm::link}});
  return (result.retCode != 0 ? 1 : 0) | (result.canRunAgain ? 0 : 2);
}
