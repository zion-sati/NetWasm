namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeMemoryLayoutCalculator
{
    RuntimeMemoryLayout Calculate(RuntimeMemoryLayoutRequest request);
}
