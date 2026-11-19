// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Upstream's src/libraries/Common/src/SkipLocalsInit.cs (deae2e32bf0e018e09d64597b7c59b62b73bb3ce), which CoreLib
// and every NETCoreApp source library compile: methods carry no .locals init flag, so stackalloc is not zeroed.
[module: System.Runtime.CompilerServices.SkipLocalsInit]
