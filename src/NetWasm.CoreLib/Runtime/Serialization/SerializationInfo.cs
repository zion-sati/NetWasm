// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// API declarations follow NETStandard.Library.Ref 2.1.0.
// Legacy formatter metadata support is unavailable in the current profile.
#pragma warning disable SYSLIB0050

namespace System.Runtime.Serialization;

public sealed class SerializationInfo
{
    public System.Boolean IsAssemblyNameSetExplicit
    {
        get => throw new System.PlatformNotSupportedException("SerializationInfo.IsAssemblyNameSetExplicit requires legacy formatter metadata, which is not available on this NetWasm platform.");
    }

    public System.Boolean IsFullTypeNameSetExplicit
    {
        get => throw new System.PlatformNotSupportedException("SerializationInfo.IsFullTypeNameSetExplicit requires legacy formatter metadata, which is not available on this NetWasm platform.");
    }

    public System.Int32 MemberCount
    {
        get => throw new System.PlatformNotSupportedException("SerializationInfo.MemberCount requires legacy formatter metadata, which is not available on this NetWasm platform.");
    }

    public System.String AssemblyName
    {
        get => throw new System.PlatformNotSupportedException("SerializationInfo.AssemblyName requires legacy formatter metadata, which is not available on this NetWasm platform.");
        set => throw new System.PlatformNotSupportedException("SerializationInfo.AssemblyName requires legacy formatter metadata, which is not available on this NetWasm platform.");
    }

    public System.String FullTypeName
    {
        get => throw new System.PlatformNotSupportedException("SerializationInfo.FullTypeName requires legacy formatter metadata, which is not available on this NetWasm platform.");
        set => throw new System.PlatformNotSupportedException("SerializationInfo.FullTypeName requires legacy formatter metadata, which is not available on this NetWasm platform.");
    }

    public System.Type ObjectType
    {
        get => throw new System.PlatformNotSupportedException("SerializationInfo.ObjectType requires legacy formatter metadata, which is not available on this NetWasm platform.");
    }

    public System.Boolean GetBoolean(System.String @name) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.GetBoolean requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public System.Byte GetByte(System.String @name) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.GetByte requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public System.Char GetChar(System.String @name) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.GetChar requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public System.DateTime GetDateTime(System.String @name) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.GetDateTime requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public System.Decimal GetDecimal(System.String @name) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.GetDecimal requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public System.Double GetDouble(System.String @name) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.GetDouble requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public System.Int16 GetInt16(System.String @name) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.GetInt16 requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public System.Int32 GetInt32(System.String @name) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.GetInt32 requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public System.Int64 GetInt64(System.String @name) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.GetInt64 requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public System.Object GetValue(System.String @name, System.Type @type) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.GetValue requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public System.Runtime.Serialization.SerializationInfoEnumerator GetEnumerator() =>
        throw new System.PlatformNotSupportedException("SerializationInfo.GetEnumerator requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public System.SByte GetSByte(System.String @name) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.GetSByte requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public System.Single GetSingle(System.String @name) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.GetSingle requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public System.String GetString(System.String @name) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.GetString requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public System.UInt16 GetUInt16(System.String @name) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.GetUInt16 requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public System.UInt32 GetUInt32(System.String @name) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.GetUInt32 requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public System.UInt64 GetUInt64(System.String @name) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.GetUInt64 requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public SerializationInfo(System.Type @type, System.Runtime.Serialization.IFormatterConverter @converter) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.SerializationInfo requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public SerializationInfo(System.Type @type, System.Runtime.Serialization.IFormatterConverter @converter, System.Boolean @requireSameTokenInPartialTrust) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.SerializationInfo requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public void AddValue(System.String @name, System.Boolean @value) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.AddValue requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public void AddValue(System.String @name, System.Byte @value) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.AddValue requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public void AddValue(System.String @name, System.Char @value) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.AddValue requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public void AddValue(System.String @name, System.DateTime @value) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.AddValue requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public void AddValue(System.String @name, System.Decimal @value) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.AddValue requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public void AddValue(System.String @name, System.Double @value) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.AddValue requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public void AddValue(System.String @name, System.Int16 @value) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.AddValue requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public void AddValue(System.String @name, System.Int32 @value) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.AddValue requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public void AddValue(System.String @name, System.Int64 @value) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.AddValue requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public void AddValue(System.String @name, System.Object? @value) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.AddValue requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public void AddValue(System.String @name, System.Object? @value, System.Type @type) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.AddValue requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public void AddValue(System.String @name, System.SByte @value) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.AddValue requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public void AddValue(System.String @name, System.Single @value) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.AddValue requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public void AddValue(System.String @name, System.UInt16 @value) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.AddValue requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public void AddValue(System.String @name, System.UInt32 @value) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.AddValue requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public void AddValue(System.String @name, System.UInt64 @value) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.AddValue requires legacy formatter metadata, which is not available on this NetWasm platform.");

    public void SetType(System.Type @type) =>
        throw new System.PlatformNotSupportedException("SerializationInfo.SetType requires legacy formatter metadata, which is not available on this NetWasm platform.");

}
