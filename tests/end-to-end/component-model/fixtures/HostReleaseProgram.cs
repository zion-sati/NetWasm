// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

// Does through WASI, many times over, what a long-running program does: reads files, writes to a file,
// cancels timers it no longer needs and opens files. The host must release what each finished call held:
// the streams and futures of a read or a write, the copy buffers, a cancelled wait's timer and every
// resource the program has dropped. Then it waits on the host many times, and each wait must end when the host
// is done, not at the runtime's next idle poll. Each phase prints what it did, so a host that fails shows where.
public static class Program
{
    public static int Main()
    {
        var read = 0L;
        for (var pass = 0; pass < 100; pass++)
        {
            read += File.ReadAllBytes("/data/data.bin").Length;
        }
        Console.WriteLine($"read {read}");

        var written = 0L;
        var chunk = new byte[4096];
        using (var stream = File.OpenWrite("/data/written.bin"))
        {
            for (var write = 0; write < 100_000; write++)
            {
                stream.Write(chunk, 0, chunk.Length);
                written += chunk.Length;
            }
        }
        Console.WriteLine($"written {written}");

        var cancelled = 0;
        for (var wait = 0; wait < 50_000; wait++)
        {
            using var source = new CancellationTokenSource();
            var delay = Task.Delay(100_000, source.Token);
            source.Cancel();
            if (delay.IsCanceled) cancelled++;
            // Yields to the host, which disposes the waits the program cancelled.
            if (wait % 100 == 99) Thread.Sleep(1);
        }
        Console.WriteLine($"cancelled {cancelled}");

        var opened = 0;
        for (var open = 0; open < 1000; open++)
        {
            using var file = File.OpenRead("/data/data.bin");
            opened++;
        }
        Console.WriteLine($"opened {opened}");

        // Each sleep waits on the host; the guest resumes as soon as the wait ends, not at the
        // runtime's next idle poll.
        for (var sleep = 0; sleep < 200; sleep++)
        {
            Thread.Sleep(1);
        }
        Console.WriteLine("slept 200");
        return 0;
    }
}
