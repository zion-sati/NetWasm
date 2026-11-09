// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Runtime.Versioning;

namespace System
{
    public sealed partial class OperatingSystem : ISerializable, ICloneable
    {
        private readonly Version _version;
        private readonly PlatformID _platform;
        private readonly string? _servicePack;
        private string? _versionString;

        public OperatingSystem(PlatformID platform, Version version) : this(platform, version, null)
        {
        }

        internal OperatingSystem(PlatformID platform, Version version, string? servicePack)
        {
            if (platform < PlatformID.Win32S || platform > PlatformID.Other)
            {
                throw new ArgumentOutOfRangeException(nameof(platform), platform, "The platform value is invalid.");
            }

            ArgumentNullException.ThrowIfNull(version);

            _platform = platform;
            _version = version;
            _servicePack = servicePack;
        }

        [Obsolete("Formatter-based serialization is obsolete.")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            throw new PlatformNotSupportedException("Formatter-based operating system serialization is not supported on NetWasm.");
        }

        public PlatformID Platform => _platform;

        public string ServicePack => _servicePack ?? string.Empty;

        public Version Version => _version;

        public object Clone() => new OperatingSystem(_platform, _version, _servicePack);

        public override string ToString() => VersionString;

        public unsafe string VersionString
        {
            get
            {
                if (_versionString == null)
                {
                    string os;
                    switch (_platform)
                    {
                        case PlatformID.Win32S: os = "Microsoft Win32S "; break;
                        case PlatformID.Win32Windows: os = (_version.Major > 4 || (_version.Major == 4 && _version.Minor > 0)) ? "Microsoft Windows 98 " : "Microsoft Windows 95 "; break;
                        case PlatformID.Win32NT: os = "Microsoft Windows NT "; break;
                        case PlatformID.WinCE: os = "Microsoft Windows CE "; break;
                        case PlatformID.Unix: os = "Unix "; break;
                        case PlatformID.Xbox: os = "Xbox "; break;
                        case PlatformID.MacOSX: os = "Mac OS X "; break;
                        case PlatformID.Other: os = "Other "; break;
                        default:
                            Debug.Fail($"Unknown platform {_platform}");
                            os = "<unknown> "; break;
                    }

                    // Preserve upstream formatting using the existing string concatenation contract.
                    _versionString = string.IsNullOrEmpty(_servicePack) ?
                        string.Concat(os, _version.ToString()) :
                        string.Concat(os, _version.ToString(3), " ", _servicePack);
                }

                return _versionString;
            }
        }

    }
}
