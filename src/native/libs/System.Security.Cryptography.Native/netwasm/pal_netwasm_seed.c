// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// NetWasm: OpenSSL's seed source, as a built-in provider over the platform's random source.
//
// OpenSSL's own seed source (providers/implementations/rands/seed_src.c) gathers entropy from the operating system,
// which on WebAssembly would mean a WASI Preview 1 import that NetWasm does not use. This one has the same shape and
// asks a managed [UnmanagedCallersOnly] callback instead, which fills the buffer from RandomNumberGenerator, so the
// platform's random source (wasi:random, or a platform plugin) governs it. System.Security.Cryptography registers the
// callback (NetWasmCryptoNative_SetRandomSource) before OpenSSL initializes, and NetWasmCryptoNative_InstallSeedSource
// makes it OpenSSL's seed source before OpenSSL creates its generators.

#include "pal_netwasm_seed.h"

#include <openssl/core_dispatch.h>
#include <openssl/core_names.h>
#include <openssl/crypto.h>
#include <openssl/err.h>
#include <openssl/params.h>
#include <openssl/provider.h>
#include <openssl/rand.h>
#include <string.h>

// The managed callback: fills buffer with length random bytes and returns nonzero on success.
static int32_t (*s_getRandomBytes)(uint8_t* buffer, int32_t length);

void NetWasmCryptoNative_SetRandomSource(int32_t (*source)(uint8_t* buffer, int32_t length))
{
    s_getRandomBytes = source;
}

#define NETWASM_SEED_PROVIDER "netwasm-seed"
#define NETWASM_SEED_ALGORITHM "NETWASM-SEED-SRC"
#define NETWASM_SEED_STRENGTH 1024

typedef struct
{
    int state;
} NetWasmSeedSource;

static int FillRandom(unsigned char* out, size_t length)
{
    if (s_getRandomBytes == NULL)
    {
        return 0;
    }
    while (length > 0)
    {
        int32_t chunk = length > INT32_MAX ? INT32_MAX : (int32_t)length;
        if (!s_getRandomBytes(out, chunk))
        {
            return 0;
        }
        out += chunk;
        length -= (size_t)chunk;
    }
    return 1;
}

static void* SeedNew(void* provctx, void* parent, const OSSL_DISPATCH* parent_dispatch)
{
    (void)provctx;
    (void)parent_dispatch;
    if (parent != NULL)
    {
        // As OpenSSL's seed source: it has no parent.
        return NULL;
    }
    NetWasmSeedSource* seed = OPENSSL_zalloc(sizeof(NetWasmSeedSource));
    if (seed != NULL)
    {
        seed->state = EVP_RAND_STATE_UNINITIALISED;
    }
    return seed;
}

static void SeedFree(void* vseed)
{
    OPENSSL_free(vseed);
}

static int SeedInstantiate(void* vseed, unsigned int strength, int prediction_resistance,
    const unsigned char* pstr, size_t pstr_len, const OSSL_PARAM params[])
{
    (void)strength;
    (void)prediction_resistance;
    (void)pstr;
    (void)pstr_len;
    (void)params;
    ((NetWasmSeedSource*)vseed)->state = EVP_RAND_STATE_READY;
    return 1;
}

static int SeedUninstantiate(void* vseed)
{
    ((NetWasmSeedSource*)vseed)->state = EVP_RAND_STATE_UNINITIALISED;
    return 1;
}

static int SeedGenerate(void* vseed, unsigned char* out, size_t outlen, unsigned int strength,
    int prediction_resistance, const unsigned char* adin, size_t adin_len)
{
    (void)strength;
    (void)prediction_resistance;
    if (((NetWasmSeedSource*)vseed)->state != EVP_RAND_STATE_READY || !FillRandom(out, outlen))
    {
        return 0;
    }
    // Additional input is mixed in, as OpenSSL's seed source does through its pool.
    for (size_t i = 0; i < adin_len && outlen > 0; i++)
    {
        out[i % outlen] ^= adin[i];
    }
    return 1;
}

static int SeedReseed(void* vseed, int prediction_resistance, const unsigned char* ent, size_t ent_len,
    const unsigned char* adin, size_t adin_len)
{
    (void)prediction_resistance;
    (void)ent;
    (void)ent_len;
    (void)adin;
    (void)adin_len;
    return ((NetWasmSeedSource*)vseed)->state == EVP_RAND_STATE_READY;
}

static int SeedGetCtxParams(void* vseed, OSSL_PARAM params[])
{
    OSSL_PARAM* p = OSSL_PARAM_locate(params, OSSL_RAND_PARAM_STATE);
    if (p != NULL && !OSSL_PARAM_set_int(p, ((NetWasmSeedSource*)vseed)->state))
    {
        return 0;
    }
    p = OSSL_PARAM_locate(params, OSSL_RAND_PARAM_STRENGTH);
    if (p != NULL && !OSSL_PARAM_set_int(p, NETWASM_SEED_STRENGTH))
    {
        return 0;
    }
    p = OSSL_PARAM_locate(params, OSSL_RAND_PARAM_MAX_REQUEST);
    if (p != NULL && !OSSL_PARAM_set_size_t(p, 128))
    {
        return 0;
    }
    return 1;
}

