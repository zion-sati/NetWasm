// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma once

#include "pal_compiler.h"

#include <stdint.h>

// Sets the managed callback the seed source draws from (System.Security.Cryptography's CryptoInitializer).
PALEXPORT void NetWasmCryptoNative_SetRandomSource(int32_t (*source)(uint8_t* buffer, int32_t length));

// Registers OpenSSL's seed source over the platform's random source (pal_netwasm_seed.c); nonzero on success.
int32_t NetWasmCryptoNative_InstallSeedSource(void);
