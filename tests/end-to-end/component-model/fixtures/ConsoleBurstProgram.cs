using System;

// Writes many lines, then returns at once.
public static class Program
{
    public static int Main()
    {
        for (var line = 0; line < 2000; line++)
        {
            Console.WriteLine("line " + line);
        }

        return 0;
    }
}
