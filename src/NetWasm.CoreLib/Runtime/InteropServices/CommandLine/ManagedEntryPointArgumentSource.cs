namespace System.Runtime.InteropServices.CommandLine
{
    using System.Runtime.InteropServices.WebAssembly;

    internal sealed class ManagedEntryPointArgumentSource(
        IProcessEnvironment commandLineArguments) :
        IManagedEntryPointArgumentSource
    {
        private readonly IProcessEnvironment _commandLineArguments =
            commandLineArguments ?? throw new ArgumentNullException();

        public string[] Read()
        {
            var commandLineArguments = _commandLineArguments.GetArguments() ??
                throw new InvalidOperationException("The command line service returned no arguments.");
            if (commandLineArguments.Length <= 1)
            {
                return [];
            }

            var arguments = new string[commandLineArguments.Length - 1];
            for (var index = 0; index < arguments.Length; index++)
            {
                arguments[index] = commandLineArguments[index + 1];
            }
            return arguments;
        }
    }
}
