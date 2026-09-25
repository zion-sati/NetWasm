using System.Runtime.InteropServices.WebAssembly;

namespace NetWasm.Acceptance.Components;

public static class XmlQualificationComponent
{
    [WitImport("", "increment")]
    private static extern int Increment(int value);

    [WitExport("", "run")]
    public static int Run(int value) => Increment(value + XmlQualificationCorpus.Execute());
}
