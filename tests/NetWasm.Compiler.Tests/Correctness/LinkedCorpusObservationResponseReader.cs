using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal interface ILinkedCorpusObservationResponseReader
{
    LinkedCorpusObservationResult Read(
        string path,
        LinkedCorpusObservationRequest request,
        ImmutableDictionary<int, string> typeNames);
}

internal sealed class LinkedCorpusObservationResponseReader(
    ILinkedCorpusObservationResponseParser parser) : ILinkedCorpusObservationResponseReader
{
    public LinkedCorpusObservationResult Read(
        string path,
        LinkedCorpusObservationRequest request,
        ImmutableDictionary<int, string> typeNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return parser.Parse(File.ReadAllText(path), request, typeNames);
    }
}
