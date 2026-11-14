namespace NetWasm.Runtime.Pack.Materialization;

/// <summary>Formats the excerpt of a failed tool's output that its failure message ends with.</summary>
internal interface IToolOutputExcerptFormatter
{
    string Format(string output);
}
