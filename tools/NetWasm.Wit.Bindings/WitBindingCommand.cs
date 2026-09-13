namespace NetWasm.Wit.Bindings;

internal interface IWitBindingCommand
{
    int Run(string[] arguments);
}

internal sealed class WitBindingCommand(
    IWitBindingOptionsReader options,
    IWitDocumentReader documents,
    IWitCSharpBindingGenerator bindings,
    IWitBindingSourceAccessibilityRewriter accessibility,
    ITextFileWriter textFiles) : IWitBindingCommand
{
    private readonly IWitBindingOptionsReader _options = options ??
        throw new ArgumentNullException(nameof(options));
    private readonly IWitDocumentReader _documents = documents ??
        throw new ArgumentNullException(nameof(documents));
    private readonly IWitCSharpBindingGenerator _bindings = bindings ??
        throw new ArgumentNullException(nameof(bindings));
    private readonly IWitBindingSourceAccessibilityRewriter _accessibility =
        accessibility ?? throw new ArgumentNullException(nameof(accessibility));
    private readonly ITextFileWriter _textFiles = textFiles ??
        throw new ArgumentNullException(nameof(textFiles));

    public int Run(string[] arguments)
    {
        var options = _options.Read(arguments);
        var document = _documents.Read(options.Wit);
        var world = document.SelectWorld(options.World);
        var source = _bindings.Generate(document, world);
        _textFiles.Write(
            options.Output,
            _accessibility.Rewrite(source, options.Accessibility));
        return 0;
    }
}
