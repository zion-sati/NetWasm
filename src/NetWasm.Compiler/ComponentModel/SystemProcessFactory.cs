using System.Diagnostics;
using System.Text;
using SystemProcess = System.Diagnostics.Process;

namespace NetWasm.Compiler.ComponentModel;

internal sealed class SystemProcessFactory : IProcessFactory
{
    public IProcessSession Create(string executable) => new SystemProcessSession(new SystemProcess
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        },
    });
}
