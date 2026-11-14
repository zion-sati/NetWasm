// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System
{
    public abstract class ContextBoundObject : MarshalByRefObject
    {
        protected ContextBoundObject() { }
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public partial class ContextStaticAttribute : Attribute
    {
        public ContextStaticAttribute() { }
    }
}
