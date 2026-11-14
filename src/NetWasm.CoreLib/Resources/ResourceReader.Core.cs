// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptation: custom resource types deserialize through BinaryFormatter, which upstream
// loads by name with UnsafeAccessorType; NetWasm has no BinaryFormatter and cannot load types
// by name. DeserializeObject keeps upstream's three switch checks, then throws
// PlatformNotSupportedException (API policy stream-resource-data).

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;

namespace System.Resources
{
    public partial class ResourceReader
    {
        private readonly bool _permitDeserialization;  // can deserialize BinaryFormatted resources

        internal ResourceReader(Stream stream, Dictionary<string, ResourceLocator> resCache, bool permitDeserialization)
        {
            Debug.Assert(stream != null, "Need a stream!");
            Debug.Assert(stream.CanRead, "Stream should be readable!");
            Debug.Assert(resCache != null, "Need a Dictionary!");

            _resCache = resCache;
            _store = new BinaryReader(stream, Encoding.UTF8);

            _ums = stream as UnmanagedMemoryStream;

            _permitDeserialization = permitDeserialization;

            ReadResources();
        }

        private object DeserializeObject(int typeIndex)
        {
            if (!AllowCustomResourceTypes)
            {
                throw new NotSupportedException(SR.ResourceManager_ReflectionNotAllowed);
            }

            if (!_permitDeserialization)
            {
                throw new NotSupportedException(SR.NotSupported_ResourceObjectSerialization);
            }

            if (!EnableUnsafeBinaryFormatterSerialization)
            {
                throw new NotSupportedException(SR.BinaryFormatter_SerializationDisallowed);
            }

            throw new PlatformNotSupportedException(SR.PlatformNotSupported_ResourceDeserialization);
        }

        [FeatureSwitchDefinition("System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization")]
        private static bool EnableUnsafeBinaryFormatterSerialization { get; } = AppContext.TryGetSwitch("System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization", out bool value)
            ? value
            : false;

        private static bool ValidateReaderType(string readerType)
        {
            return ResourceManager.IsDefaultType(readerType, ResourceManager.ResReaderTypeName);
        }

        public void GetResourceData(string resourceName, out string resourceType, out byte[] resourceData)
        {
            ArgumentNullException.ThrowIfNull(resourceName);

            if (_resCache == null)
                throw new InvalidOperationException(SR.ResourceReaderIsClosed);

            // Get the type information from the data section.  Also,
            // sort all of the data section's indexes to compute length of
            // the serialized data for this type (making sure to subtract
            // off the length of the type code).
            int[] sortedDataPositions = new int[_numResources];
            int dataPos = FindPosForResource(resourceName);
            if (dataPos == -1)
            {
                throw new ArgumentException(SR.Format(SR.Arg_ResourceNameNotExist, resourceName));
            }

            lock (this)
            {
                // Read all the positions of data within the data section.
                for (int i = 0; i < _numResources; i++)
                {
                    _store.BaseStream.Position = _nameSectionOffset + GetNamePosition(i);
                    // Skip over name of resource
                    int numBytesToSkip = _store.Read7BitEncodedInt();
                    if (numBytesToSkip < 0)
                    {
                        throw new FormatException(SR.Format(SR.BadImageFormat_ResourcesNameInvalidOffset, numBytesToSkip));
                    }
                    _store.BaseStream.Position += numBytesToSkip;

                    int dPos = _store.ReadInt32();
                    if (dPos < 0 || dPos >= _store.BaseStream.Length - _dataSectionOffset)
                    {
                        throw new FormatException(SR.Format(SR.BadImageFormat_ResourcesDataInvalidOffset, dPos));
                    }
                    sortedDataPositions[i] = dPos;
                }
                Array.Sort(sortedDataPositions);

                int index = Array.BinarySearch(sortedDataPositions, dataPos);
                Debug.Assert(index >= 0 && index < _numResources, "Couldn't find data position within sorted data positions array!");
                long nextData = (index < _numResources - 1) ? sortedDataPositions[index + 1] + _dataSectionOffset : _store.BaseStream.Length;
                int len = (int)(nextData - (dataPos + _dataSectionOffset));
                Debug.Assert(len >= 0 && len <= (int)_store.BaseStream.Length - dataPos + _dataSectionOffset, "Length was negative or outside the bounds of the file!");

                // Read type code then byte[]
                _store.BaseStream.Position = _dataSectionOffset + dataPos;
                ResourceTypeCode typeCode = (ResourceTypeCode)_store.Read7BitEncodedInt();
                if (typeCode < 0 || typeCode >= ResourceTypeCode.StartOfUserTypes + _typeTable.Length)
                {
                    throw new BadImageFormatException(SR.BadImageFormat_InvalidType);
                }
                resourceType = TypeNameFromTypeCode(typeCode);

                // The length must be adjusted to subtract off the number
                // of bytes in the 7 bit encoded type code.
                len -= (int)(_store.BaseStream.Position - (_dataSectionOffset + dataPos));
                byte[] bytes = _store.ReadBytes(len);
                if (bytes.Length != len)
                    throw new FormatException(SR.BadImageFormat_ResourceNameCorrupted);
                resourceData = bytes;
            }
        }
    }
}
