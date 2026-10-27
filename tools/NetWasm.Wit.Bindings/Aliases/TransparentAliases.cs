using System.Linq;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Wit.Bindings.Aliases;

// A type alias has the canonical ABI and the C# type of the type it names, so a
// binding crosses it as that type: an alias of u64 is the cast u64 is, not a
// copy through memory.
internal static class TransparentAliases
{
    public static WitTypeReference Resolve(WitDocument document, WitTypeReference reference)
    {
        while (reference is WitTypeReference.Defined defined &&
               document.Types[defined.Id].Kind is { ValueKind: JsonValueKind.Object } kind &&
               kind.TryGetProperty("type", out var target))
        {
            reference = target.ValueKind == JsonValueKind.String
                ? new WitTypeReference.Primitive(target.GetString()!)
                : new WitTypeReference.Defined(target.GetInt32());
        }
        return reference;
    }

    // The function with each parameter and its result as the types they name.
    public static WitFunction Resolve(WitDocument document, WitFunction function) => function with
    {
        Parameters = [.. function.Parameters.Select(parameter =>
            parameter with { Type = Resolve(document, parameter.Type) })],
        Result = function.Result is null ? null : Resolve(document, function.Result),
    };
}
