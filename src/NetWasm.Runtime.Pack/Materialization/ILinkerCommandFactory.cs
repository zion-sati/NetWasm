using System.Collections.Immutable;

namespace NetWasm.Runtime.Pack.Materialization;

internal interface ILinkerCommandFactory
{
    RuntimeCommand Create(string linkerPath, ImmutableArray<string> arguments, string logPath);
}
