namespace System.Runtime.InteropServices.TimeZones
{
    using System.Runtime.InteropServices.WebAssembly;

    internal sealed class EnvironmentVariableReader(
        IProcessEnvironment source) : IEnvironmentVariableReader
    {
        private readonly IProcessEnvironment _source =
            source ?? throw new ArgumentNullException();

        public string? Read(string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException();
            }
            string? selected = null;
            foreach (var variable in _source.GetVariables())
            {
                if (variable.Key != name)
                {
                    continue;
                }
                if (selected != null)
                {
                    throw new PlatformNotSupportedException(
                        "WASI environment contains duplicate '" + name + "' entries.");
                }
                selected = variable.Value;
            }
            return selected;
        }
    }
}
