// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

// System.Net.Primitives declares HttpStatusCode, as Microsoft.NETCore.App.Ref does; earlier NetWasm.App releases
// declared it here.
[assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(global::System.Net.HttpStatusCode))]
