// From the compiler harness's HostExecutionContextContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Threading;
namespace HostContextFixture;
public static class EntryPoint
{
    public static int Run(int input)
    {
        var state = new object();
        var context = new Context(state);
        if (!ReferenceEquals(context.Value, state)) return 1;
        context.Value = null;
        if (context.Value != null) return 2;
        context.Value = state;
        var copy = context.CreateCopy();
        if (ReferenceEquals(copy, context) || context.Copies != 1) return 3;
        var clone = (Context)copy;
        if (!ReferenceEquals(clone.Value, state)) return 4;
        ((IDisposable)context).Dispose();
        context.Dispose(false);
        if (context.TrueDisposals != 1 || context.FalseDisposals != 1) return 5;
        var basic = new HostExecutionContext(state);
        var basicCopy = basic.CreateCopy();
        if (ReferenceEquals(basic, basicCopy) || basicCopy == null) return 6;
        basic.Dispose(); basic.Dispose(false); basic.Dispose();
        var manager = new HostExecutionContextManager();
        if (manager.Capture() != null) return 7;
        try { manager.SetHostExecutionContext(null!); return 8; } catch (InvalidOperationException) { }
        try { manager.Revert(null!); return 9; } catch (InvalidOperationException) { }
        try { manager.Revert(new object()); return 10; } catch (InvalidOperationException) { }
        var before = ExecutionContext.Capture()!;
        var token = manager.SetHostExecutionContext(basic);
        if (manager.Capture() != null) return 11;
        var isolatedRejected = false;
        ExecutionContext.Run(before, _ => {
            try { manager.Revert(token); } catch (InvalidOperationException) { isolatedRejected = true; }
        }, null);
        if (!isolatedRejected) return 12;
        manager.Revert(token);
        try { manager.Revert(token); return 13; } catch (InvalidOperationException) { }
        var outer = manager.SetHostExecutionContext(basic);
        var inner = manager.SetHostExecutionContext(basicCopy);
        try { manager.Revert(outer); return 14; } catch (InvalidOperationException) { }
        manager.Revert(inner);
        try { manager.Revert(outer); return 15; } catch (InvalidOperationException) { }
        HostExecutionContextManager custom = new Manager();
        var captured = custom.Capture();
        var previous = custom.SetHostExecutionContext(captured!);
        custom.Revert(previous);
        if (((Manager)custom).Calls != 3) return 16;
        return input + 1;
    }
    private sealed class Context : HostExecutionContext
    {
        public Context(object? state) : base(state) { }
        public object? Value { get => State; set => State = value; }
        public int Copies, TrueDisposals, FalseDisposals;
        public override HostExecutionContext CreateCopy() { Copies++; return new Context(State); }
        public override void Dispose(bool disposing) { if (disposing) TrueDisposals++; else FalseDisposals++; }
    }
    private sealed class Manager : HostExecutionContextManager
    {
        public int Calls;
        public override HostExecutionContext Capture() { Calls++; return new HostExecutionContext(); }
        public override object SetHostExecutionContext(HostExecutionContext value) { Calls++; return value; }
        public override void Revert(object previousState) { if (previousState is HostExecutionContext) Calls++; }
    }
}
