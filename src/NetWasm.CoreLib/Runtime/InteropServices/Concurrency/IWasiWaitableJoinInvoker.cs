namespace System.Runtime.InteropServices.Concurrency;

internal interface IWasiWaitableJoinInvoker
{
    // Joins the waitable to the set, or removes it from its set when set is 0.
    void Join(int waitable, int set);
}
