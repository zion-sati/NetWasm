namespace NetWasm.Compiler.ComponentModel;

public interface IConfiguredExternalToolRunner
{
    ToolResult Run(ExternalToolInvocation invocation);
}
