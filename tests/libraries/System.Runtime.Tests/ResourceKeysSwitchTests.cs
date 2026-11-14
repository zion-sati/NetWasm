using System;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.CoreLib.Tests;

// NetWasm deploys no manifest resources, so the NetWasm SDK defaults System.Resources.UseSystemResourceKeys to true,
// as Blazor WebAssembly's SDK does, and AppContext reads the project's value: libraries' resource lookups then answer
// with their keys instead of throwing PlatformNotSupportedException from ResourceManager. Desktop .NET leaves it unset.
public sealed class ResourceKeysSwitchTests
{
    private const string Switch = "System.Resources.UseSystemResourceKeys";

    [Test]
    public async Task UseSystemResourceKeysDefaultsOnNetWasmOnly()
    {
        var found = AppContext.TryGetSwitch(Switch, out var enabled);
#if NETWASM
        await Assert.That(found).IsTrue();
        await Assert.That(enabled).IsTrue();
#else
        await Assert.That(found).IsFalse();
#endif
    }

#if NETWASM
    [Test]
    public async Task AnAppCanStillTurnResourceKeysOff()
    {
        AppContext.SetSwitch(Switch, false);
        try
        {
            await Assert.That(AppContext.TryGetSwitch(Switch, out var enabled)).IsTrue();
            await Assert.That(enabled).IsFalse();
        }
        finally
        {
            AppContext.SetSwitch(Switch, true);
        }
    }
#endif
}
