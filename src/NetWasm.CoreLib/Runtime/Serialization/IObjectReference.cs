// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Runtime.Serialization;

[Obsolete("Formatter-based serialization is obsolete.", DiagnosticId = "SYSLIB0050")]
public interface IObjectReference
{
    object GetRealObject(StreamingContext context);
}
