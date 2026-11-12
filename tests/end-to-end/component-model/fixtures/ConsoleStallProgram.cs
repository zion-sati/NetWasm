using System;

// Writes a line, then computes without ever waiting again.
public static class Program
{
    public static int Main()
    {
        Console.WriteLine("stalled");
        var spins = 0L;
        while (true)
        {
            spins++;
        }
    }
}
