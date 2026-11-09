namespace System.Runtime.InteropServices.Concurrency;

using System.Runtime.InteropServices.WebAssembly;

internal sealed class ComponentBackpressure : IComponentBackpressure
{
    public void Hold(bool held)
    {
        if (held)
        {
            Increment();
        }
        else
        {
            Decrement();
        }
    }

    [ComponentBuiltIn("$root", "[backpressure-inc]")]
    private static extern void Increment();

    [ComponentBuiltIn("$root", "[backpressure-dec]")]
    private static extern void Decrement();
}
