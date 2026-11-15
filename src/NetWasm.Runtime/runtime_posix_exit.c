// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

#include <stdlib.h>
#include "runtime_process_exit.h"

_Noreturn void _Exit(int status)
{
    runtime_exit_process(status);
}
