namespace NetWasm.Compiler.Cli;

// The option summary csc prints for -help, grouped as csc groups its own.
internal static class NwcHelp
{
    private static readonly string[] Lines =
    [
        "                             NetWasm Compiler Options",
        "",
        "Usage: nwc [options] <input assembly>",
        "       nwc componentize [options]",
        "",
        "                        - OUTPUT FILES -",
        "-out:<file>                   Write the core WebAssembly module to <file>",
        "-target:exe                   Build an executable from the assembly's entry",
        "                              point (default)",
        "-target:library               Build a library",
        "-target:function              Build a module whose entry is one static method",
        "-entry:<type>::<method>       The static method -target:function enters",
        "-export:<name>=<type>::<method>",
        "                              Export a static method as <name>",
        "-interopmanifest:<file>       Write the host interop manifest to <file>",
        "-runtimelayout:<file>         Write the runtime layout to <file>",
        "-compilermetadata:<file>      Write the compiler metadata to <file>",
        "-callbackobject:<file>        Write the native callback object to <file>",
        "-exceptiontypemap:<file>      Write the exception type map to <file>",
        "-stacktracesymbols:<file>     Emit stack traces and write their symbols to <file>",
        "",
        "                        - INPUT FILES -",
        "-reference:<file list>        Reference metadata from the specified assembly",
        "                              files (Short form: -r)",
        "-source:<file>                A source file of the input, for stack traces",
        "-wit:<file>                   The WIT package the application targets",
        "-world:<name>                 The WIT world the application targets",
        "-importmodule:<name>          A module the input's native archives import from",
        "",
        "                        - CODE GENERATION -",
        "-platform:<string>            Limit which platforms this code can run on: wasm32",
        "                              (default) or wasm64",
        "-optimize:<level>             None, O0, O1, O2, O3, Os or Oz (default)",
        "-jsexportboundary[+|-]        Export through the JavaScript boundary",
        "-structureddiagnostics[+|-]   Report unhandled exceptions in structured form",
        "-managed<optimization>:<value>",
        "                              Override one managed optimization, such as",
        "                              -managedmethodinlining:Enabled",
        "",
        "                        - ADVANCED -",
        "-pathmap:<K1>=<V1>,<K2>=<V2>  Specify a mapping for source path names output by",
        "                              the compiler",
        "-projectdirectory:<dir>       The project directory paths are relative to",
        "-intermediatedirectory:<dir>  Cache compiled metadata in <dir>",
        "-diagnostictrace:<file>       Write a compiler trace and its artifacts",
        "-diagnosticlog:<file>         Write the compiler's diagnostic log",
        "-wasmtoolsnode:<file>         Run wasm-tools with this node, with",
        "-wasmtoolscommand:<file>      this command script",
        "-wasmtoolsmodule:<file>       and this wasm-tools module",
        "",
        "                        - COMPONENTIZE -",
        "-coremodule:<file>            The linked core module to package",
        "-runtimemodule:<file>         The runtime module it links with",
        "-wit:<file>                   The WIT package the component implements",
        "-world:<name>                 The WIT world the component implements",
        "-out:<file>                   Write the component to <file>",
        "-manifest:<file>              Write the component manifest to <file>",
        "-platform:<string>            wasm32 (default) or wasm64",
        "-interopmanifest:<file>       Read the JavaScript boundary from <file>",
        "-jcoversion:<version>         Record the jco version in the manifest",
        "-preview2shimversion:<version>",
        "                              Record the preview2-shim version in the manifest",
        "-optimize:<level>             None, O0, O1, O2, O3, Os or Oz (default)",
        "-managedinlininghints:<value> Auto (default), Preserve or IgnoreAggressive",
        "",
        "                        - MISCELLANEOUS -",
        "@<file>                       Read response file for more options",
        "-help                         Display this usage message (Short form: -?)",
        "-nologo                       Suppress compiler copyright message",
    ];

    public static void Write(IConsole console)
    {
        foreach (var line in Lines)
        {
            console.WriteLine(line);
        }
    }
}
