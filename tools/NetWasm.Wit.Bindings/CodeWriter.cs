using System;
using System.Text;

namespace NetWasm.Wit.Bindings;

internal enum CodeWriterCommandKind
{
    Indent,
    Unindent,
    Line,
    Raw,
}

internal readonly record struct CodeWriterCommand(
    CodeWriterCommandKind Kind,
    string Value = "");

internal interface ICodeWriter
{
    void Write(CodeWriterCommand command);
    string Text { get; }
}

public interface ICodeWriterFactory
{
    CodeWriter Create();
}

public sealed class CodeWriterFactory : ICodeWriterFactory
{
    public CodeWriter Create() => new();
}

public sealed class CodeWriter : ICodeWriter
{
    private readonly StringBuilder _builder = new();
    private int _indent;

    internal void Indent() => Write(new(CodeWriterCommandKind.Indent));

    internal void Unindent() => Write(new(CodeWriterCommandKind.Unindent));

    internal void Line(string value = "") =>
        Write(new(CodeWriterCommandKind.Line, value));

    internal void Raw(string value) =>
        Write(new(CodeWriterCommandKind.Raw, value));

    public string Text => _builder.ToString();

    internal void Write(CodeWriterCommand command)
    {
        switch (command.Kind)
        {
            case CodeWriterCommandKind.Indent:
                _indent++;
                break;
            case CodeWriterCommandKind.Unindent:
                if (_indent == 0)
                {
                    throw new InvalidOperationException(
                        "code writer indentation cannot be negative");
                }
                _indent--;
                break;
            case CodeWriterCommandKind.Line:
                if (command.Value.Length != 0)
                {
                    _builder.Append(' ', _indent * 4);
                }
                _builder.Append(command.Value).Append('\n');
                break;
            case CodeWriterCommandKind.Raw:
                _builder.Append(command.Value);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command));
        }
    }

    void ICodeWriter.Write(CodeWriterCommand command) => Write(command);

    string ICodeWriter.Text => Text;
}
