using NetWasm.Wit.Bindings.TypeDefinitions;
using Xunit;

namespace NetWasm.Wit.Bindings.Tests;

public sealed class WitBindingQualifiedFunctionNameTests
{
    [Fact]
    public void FormatDistinguishesResourceFunctionsWithTheSamePublicMethodName()
    {
        var formatter = new IWitBindingSyntaxFormatter[]
        {
            new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier()),
        }[0];
        const string firstIdentity = "[method]fields.get";
        const string secondIdentity = "[method]trailers.get";

        var firstPublicName = formatter.Format(new WitBindingSyntaxRequest.FunctionName(firstIdentity));
        var secondPublicName = formatter.Format(new WitBindingSyntaxRequest.FunctionName(secondIdentity));
        var firstHelperName = formatter.Format(
            new WitBindingSyntaxRequest.QualifiedFunctionName("wasi-http-types", firstIdentity));
        var secondHelperName = formatter.Format(
            new WitBindingSyntaxRequest.QualifiedFunctionName("wasi-http-types", secondIdentity));

        Assert.Equal(firstPublicName, secondPublicName);
        Assert.NotEqual(firstHelperName, secondHelperName);
    }
}
