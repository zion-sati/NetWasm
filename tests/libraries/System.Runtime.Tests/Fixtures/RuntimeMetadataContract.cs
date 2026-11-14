// From the compiler harness's RuntimeMetadataContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
namespace RuntimeMetadataFixture;
public static class EntryPoint
{
    [STAThread]
    [LoaderOptimization(LoaderOptimization.SingleDomain)]
    public static int Run(int input)
    {
        Attribute[] attributes = { new STAThreadAttribute(), new MTAThreadAttribute(), new ContextStaticAttribute() };
        if (attributes.Length != 3 || !(attributes[0] is STAThreadAttribute) || !(attributes[1] is MTAThreadAttribute) || !(attributes[2] is ContextStaticAttribute)) return 1;
        for (var value = 0; value < 256; value++)
            if ((int)new LoaderOptimizationAttribute((byte)value).Value != value) return 2;
        if ((int)new LoaderOptimizationAttribute((LoaderOptimization)257).Value != 1 || (int)new LoaderOptimizationAttribute((LoaderOptimization)(-1)).Value != 255) return 3;
        if ((int)LoaderOptimization.NotSpecified != 0 || (int)LoaderOptimization.SingleDomain != 1 || (int)LoaderOptimization.MultiDomain != 2 || (int)LoaderOptimization.MultiDomainHost != 3) return 4;
        var instance = new LocalObject();
        if (!(instance is MarshalByRefObject) || !(instance is ContextBoundObject) || instance.Value() != 42) return 5;
        return input + 1;
    }
    private sealed class LocalObject : ContextBoundObject
    {
        public int Value() => 42;
    }
}
