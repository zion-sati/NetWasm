// Adapted from dotnet/runtime System.Runtime.Versioning.VersioningHelper.
// The upstream implementation is licensed under the MIT license.

using System.Text;

namespace System.Runtime.Versioning
{
    [Flags]
    internal enum SxSRequirements
    {
        None = 0,
        AppDomainID = 0x1,
        ProcessID = 0x2,
        CLRInstanceID = 0x4,
        AssemblyName = 0x8,
        TypeName = 0x10
    }

    public static partial class VersioningHelper
    {
        // These masks depend on the values in ResourceScope.
        private const ResourceScope ResTypeMask =
            ResourceScope.Machine | ResourceScope.Process | ResourceScope.AppDomain | ResourceScope.Library;
        private const ResourceScope VisibilityMask = ResourceScope.Private | ResourceScope.Assembly;

        public static string MakeVersionSafeName(string? name, ResourceScope from, ResourceScope to) =>
            MakeVersionSafeName(name, from, to, type: null);

        public static string MakeVersionSafeName(string? name, ResourceScope from, ResourceScope to, Type? type)
        {
            ResourceScope fromResType = from & ResTypeMask;
            ResourceScope toResType = to & ResTypeMask;
            if (fromResType > toResType)
            {
                throw new ArgumentException(
                    "The source resource scope cannot be broader than the destination scope.", nameof(from));
            }

            SxSRequirements requires = GetRequirements(to, from);

            if ((requires & (SxSRequirements.AssemblyName | SxSRequirements.TypeName)) != 0 && type is null)
            {
                throw new ArgumentNullException(nameof(type), "A type is required by the resource scope.");
            }

            // NetWasm does not deploy process, AppDomain, assembly, or reflection metadata. Returning a
            // name without those components would silently remove the side-by-side isolation guarantees of
            // the API, so preserve the contract by failing deterministically when identity is required.
            if (requires != SxSRequirements.None)
            {
                throw new PlatformNotSupportedException(
                    "Version-safe names requiring process, AppDomain, CLR, type, or assembly identity are unavailable.");
            }

            return new StringBuilder(name).ToString();
        }

        private static SxSRequirements GetRequirements(ResourceScope consumeAsScope, ResourceScope calleeScope)
        {
            SxSRequirements requires = SxSRequirements.None;

            switch (calleeScope & ResTypeMask)
            {
                case ResourceScope.Machine:
                    switch (consumeAsScope & ResTypeMask)
                    {
                        case ResourceScope.Machine:
                            break;
                        case ResourceScope.Process:
                            requires |= SxSRequirements.ProcessID;
                            break;
                        case ResourceScope.AppDomain:
                            requires |= SxSRequirements.AppDomainID |
                                        SxSRequirements.CLRInstanceID |
                                        SxSRequirements.ProcessID;
                            break;
                        default:
                            throw new ArgumentException(
                                "The resource scope contains invalid type bits.", nameof(consumeAsScope));
                    }
                    break;

                case ResourceScope.Process:
                    if ((consumeAsScope & ResourceScope.AppDomain) != 0)
                    {
                        requires |= SxSRequirements.AppDomainID | SxSRequirements.CLRInstanceID;
                    }
                    break;

                case ResourceScope.AppDomain:
                    break;

                default:
                    throw new ArgumentException(
                        "The resource scope contains invalid type bits.", nameof(calleeScope));
            }

            switch (calleeScope & VisibilityMask)
            {
                case ResourceScope.None:
                    switch (consumeAsScope & VisibilityMask)
                    {
                        case ResourceScope.None:
                            break;
                        case ResourceScope.Assembly:
                            requires |= SxSRequirements.AssemblyName;
                            break;
                        case ResourceScope.Private:
                            requires |= SxSRequirements.TypeName | SxSRequirements.AssemblyName;
                            break;
                        default:
                            throw new ArgumentException(
                                "The resource scope contains invalid visibility bits.", nameof(consumeAsScope));
                    }
                    break;

                case ResourceScope.Assembly:
                    if ((consumeAsScope & ResourceScope.Private) != 0)
                    {
                        requires |= SxSRequirements.TypeName;
                    }
                    break;

                case ResourceScope.Private:
                    break;

                default:
                    throw new ArgumentException(
                        "The resource scope contains invalid visibility bits.", nameof(calleeScope));
            }

            return requires;
        }
    }
}
