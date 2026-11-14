using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

/// <summary>Selects the value a method returns under the project's folded feature switches, or null.</summary>
internal interface IFeatureSwitchValueSelector
{
    bool? Select(MethodDefinitionModel method);
}
