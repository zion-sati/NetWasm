using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.CoreLib.Tests;

// PosixSignal, PosixSignalContext and PosixSignalRegistration exist on NetWasm as upstream's browser and WASI builds
// have them: registering a handler throws PlatformNotSupportedException, which code such as TUnit's engine catches.
// Assembly.IsCollectible has upstream's base value.
public sealed class PosixSignalAndAssemblyContractTests
{
    [Test]
    public async Task RegisteringASignalHandlerThrowsOnNetWasm()
    {
        await Assert.That(ThrowsArgumentNull(() => PosixSignalRegistration.Create(PosixSignal.SIGCONT, null!))).IsTrue();
#if NETWASM
        await Assert.That(ThrowsPlatformNotSupported(() => PosixSignalRegistration.Create(PosixSignal.SIGCONT, _ => { }))).IsTrue();
#else
        using var registration = PosixSignalRegistration.Create(PosixSignal.SIGCONT, _ => { });
        await Assert.That(registration).IsNotNull();
#endif
    }

    [Test]
    public async Task SignalContextCarriesItsSignalAndCancellation()
    {
        var context = new PosixSignalContext(PosixSignal.SIGTERM);
        await Assert.That(context.Signal).IsEqualTo(PosixSignal.SIGTERM);
        await Assert.That(context.Cancel).IsFalse();
        context.Cancel = true;
        await Assert.That(context.Cancel).IsTrue();
        await Assert.That((int)context.Signal).IsEqualTo(-4);
    }

    [Test]
    public async Task AnAssemblySubclassIsCollectibleByDefault()
    {
        await Assert.That(new CustomAssembly().IsCollectible).IsTrue();
    }

    private sealed class CustomAssembly : Assembly;

    private static bool ThrowsArgumentNull(Action action)
    {
        try { action(); return false; }
        catch (ArgumentNullException) { return true; }
    }

    private static bool ThrowsPlatformNotSupported(Action action)
    {
        try { action(); return false; }
        catch (PlatformNotSupportedException) { return true; }
    }
}
