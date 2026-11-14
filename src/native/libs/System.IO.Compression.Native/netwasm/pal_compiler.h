// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm stand-in for dotnet/runtime's src/native/libs/Common/pal_compiler.h, reduced to what pal_zlib.c uses.
// The archive is linked statically, by symbol, so exported entry points need default visibility only.

#pragma once

#include <stdbool.h>

#define PALEXPORT __attribute__ ((__visibility__ ("default")))
