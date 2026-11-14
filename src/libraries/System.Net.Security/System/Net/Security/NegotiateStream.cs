// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm: the concrete NegotiateStream is deferred (Negotiate/NTLM/Kerberos authentication needs a host security backend that NetWasm does not have).
// Its .NET Standard 2.1 surface comes from the pinned reference source (ref/System.Net.Security.cs):
// constructors validate the inner stream through AuthenticatedStream as upstream's do, then throw
// PlatformNotSupportedException before taking ownership or doing any IO; every other member throws.

using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace System.Net.Security
{
    public partial class NegotiateStream : AuthenticatedStream
    {
        public NegotiateStream(System.IO.Stream innerStream) : base(innerStream, false)
        {
            throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        }
        public NegotiateStream(System.IO.Stream innerStream, bool leaveInnerStreamOpen) : base(innerStream, leaveInnerStreamOpen)
        {
            throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        }
        public override bool CanRead { get => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported); }
        public override bool CanSeek { get => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported); }
        public override bool CanTimeout { get => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported); }
        public override bool CanWrite { get => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported); }
        public virtual System.Security.Principal.TokenImpersonationLevel ImpersonationLevel { get => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported); }
        public override bool IsAuthenticated { get => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported); }
        public override bool IsEncrypted { get => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported); }
        public override bool IsMutuallyAuthenticated { get => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported); }
        public override bool IsServer { get => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported); }
        public override bool IsSigned { get => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported); }
        public override long Length { get => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported); }
        public override long Position { get => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported); set => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported); }
        public override int ReadTimeout { get => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported); set => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported); }
        public virtual System.Security.Principal.IIdentity RemoteIdentity { get => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported); }
        public override int WriteTimeout { get => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported); set => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported); }
        public virtual void AuthenticateAsClient() => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public virtual void AuthenticateAsClient(System.Net.NetworkCredential credential, System.Security.Authentication.ExtendedProtection.ChannelBinding? binding, string targetName) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public virtual void AuthenticateAsClient(System.Net.NetworkCredential credential, System.Security.Authentication.ExtendedProtection.ChannelBinding? binding, string targetName, System.Net.Security.ProtectionLevel requiredProtectionLevel, System.Security.Principal.TokenImpersonationLevel allowedImpersonationLevel) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public virtual void AuthenticateAsClient(System.Net.NetworkCredential credential, string targetName) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public virtual void AuthenticateAsClient(System.Net.NetworkCredential credential, string targetName, System.Net.Security.ProtectionLevel requiredProtectionLevel, System.Security.Principal.TokenImpersonationLevel allowedImpersonationLevel) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public virtual System.Threading.Tasks.Task AuthenticateAsClientAsync() => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public virtual System.Threading.Tasks.Task AuthenticateAsClientAsync(System.Net.NetworkCredential credential, System.Security.Authentication.ExtendedProtection.ChannelBinding? binding, string targetName) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public virtual System.Threading.Tasks.Task AuthenticateAsClientAsync(System.Net.NetworkCredential credential, System.Security.Authentication.ExtendedProtection.ChannelBinding? binding, string targetName, System.Net.Security.ProtectionLevel requiredProtectionLevel, System.Security.Principal.TokenImpersonationLevel allowedImpersonationLevel) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public virtual System.Threading.Tasks.Task AuthenticateAsClientAsync(System.Net.NetworkCredential credential, string targetName) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public virtual System.Threading.Tasks.Task AuthenticateAsClientAsync(System.Net.NetworkCredential credential, string targetName, System.Net.Security.ProtectionLevel requiredProtectionLevel, System.Security.Principal.TokenImpersonationLevel allowedImpersonationLevel) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public virtual void AuthenticateAsServer() => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public virtual void AuthenticateAsServer(System.Net.NetworkCredential credential, System.Net.Security.ProtectionLevel requiredProtectionLevel, System.Security.Principal.TokenImpersonationLevel requiredImpersonationLevel) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public virtual void AuthenticateAsServer(System.Net.NetworkCredential credential, System.Security.Authentication.ExtendedProtection.ExtendedProtectionPolicy? policy, System.Net.Security.ProtectionLevel requiredProtectionLevel, System.Security.Principal.TokenImpersonationLevel requiredImpersonationLevel) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public virtual void AuthenticateAsServer(System.Security.Authentication.ExtendedProtection.ExtendedProtectionPolicy? policy) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public virtual System.Threading.Tasks.Task AuthenticateAsServerAsync() => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public virtual System.Threading.Tasks.Task AuthenticateAsServerAsync(System.Net.NetworkCredential credential, System.Net.Security.ProtectionLevel requiredProtectionLevel, System.Security.Principal.TokenImpersonationLevel requiredImpersonationLevel) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public virtual System.Threading.Tasks.Task AuthenticateAsServerAsync(System.Net.NetworkCredential credential, System.Security.Authentication.ExtendedProtection.ExtendedProtectionPolicy? policy, System.Net.Security.ProtectionLevel requiredProtectionLevel, System.Security.Principal.TokenImpersonationLevel requiredImpersonationLevel) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public virtual System.Threading.Tasks.Task AuthenticateAsServerAsync(System.Security.Authentication.ExtendedProtection.ExtendedProtectionPolicy? policy) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public virtual System.IAsyncResult BeginAuthenticateAsClient(System.AsyncCallback? asyncCallback, object? asyncState) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public virtual System.IAsyncResult BeginAuthenticateAsClient(System.Net.NetworkCredential credential, System.Security.Authentication.ExtendedProtection.ChannelBinding? binding, string targetName, System.AsyncCallback? asyncCallback, object? asyncState) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public virtual System.IAsyncResult BeginAuthenticateAsClient(System.Net.NetworkCredential credential, System.Security.Authentication.ExtendedProtection.ChannelBinding? binding, string targetName, System.Net.Security.ProtectionLevel requiredProtectionLevel, System.Security.Principal.TokenImpersonationLevel allowedImpersonationLevel, System.AsyncCallback? asyncCallback, object? asyncState) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public virtual System.IAsyncResult BeginAuthenticateAsClient(System.Net.NetworkCredential credential, string targetName, System.AsyncCallback? asyncCallback, object? asyncState) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public virtual System.IAsyncResult BeginAuthenticateAsClient(System.Net.NetworkCredential credential, string targetName, System.Net.Security.ProtectionLevel requiredProtectionLevel, System.Security.Principal.TokenImpersonationLevel allowedImpersonationLevel, System.AsyncCallback? asyncCallback, object? asyncState) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public virtual System.IAsyncResult BeginAuthenticateAsServer(System.AsyncCallback? asyncCallback, object? asyncState) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public virtual System.IAsyncResult BeginAuthenticateAsServer(System.Net.NetworkCredential credential, System.Net.Security.ProtectionLevel requiredProtectionLevel, System.Security.Principal.TokenImpersonationLevel requiredImpersonationLevel, System.AsyncCallback? asyncCallback, object? asyncState) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public virtual System.IAsyncResult BeginAuthenticateAsServer(System.Net.NetworkCredential credential, System.Security.Authentication.ExtendedProtection.ExtendedProtectionPolicy? policy, System.Net.Security.ProtectionLevel requiredProtectionLevel, System.Security.Principal.TokenImpersonationLevel requiredImpersonationLevel, System.AsyncCallback? asyncCallback, object? asyncState) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public virtual System.IAsyncResult BeginAuthenticateAsServer(System.Security.Authentication.ExtendedProtection.ExtendedProtectionPolicy? policy, System.AsyncCallback? asyncCallback, object? asyncState) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public override System.IAsyncResult BeginRead(byte[] buffer, int offset, int count, System.AsyncCallback? asyncCallback, object? asyncState) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public override System.IAsyncResult BeginWrite(byte[] buffer, int offset, int count, System.AsyncCallback? asyncCallback, object? asyncState) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        protected override void Dispose(bool disposing) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public override System.Threading.Tasks.ValueTask DisposeAsync() => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public virtual void EndAuthenticateAsClient(System.IAsyncResult asyncResult) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public virtual void EndAuthenticateAsServer(System.IAsyncResult asyncResult) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public override int EndRead(System.IAsyncResult asyncResult) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public override void EndWrite(System.IAsyncResult asyncResult) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public override void Flush() => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public override System.Threading.Tasks.Task FlushAsync(System.Threading.CancellationToken cancellationToken) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public override int Read(byte[] buffer, int offset, int count) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public override long Seek(long offset, System.IO.SeekOrigin origin) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public override void SetLength(long value) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
        public override void Write(byte[] buffer, int offset, int count) => throw new PlatformNotSupportedException(SR.net_negotiate_NotSupported);
    }
}
