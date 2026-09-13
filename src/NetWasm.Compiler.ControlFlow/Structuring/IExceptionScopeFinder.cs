namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface IExceptionScopeFinder
{
    ExceptionScope? Find(ExceptionGroupSource container, ExceptionGroupSource candidate);
}
