using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class LinkedCorpusObservationResponseReaderTests
{
    [Fact]
    public void ReadForwardsExactFileTextAndArguments()
    {
        var directory = Directory.CreateTempSubdirectory("netwasm-linked-response-");
        try
        {
            var path = Path.Combine(directory.FullName, "response.json");
            File.WriteAllText(path, "response-text");
            var parser = new RecordingParser();
            var reader = Assert.IsAssignableFrom<ILinkedCorpusObservationResponseReader>(
                new LinkedCorpusObservationResponseReader(parser));
            var request = LinkedCorpusObservationResponseParserTests.CreateRequest();
            var typeNames = ImmutableDictionary<int, string>.Empty.Add(1, "Type");

            var result = reader.Read(path, request, typeNames);

            Assert.Same(parser.Result, result);
            Assert.Equal("response-text", parser.Json);
            Assert.Same(request, parser.Request);
            Assert.Same(typeNames, parser.TypeNames);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void ReadRejectsInvalidPathAndPreservesFileAndParserFailures()
    {
        var parser = new RecordingParser();
        var reader = Assert.IsAssignableFrom<ILinkedCorpusObservationResponseReader>(
            new LinkedCorpusObservationResponseReader(parser));
        var request = LinkedCorpusObservationResponseParserTests.CreateRequest();
        Assert.Throws<ArgumentNullException>(() => reader.Read(null!, request, ImmutableDictionary<int, string>.Empty));
        Assert.Throws<ArgumentException>(() => reader.Read(" ", request, ImmutableDictionary<int, string>.Empty));
        Assert.Throws<FileNotFoundException>(() => reader.Read("missing-response.json", request, ImmutableDictionary<int, string>.Empty));
        parser.Failure = new InvalidOperationException("parse");
        var directory = Directory.CreateTempSubdirectory("netwasm-linked-response-");
        try
        {
            var path = Path.Combine(directory.FullName, "response.json");
            File.WriteAllText(path, "text");
            var failure = Assert.Throws<InvalidOperationException>(() =>
                reader.Read(path, request, ImmutableDictionary<int, string>.Empty));
            Assert.Same(parser.Failure, failure);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class RecordingParser : ILinkedCorpusObservationResponseParser
    {
        public LinkedCorpusObservationResult Result { get; } = new(
            ImmutableDictionary<int, OracleObservation>.Empty, "module", "manifest");
        public Exception? Failure { get; set; }
        public string? Json { get; private set; }
        public LinkedCorpusObservationRequest? Request { get; private set; }
        public ImmutableDictionary<int, string>? TypeNames { get; private set; }

        public LinkedCorpusObservationResult Parse(
            string json,
            LinkedCorpusObservationRequest request,
            ImmutableDictionary<int, string> typeNames)
        {
            Json = json;
            Request = request;
            TypeNames = typeNames;
            if (Failure is not null) throw Failure;
            return Result;
        }
    }
}
