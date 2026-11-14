// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm stand-in for dotnet/runtime's src/native/libs/Common/pal_utilities.h, reduced to what pal_zlib.c uses.

#pragma once

#include "pal_types.h"
#include <assert.h>

#define c_static_assert_msg(e, msg) _Static_assert((e), msg)
#define c_static_assert(e) c_static_assert_msg(e, "")
