namespace NetWasm.Compiler.ControlFlow;

public interface ILoopConditionChooser
{
    int? Choose(LoopConditionSelection selection);
}
