namespace NetWasm.Runtime.Pack.Materialization;

internal interface ILinkedMemoryLayoutCalculator
{
    RuntimeLinkedMemoryLayout Calculate(RuntimeMemoryPlan plan, RuntimeLinkedMemoryLayout observed);
}
