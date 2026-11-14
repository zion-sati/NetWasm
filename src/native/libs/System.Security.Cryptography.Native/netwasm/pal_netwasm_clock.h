// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma once

#include "pal_compiler.h"

#include <stdint.h>

// Sets the managed callback OpenSSL's clock reads (System.Security.Cryptography's CryptoInitializer).
PALEXPORT void NetWasmCryptoNative_SetClock(int32_t (*source)(int32_t kind, int64_t* seconds, int32_t* nanoseconds));
