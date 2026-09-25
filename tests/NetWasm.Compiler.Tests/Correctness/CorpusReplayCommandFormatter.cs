using System.Text.RegularExpressions;

namespace NetWasm.Compiler.Tests.Correctness;

internal interface ICorpusReplayCommandFormatter
{
    string? Format(string? testMethod, string? cellId = null, int? input = null, CorpusMatrixProfile? profile = null);
}

internal sealed partial class CorpusReplayCommandFormatter : ICorpusReplayCommandFormatter
{
    public string? Format(string? testMethod, string? cellId = null, int? input = null, CorpusMatrixProfile? profile = null)
    {
        if (testMethod is null)
        {
            return null;
        }
        if (!QualifiedMethodName().IsMatch(testMethod))
        {
            throw new ArgumentException("Replay requires a qualified test-method identifier.", nameof(testMethod));
        }
        if (profile is { } selectedProfile && !Enum.IsDefined(selectedProfile))
        {
            throw new ArgumentOutOfRangeException(nameof(profile));
        }
        var filter = $"FullyQualifiedName={testMethod}";
        if (cellId is not null)
        {
            if (!MatrixCellName().IsMatch(cellId))
            {
                throw new ArgumentException("Replay requires an exact matrix-cell identifier.", nameof(cellId));
            }
            filter += $"&DisplayName~{cellId}&DisplayName!~{cellId}-";
        }
        if (input is { } selectedInput)
        {
            filter += "&DisplayName~input: " + selectedInput.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",";
        }
        var environment = profile is { } namedProfile ? $" --environment NETWASM_CORPUS_PROFILE={namedProfile}" : string.Empty;
        return "dotnet test tests/NetWasm.Compiler.Tests/NetWasm.Compiler.Tests.csproj " +
            $"-c Release{environment} --filter '{filter}'";
    }

    [GeneratedRegex(@"\A[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)+\z", RegexOptions.CultureInvariant)]
    private static partial Regex QualifiedMethodName();

    [GeneratedRegex(@"\A(?:Debug|Release|Emitted)-Wasm(?:32|64)-(?:Direct|Optimized)(?:-Linked)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex MatrixCellName();
}
