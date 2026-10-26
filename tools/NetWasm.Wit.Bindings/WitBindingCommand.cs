namespace NetWasm.Wit.Bindings;

internal interface IWitBindingCommand
{
    int Run(string[] arguments);
}

internal sealed class WitBindingCommand(
    IWitBindingOptionsReader options,
    IWitInputStager inputs,
    IWitDocumentReader documents,
    IWitCSharpBindingGenerator bindings,
    IWitBindingSourceAccessibilityRewriter accessibility,
    ITextFileWriter textFiles,
    TextWriter standardOutput) : IWitBindingCommand
{
    private readonly IWitBindingOptionsReader _options = options ??
        throw new ArgumentNullException(nameof(options));
    private readonly IWitInputStager _inputs = inputs ??
        throw new ArgumentNullException(nameof(inputs));
    private readonly IWitDocumentReader _documents = documents ??
        throw new ArgumentNullException(nameof(documents));
    private readonly IWitCSharpBindingGenerator _bindings = bindings ??
        throw new ArgumentNullException(nameof(bindings));
    private readonly IWitBindingSourceAccessibilityRewriter _accessibility =
        accessibility ?? throw new ArgumentNullException(nameof(accessibility));
    private readonly ITextFileWriter _textFiles = textFiles ??
        throw new ArgumentNullException(nameof(textFiles));
    private readonly TextWriter _standardOutput = standardOutput ??
        throw new ArgumentNullException(nameof(standardOutput));

    public int Run(string[] arguments)
    {
        var options = _options.Read(arguments);
        WitDocument document;
        using (var input = _inputs.Stage(options.Wit))
        {
            document = _documents.Read(input.Path);
        }
        var world = document.SelectWorld(options.World);
        var source = _accessibility.Rewrite(
            _bindings.Generate(document, world, options.Namespace),
            options.Accessibility);
        if (options.Output == "-")
        {
            _standardOutput.Write(source);
        }
        else
        {
            _textFiles.Write(options.Output, source);
        }
        return 0;
    }
}
