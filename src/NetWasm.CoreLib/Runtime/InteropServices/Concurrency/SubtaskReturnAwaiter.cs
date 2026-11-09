namespace System.Runtime.InteropServices.Concurrency;

// An async-lowered call returns its subtask's state in the low 4 bits and,
// unless it has already returned, the subtask's handle above them. Each
// subtask event's payload is the subtask's new state.
internal sealed class SubtaskReturnAwaiter : ISubtaskReturnAwaiter
{
    private const int Starting = 0;
    private const int Started = 1;
    private const int Returned = 2;

    private readonly IWaitableEventAwaiter _awaiter;
    private readonly IWasiSubtaskDropInvoker _drop;

    internal SubtaskReturnAwaiter(IWaitableEventAwaiter awaiter, IWasiSubtaskDropInvoker drop)
    {
        _awaiter = awaiter ?? throw new ArgumentNullException();
        _drop = drop ?? throw new ArgumentNullException();
    }

    public void AwaitReturn(int status)
    {
        var state = status & 0xF;
        if (state == Returned)
        {
            return;
        }

        if (state is not (Starting or Started))
        {
            throw new InvalidOperationException("An async-lowered call ended without returning.");
        }

        var subtask = status >> 4;
        while (state != Returned)
        {
            state = unchecked((int)_awaiter.Await(subtask));
            if (state is not (Started or Returned))
            {
                throw new InvalidOperationException("An async-lowered call ended without returning.");
            }
        }

        _drop.Drop(subtask);
    }
}
