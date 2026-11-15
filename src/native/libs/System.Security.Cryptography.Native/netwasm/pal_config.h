// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

#pragma once
// NetWasm: the shim builds single-threaded; emscripten's libc implements pthread_once for it.
#include <pthread.h>
