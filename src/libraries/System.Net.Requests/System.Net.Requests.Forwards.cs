// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The public types System.Net.Requests forwards in Microsoft.NETCore.App.Ref 11.0.0 that NetWasm defines in System.Net.WebHeaderCollection, forwarded
// there as upstream's assembly forwards them, so a package built against a contract that declared them here binds.

[assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(global::System.Net.HttpRequestHeader))]
[assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(global::System.Net.WebHeaderCollection))]
