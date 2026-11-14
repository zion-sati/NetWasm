// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce resources;
// only the strings this namespace references are included.

namespace System.Net.Sockets
{
    internal static class SR
    {
        internal const string Arg_InvalidHandle =
            "Invalid handle.";
        internal const string ArgumentOutOfRange_Bounds_Lower_Upper_Named =
            "Argument '{2}' must be between {0} and {1}.";
        internal const string ArgumentOutOfRange_PathLengthInvalid =
            "The path '{0}' is of an invalid length for use with domain sockets on this platform.  The length must be between 1 and {1} characters, inclusive.";
        internal const string InvalidNullArgument =
            "Null is not a valid value for {0}.";
        internal const string InvalidOperation_BufferNotExplicitArray =
            "This operation may only be performed when the buffer was set using the SetBuffer overload that accepts an array.";
        internal const string InvalidOperation_IncorrectToken =
            "The result of the operation was already consumed and may not be used again.";
        internal const string PlatformNotSupported_IOControl =
            "Socket.IOControl handles Windows-specific control codes and is not supported on this platform.";
        internal const string PlatformNotSupported_IPProtectionLevel =
            "IP protection level cannot be controlled on this platform.";
        internal const string net_InvalidEndPointAddressFamily =
            "The supplied EndPoint of AddressFamily {0} is not valid for this Socket, use {1} instead.";
        internal const string net_ambiguousbuffers =
            "Buffer and BufferList properties cannot both be non-null.";
        internal const string net_buffercounttoosmall =
            "The Buffer space specified by the Count property is insufficient for the AcceptAsync method.";
        internal const string net_invalidAddressList =
            "None of the discovered or specified addresses match the socket address family.";
        internal const string net_invalidversion =
            "This protocol version is not supported.";
        internal const string net_invasync =
            "Cannot block a call on this socket while an earlier asynchronous call is in progress.";
        internal const string net_io_readfailure =
            "Unable to read data from the transport connection: {0}.";
        internal const string net_io_readwritefailure =
            "Unable to transfer data on the transport connection: {0}.";
        internal const string net_io_timeout_use_gt_zero =
            "Timeout can be only be set to 'System.Threading.Timeout.Infinite' or a value > 0.";
        internal const string net_io_writefailure =
            "Unable to write data to the transport connection: {0}.";
        internal const string net_multibuffernotsupported =
            "Multiple buffers cannot be used with this method.";
        internal const string net_noseek =
            "This stream does not support seek operations.";
        internal const string net_notconnected =
            "The operation is not allowed on non-connected sockets.";
        internal const string net_notstream =
            "The operation is not allowed on non-stream oriented sockets.";
        internal const string net_protocol_invalid_family =
            "'{0}' Client can only accept InterNetwork or InterNetworkV6 addresses.";
        internal const string net_protocol_invalid_multicast_family =
            "Multicast family is not the same as the family of the '{0}' Client.";
        internal const string net_readonlystream =
            "The stream does not support writing.";
        internal const string net_socketopinprogress =
            "An asynchronous socket operation is already in progress using this SocketAsyncEventArgs instance.";
        internal const string net_sockets_accept_receive_notsupported =
            "This platform does not support receiving data with Socket.AcceptAsync.  Instead, make a separate call to Socket.ReceiveAsync.";
        internal const string net_sockets_address_small =
            "Provided SocketAddress is too small for given AddressFamily.";
        internal const string net_sockets_blocking =
            "The operation is not allowed on a non-blocking Socket.";
        internal const string net_sockets_connect_multiconnect_notsupported =
            "Sockets on this platform are invalid for use after a failed connection attempt.";
        internal const string net_sockets_disconnectedAccept =
            "Once the socket has been disconnected, you can only accept again asynchronously.  BeginAccept must be called on a thread that won't exit until the operation has been completed.";
        internal const string net_sockets_disconnectedConnect =
            "Once the socket has been disconnected, you can only reconnect again asynchronously, and only to a different EndPoint.  BeginConnect must be called on a thread that won't exit until the operation has been completed.";
        internal const string net_sockets_dualmode_receivefrom_notsupported =
            "This platform does not support packet information for dual-mode sockets.  If packet information is not required, use Socket.Receive.  If packet information is required set Socket.DualMode to false.";
        internal const string net_sockets_duplicateandclose_notsupported =
            "This platform does not support Socket.DuplicateAndClose.  Instead, create a new socket.";
        internal const string net_sockets_empty_select =
            "All lists are either null or empty.";
        internal const string net_sockets_invalid_connect_algorithm =
            "Provided ConnectAlgorithm {0} is not valid.";
        internal const string net_sockets_invalid_dnsendpoint =
            "The parameter {0} must not be of type DnsEndPoint.";
        internal const string net_sockets_invalid_ipaddress_length =
            "The number of specified IP addresses has to be greater than 0.";
        internal const string net_sockets_invalid_optionValue =
            "The specified value is not a valid '{0}'.";
        internal const string net_sockets_invalid_optionValue_all =
            "The specified value is not valid.";
        internal const string net_sockets_mustbind =
            "You must call the Bind method before performing this operation.";
        internal const string net_sockets_mustlisten =
            "You must call the Listen method before performing this operation.";
        internal const string net_sockets_mustnotbebound =
            "The socket must not be bound or connected.";
        internal const string net_sockets_mustnotlisten =
            "You may not perform this operation after calling the Listen method.";
        internal const string net_sockets_namedmustnotbebound =
            "{0}: The socket must not be bound or connected.";
        internal const string net_sockets_select =
            "The operation is not allowed on objects of type {0}. Use only objects of type {1}.";
        internal const string net_sockets_sendpackelement_FileStreamMustBeAsync =
            "The FileStream must have been opened for asynchronous reading and writing.";
        internal const string net_sockets_toolarge_select =
            "The {0} list contains too many items; a maximum of {1} is allowed.";
        internal const string net_sockets_transmitfileoptions_notsupported =
            "This platform does not support TransmitFileOptions other than TransmitFileOptions.UseDefaultWorkerThread.";
        internal const string net_sockets_useblocking =
            "Use the Blocking property to change the status of the Socket.";
        internal const string net_sockets_zerolist =
            "The parameter {0} must contain one or more elements.";
        internal const string net_stopped =
            "Not listening. You must call the Start() method before calling this method.";
        internal const string net_tcplistener_mustbestopped =
            "The TcpListener must not be listening before performing this operation.";
        internal const string net_udpconnected =
            "Cannot send packets to an arbitrary host while connected.";
        internal const string net_writeonlystream =
            "The stream does not support reading.";

        internal static string Format(string format, object? arg0) =>
            string.Format(format, arg0);

        internal static string Format(string format, object? arg0, object? arg1) =>
            string.Format(format, arg0, arg1);

        internal static string Format(string format, object? arg0, object? arg1, object? arg2) =>
            string.Format(format, arg0, arg1, arg2);

        internal static string Format(string format, params object?[] args) =>
            string.Format(format, args);
    }
}
