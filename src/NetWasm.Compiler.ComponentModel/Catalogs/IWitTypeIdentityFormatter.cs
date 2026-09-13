namespace NetWasm.Compiler.ComponentModel.Catalogs;

public interface IWitTypeIdentityFormatter
{
    string Format(WitDocument document, WitTypeReference reference);
}
