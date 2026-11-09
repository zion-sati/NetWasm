// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Data contracts from NETStandard.Library.Ref 2.1.0.

namespace System.Net.Sockets;

public enum AddressFamily
{
    AppleTalk = 16,
    Atm = 22,
    Banyan = 21,
    Ccitt = 10,
    Chaos = 5,
    Cluster = 24,
    ControllerAreaNetwork = 65537,
    DataKit = 9,
    DataLink = 13,
    DecNet = 12,
    Ecma = 8,
    FireFox = 19,
    HyperChannel = 15,
    Ieee12844 = 25,
    ImpLink = 3,
    InterNetwork = 2,
    InterNetworkV6 = 23,
    Ipx = 6,
    Irda = 26,
    Iso = 7,
    Lat = 14,
    Max = 29,
    NS = 6,
    NetBios = 17,
    NetworkDesigners = 28,
    Osi = 7,
    Packet = 65536,
    Pup = 4,
    Sna = 11,
    Unix = 1,
    Unknown = -1,
    Unspecified = 0,
    VoiceView = 18,
}

public enum IPProtectionLevel
{
    EdgeRestricted = 20,
    Restricted = 30,
    Unrestricted = 10,
    Unspecified = -1,
}

public enum ProtocolType
{
    Ggp = 3,
    IP = 0,
    IPSecAuthenticationHeader = 51,
    IPSecEncapsulatingSecurityPayload = 50,
    IPv4 = 4,
    IPv6 = 41,
    IPv6DestinationOptions = 60,
    IPv6FragmentHeader = 44,
    IPv6HopByHopOptions = 0,
    IPv6NoNextHeader = 59,
    IPv6RoutingHeader = 43,
    Icmp = 1,
    IcmpV6 = 58,
    Idp = 22,
    Igmp = 2,
    Ipx = 1000,
    ND = 77,
    Pup = 12,
    Raw = 255,
    Spx = 1256,
    SpxII = 1257,
    Tcp = 6,
    Udp = 17,
    Unknown = -1,
    Unspecified = 0,
}

public enum SelectMode
{
    SelectError = 2,
    SelectRead = 0,
    SelectWrite = 1,
}

public enum SocketAsyncOperation
{
    Accept = 1,
    Connect = 2,
    Disconnect = 3,
    None = 0,
    Receive = 4,
    ReceiveFrom = 5,
    ReceiveMessageFrom = 6,
    Send = 7,
    SendPackets = 8,
    SendTo = 9,
}

public enum SocketError
{
    AccessDenied = 10013,
    AddressAlreadyInUse = 10048,
    AddressFamilyNotSupported = 10047,
    AddressNotAvailable = 10049,
    AlreadyInProgress = 10037,
    ConnectionAborted = 10053,
    ConnectionRefused = 10061,
    ConnectionReset = 10054,
    DestinationAddressRequired = 10039,
    Disconnecting = 10101,
    Fault = 10014,
    HostDown = 10064,
    HostNotFound = 11001,
    HostUnreachable = 10065,
    IOPending = 997,
    InProgress = 10036,
    Interrupted = 10004,
    InvalidArgument = 10022,
    IsConnected = 10056,
    MessageSize = 10040,
    NetworkDown = 10050,
    NetworkReset = 10052,
    NetworkUnreachable = 10051,
    NoBufferSpaceAvailable = 10055,
    NoData = 11004,
    NoRecovery = 11003,
    NotConnected = 10057,
    NotInitialized = 10093,
    NotSocket = 10038,
    OperationAborted = 995,
    OperationNotSupported = 10045,
    ProcessLimit = 10067,
    ProtocolFamilyNotSupported = 10046,
    ProtocolNotSupported = 10043,
    ProtocolOption = 10042,
    ProtocolType = 10041,
    Shutdown = 10058,
    SocketError = -1,
    SocketNotSupported = 10044,
    Success = 0,
    SystemNotReady = 10091,
    TimedOut = 10060,
    TooManyOpenSockets = 10024,
    TryAgain = 11002,
    TypeNotFound = 10109,
    VersionNotSupported = 10092,
    WouldBlock = 10035,
}

[System.Flags]
public enum SocketFlags
{
    Broadcast = 1024,
    ControlDataTruncated = 512,
    DontRoute = 4,
    Multicast = 2048,
    None = 0,
    OutOfBand = 1,
    Partial = 32768,
    Peek = 2,
    Truncated = 256,
}

public enum SocketOptionLevel
{
    IP = 0,
    IPv6 = 41,
    Socket = 65535,
    Tcp = 6,
    Udp = 17,
}

public enum SocketOptionName
{
    AcceptConnection = 2,
    AddMembership = 12,
    AddSourceMembership = 15,
    BlockSource = 17,
    Broadcast = 32,
    BsdUrgent = 2,
    ChecksumCoverage = 20,
    Debug = 1,
    DontFragment = 14,
    DontLinger = -129,
    DontRoute = 16,
    DropMembership = 13,
    DropSourceMembership = 16,
    Error = 4103,
    ExclusiveAddressUse = -5,
    Expedited = 2,
    HeaderIncluded = 2,
    HopLimit = 21,
    IPOptions = 1,
    IPProtectionLevel = 23,
    IPv6Only = 27,
    IpTimeToLive = 4,
    KeepAlive = 8,
    Linger = 128,
    MaxConnections = 2147483647,
    MulticastInterface = 9,
    MulticastLoopback = 11,
    MulticastTimeToLive = 10,
    NoChecksum = 1,
    NoDelay = 1,
    OutOfBandInline = 256,
    PacketInformation = 19,
    ReceiveBuffer = 4098,
    ReceiveLowWater = 4100,
    ReceiveTimeout = 4102,
    ReuseAddress = 4,
    ReuseUnicastPort = 12295,
    SendBuffer = 4097,
    SendLowWater = 4099,
    SendTimeout = 4101,
    Type = 4104,
    TypeOfService = 3,
    UnblockSource = 18,
    UpdateAcceptContext = 28683,
    UpdateConnectContext = 28688,
    UseLoopback = 64,
}

public enum SocketShutdown
{
    Both = 2,
    Receive = 0,
    Send = 1,
}

public enum SocketType
{
    Dgram = 2,
    Raw = 3,
    Rdm = 4,
    Seqpacket = 5,
    Stream = 1,
    Unknown = -1,
}

[System.Flags]
public enum TransmitFileOptions
{
    Disconnect = 1,
    ReuseSocket = 2,
    UseDefaultWorkerThread = 0,
    UseKernelApc = 32,
    UseSystemThread = 16,
    WriteBehind = 4,
}
