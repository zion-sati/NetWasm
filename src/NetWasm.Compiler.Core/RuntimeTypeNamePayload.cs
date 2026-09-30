using System;

namespace NetWasm.Compiler.Core;

[Flags]
public enum RuntimeTypeNamePayload
{
    None = 0,
    Name = 1,
    Namespace = 2,
    FullName = 4,
    DisplayName = 8,
}
