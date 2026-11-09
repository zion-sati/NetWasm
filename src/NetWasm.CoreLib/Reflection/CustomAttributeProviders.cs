// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace System.Reflection;

// Preserve upstream virtual forwarding while runtime discovery remains unavailable.
public abstract partial class Assembly
{
    public virtual IEnumerable<CustomAttributeData> CustomAttributes => GetCustomAttributesData();
    public virtual IList<CustomAttributeData> GetCustomAttributesData() =>
        throw new PlatformNotSupportedException("Runtime assembly custom attribute metadata is not deployed on NetWasm.");
}

public abstract partial class Module
{
    public virtual IEnumerable<CustomAttributeData> CustomAttributes => GetCustomAttributesData();
    public virtual IList<CustomAttributeData> GetCustomAttributesData() =>
        throw new PlatformNotSupportedException("Runtime module custom attribute metadata is not deployed on NetWasm.");
}
