namespace NetWasm.Compiler.Wasm.Emission.Exceptions;

internal interface IExceptionFieldLayoutResolver
{
    int? Resolve(string fieldName);
}
