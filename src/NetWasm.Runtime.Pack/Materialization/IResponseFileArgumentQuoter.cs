namespace NetWasm.Runtime.Pack.Materialization;

/// <summary>Quotes one argument for a response file, so the tool reads it back exactly.</summary>
internal interface IResponseFileArgumentQuoter
{
    string Quote(string argument);
}
