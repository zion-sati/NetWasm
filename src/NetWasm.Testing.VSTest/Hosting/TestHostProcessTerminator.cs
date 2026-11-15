// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.ComponentModel;

namespace NetWasm.Testing.VSTest.Hosting;

internal sealed class TestHostProcessTerminator : ITestHostProcessTerminator
{
    public void Terminate(
        Func<bool> hasExited,
        Action kill,
        Action waitForExit)
    {
        ArgumentNullException.ThrowIfNull(hasExited);
        ArgumentNullException.ThrowIfNull(kill);
        ArgumentNullException.ThrowIfNull(waitForExit);
        try
        {
            if (!hasExited())
            {
                kill();
                waitForExit();
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }
}
