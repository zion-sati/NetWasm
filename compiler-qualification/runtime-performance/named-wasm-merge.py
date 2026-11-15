#!/usr/bin/env python3
# Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
# SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

"""Diagnostic stand-in for wasm-merge, selected through NETWASM_WASM_MERGE_PATH.

Binaryen writes a name section only with -g, so the merged module would lose
the managed function names before optimization. This adds -g; iterate.py
checks that the optimized product output is unchanged by it.
"""

import os
import subprocess
import sys

sys.exit(subprocess.run([os.environ['NETWASM_REAL_WASM_MERGE'], *sys.argv[1:], '-g']).returncode)
