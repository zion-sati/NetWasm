using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace NetWasm.Compiler.ComponentModel.Raw;

// The JavaScript string and name-list literals the canonical binding emitter
// writes. The compiler also runs without reflection, in the browser.
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(string[]))]
[ExcludeFromCodeCoverage]
internal sealed partial class RawCanonicalBindingJsonContext : JsonSerializerContext;
