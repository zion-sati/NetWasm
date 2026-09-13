namespace NetWasm.Runtime.Pack.Materialization;

internal interface ICommandInvoker
{
    void Invoke(RuntimeCommand command);
}
