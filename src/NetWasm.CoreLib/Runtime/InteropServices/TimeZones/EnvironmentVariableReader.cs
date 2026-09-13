namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class EnvironmentVariableReader(
        IEnvironmentVariableSource source) : IEnvironmentVariableReader
    {
        private readonly IEnvironmentVariableSource _source =
            source ?? throw new ArgumentNullException();

        public string? Read(string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException();
            }
            string? selected = null;
            foreach (var variable in _source.Read())
            {
                if (variable.Name != name)
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
