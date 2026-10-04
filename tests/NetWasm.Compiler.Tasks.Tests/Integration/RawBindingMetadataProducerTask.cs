using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace NetWasm.Compiler.Tasks.Tests.Integration;

// Substitute the binding mechanism only. Actual production target scheduling
// and computed output propagation remain under test, without native tools.
public sealed class RawBindingMetadataProducerTask : Microsoft.Build.Utilities.Task
{
    public string AdapterPath { get; set; } = string.Empty;

    [Output]
    public ITaskItem[] Adapters { get; private set; } = [];

    [Output]
    public ITaskItem[] RequiredImports { get; private set; } = [];

    [Output]
    public ITaskItem[] RequiredImportModules { get; private set; } = [];

    public override bool Execute()
    {
        File.WriteAllText(AdapterPath, "retained adapter");
        var function = new TaskItem("sample:api/math@1/sum");
        function.SetMetadata("Interface", "sample:api/math@1");
        function.SetMetadata("Name", "sum");
        function.SetMetadata("Parameters", "[{\"name\":\"value\",\"type\":\"u32\"}]");
        function.SetMetadata("Results", "[\"u32\"]");
        Adapters = [new TaskItem(AdapterPath)];
        RequiredImports = [function];
        RequiredImportModules = [new TaskItem("sample:api/math@1")];
        return true;
    }
}
