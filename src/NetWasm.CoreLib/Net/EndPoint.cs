// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptation: invariant resources and ordinary NotImplementedException.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace System.Net
{
    /// <devdoc>
    ///    <para>
    ///       Identifies a network address.
    ///    </para>
    /// </devdoc>
    public abstract class EndPoint
    {
        /// <devdoc>
        ///    <para>
        ///       Returns the Address Family to which the EndPoint belongs.
        ///    </para>
        /// </devdoc>
        public virtual AddressFamily AddressFamily
        {
            get
            {
                throw new NotImplementedException("This property is not implemented by this class.");
            }
        }

        /// <devdoc>
        ///    <para>
        ///       Serializes EndPoint information into a SocketAddress structure.
        ///    </para>
        /// </devdoc>
        public virtual SocketAddress Serialize()
        {
            throw new NotImplementedException("This method is not implemented by this class.");
        }

        /// <devdoc>
        ///    <para>
        ///       Creates an EndPoint instance from a SocketAddress structure.
        ///    </para>
        /// </devdoc>
        public virtual EndPoint Create(SocketAddress socketAddress)
        {
            throw new NotImplementedException("This method is not implemented by this class.");
        }
    }
}
