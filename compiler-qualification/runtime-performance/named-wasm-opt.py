#!/usr/bin/env python3
# Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
# SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

"""Diagnostic stand-in for wasm-opt, selected through NETWASM_WASM_OPT_PATH.

Runs the real tool exactly as requested. When the request strips debug
information, it repeats the same optimization keeping names (-g, no
--strip-debug) and writes that copy to NETWASM_NAMED_WASM_DIR, so engine
output can be read by function name. iterate.py checks the copy against the product output.

With NETWASM_KEEP_NAMES set, for a twin that is never timed, every run keeps
names in its own output, so names survive earlier stripped steps such as the
runtime's optimization, and the stripped steps' outputs are copied as above.
profile.py uses this to name runtime functions too.
"""

import os
import shutil
from pathlib import Path
import subprocess
import sys

real = os.environ['NETWASM_REAL_WASM_OPT']
arguments = sys.argv[1:]
if os.environ.get('NETWASM_KEEP_NAMES'):
    status = subprocess.run([real, *[a for a in arguments if a != '--strip-debug'], '-g']).returncode
    if status == 0 and '--strip-debug' in arguments and '--output' in arguments:
        output = Path(arguments[arguments.index('--output') + 1])
        named = Path(os.environ['NETWASM_NAMED_WASM_DIR']) / f'{output.stem}.named.wasm'
        named.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(output, named)
    sys.exit(status)
status = subprocess.run([real, *arguments]).returncode
if status != 0 or '--strip-debug' not in arguments or '--output' not in arguments:
    sys.exit(status)
output = Path(arguments[arguments.index('--output') + 1])
named = Path(os.environ['NETWASM_NAMED_WASM_DIR']) / f'{output.stem}.named.wasm'
named.parent.mkdir(parents=True, exist_ok=True)
shutil.copyfile(arguments[0], named.parent / f'{output.stem}.input.wasm')
named_arguments = [argument for argument in arguments if argument != '--strip-debug'] + ['-g']
named_arguments[named_arguments.index('--output') + 1] = str(named)
sys.exit(subprocess.run([real, *named_arguments]).returncode)