static const OSSL_PARAM* SeedGettableCtxParams(void* vseed, void* provctx)
{
    (void)vseed;
    (void)provctx;
    static const OSSL_PARAM known[] =
    {
        OSSL_PARAM_int(OSSL_RAND_PARAM_STATE, NULL),
        OSSL_PARAM_uint(OSSL_RAND_PARAM_STRENGTH, NULL),
        OSSL_PARAM_size_t(OSSL_RAND_PARAM_MAX_REQUEST, NULL),
        OSSL_PARAM_END
    };
    return known;
}

static int SeedVerifyZeroization(void* vseed)
{
    (void)vseed;
    return 1;
}

static size_t SeedGetSeed(void* vseed, unsigned char** pout, int entropy, size_t min_len, size_t max_len,
    int prediction_resistance, const unsigned char* adin, size_t adin_len)
{
    (void)vseed;
    (void)prediction_resistance;
    // Enough bytes for the requested entropy, one bit per bit, within the caller's bounds.
    size_t length = ((size_t)(entropy > 0 ? entropy : 0) + 7) / 8;
    if (length < min_len)
    {
        length = min_len;
    }
    if (length > max_len)
    {
        return 0;
    }
    unsigned char* buffer = OPENSSL_secure_malloc(length);
    if (buffer == NULL)
    {
        return 0;
    }
    if (!FillRandom(buffer, length))
    {
        OPENSSL_secure_clear_free(buffer, length);
        return 0;
    }
    for (size_t i = 0; i < adin_len && length > 0; i++)
    {
        buffer[i % length] ^= adin[i];
    }
    *pout = buffer;
    return length;
}

static void SeedClearSeed(void* vseed, unsigned char* out, size_t outlen)
{
    (void)vseed;
    OPENSSL_secure_clear_free(out, outlen);
}

static int SeedEnableLocking(void* vseed)
{
    (void)vseed;
    return 1;
}

static int SeedLock(void* vseed)
{
    (void)vseed;
    return 1;
}

static void SeedUnlock(void* vseed)
{
    (void)vseed;
}

static const OSSL_DISPATCH s_seedFunctions[] =
{
    { OSSL_FUNC_RAND_NEWCTX, (void (*)(void))SeedNew },
    { OSSL_FUNC_RAND_FREECTX, (void (*)(void))SeedFree },
    { OSSL_FUNC_RAND_INSTANTIATE, (void (*)(void))SeedInstantiate },
    { OSSL_FUNC_RAND_UNINSTANTIATE, (void (*)(void))SeedUninstantiate },
    { OSSL_FUNC_RAND_GENERATE, (void (*)(void))SeedGenerate },
    { OSSL_FUNC_RAND_RESEED, (void (*)(void))SeedReseed },
    { OSSL_FUNC_RAND_ENABLE_LOCKING, (void (*)(void))SeedEnableLocking },
    { OSSL_FUNC_RAND_LOCK, (void (*)(void))SeedLock },
    { OSSL_FUNC_RAND_UNLOCK, (void (*)(void))SeedUnlock },
    { OSSL_FUNC_RAND_GETTABLE_CTX_PARAMS, (void (*)(void))SeedGettableCtxParams },
    { OSSL_FUNC_RAND_GET_CTX_PARAMS, (void (*)(void))SeedGetCtxParams },
    { OSSL_FUNC_RAND_VERIFY_ZEROIZATION, (void (*)(void))SeedVerifyZeroization },
    { OSSL_FUNC_RAND_GET_SEED, (void (*)(void))SeedGetSeed },
    { OSSL_FUNC_RAND_CLEAR_SEED, (void (*)(void))SeedClearSeed },
    OSSL_DISPATCH_END
};

static const OSSL_ALGORITHM s_seedAlgorithms[] =
{
    { NETWASM_SEED_ALGORITHM, "provider=" NETWASM_SEED_PROVIDER, s_seedFunctions, "The platform's random source" },
    { NULL, NULL, NULL, NULL }
};

static const OSSL_ALGORITHM* SeedQuery(void* provctx, int operation_id, int* no_cache)
{
    (void)provctx;
    *no_cache = 0;
    return operation_id == OSSL_OP_RAND ? s_seedAlgorithms : NULL;
}

static const OSSL_DISPATCH s_providerFunctions[] =
{
    { OSSL_FUNC_PROVIDER_QUERY_OPERATION, (void (*)(void))SeedQuery },
    OSSL_DISPATCH_END
};

static int SeedProviderInit(const OSSL_CORE_HANDLE* handle, const OSSL_DISPATCH* in, const OSSL_DISPATCH** out,
    void** provctx)
{
    (void)in;
    *out = s_providerFunctions;
    *provctx = (void*)handle;
    return 1;
}

int32_t NetWasmCryptoNative_InstallSeedSource(void)
{
    // Loading a provider explicitly stops OpenSSL from loading the default one on demand, so both are loaded.
    if (!OSSL_PROVIDER_add_builtin(NULL, NETWASM_SEED_PROVIDER, SeedProviderInit) ||
        OSSL_PROVIDER_load(NULL, NETWASM_SEED_PROVIDER) == NULL ||
        OSSL_PROVIDER_load(NULL, "default") == NULL ||
        !RAND_set_seed_source_type(NULL, NETWASM_SEED_ALGORITHM, NULL))
    {
        return 0;
    }
    return 1;
}
