using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.PlatformServices;

// The platform a program declares (NetWasmPlatformAttribute): the constructor that replaces
// CoreLib's PlatformServiceBindings.CreatePlatform, and the description, such as
// "'Game.HostPlatform', declared in GameHost", that replaces DescribeDeclaredPlatform in the
// messages of the services it leaves unprovided.
public sealed record DeclaredPlatform(MethodInstanceModel Constructor, string Description);
