// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptation: use the invariant upstream remoting resource text.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace System
{
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    [ComVisible(true)]
    public abstract class MarshalByRefObject
    {
        protected MarshalByRefObject()
        {
        }

        [Obsolete(Obsoletions.RemotingApisMessage, DiagnosticId = Obsoletions.RemotingApisDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public object GetLifetimeService()
        {
            throw new PlatformNotSupportedException("Remoting is not supported on this platform.");
        }

        [Obsolete(Obsoletions.RemotingApisMessage, DiagnosticId = Obsoletions.RemotingApisDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public virtual object InitializeLifetimeService()
        {
            throw new PlatformNotSupportedException("Remoting is not supported on this platform.");
        }

        protected MarshalByRefObject MemberwiseClone(bool cloneIdentity)
        {
            MarshalByRefObject mbr = (MarshalByRefObject)MemberwiseClone();
            return mbr;
        }
    }
}
