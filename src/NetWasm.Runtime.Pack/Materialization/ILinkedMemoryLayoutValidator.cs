namespace NetWasm.Runtime.Pack.Materialization;

internal interface ILinkedMemoryLayoutValidator
{
    void Validate(RuntimeMemoryPlan plan, RuntimeLinkedMemoryLayout observed);
    void Validate(RuntimeMemoryPlan plan, RuntimeLinkedMemoryLayout observed, RuntimeLinkedMemoryLayout expected);
}
