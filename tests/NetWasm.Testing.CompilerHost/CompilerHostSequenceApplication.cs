using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Testing.CompilerHost;

internal sealed class CompilerHostSequenceApplication(
    ICompilerHostRequestRunner requests)
{
    private static readonly JsonSerializerOptions ReceiptJsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly ICompilerHostRequestRunner _requests = requests ??
        throw new ArgumentNullException(nameof(requests));

    public int Run(CompilationSequenceRequest sequence, string receiptPath)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentException.ThrowIfNullOrWhiteSpace(receiptPath);
        var receipts = ImmutableArray.CreateBuilder<CompilationSequenceStepReceipt>();
        foreach (var step in sequence.Steps)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(step.Label);
            var started = Stopwatch.GetTimestamp();
            var exitCode = _requests.Run(step.Request, step.ResponsePath);
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (exitCode != 0)
            {
                var failure = JsonSerializer.Deserialize<FailedCompilationResponse>(
                    File.ReadAllText(step.ResponsePath)) ??
                    throw new InvalidOperationException("compiler failure response was empty");
                receipts.Add(new(
                    step.Label,
                    exitCode,
                    failure.Outcome,
                    elapsed,
                    failure.AdapterDuration,
                    failure.CompilerTiming,
                    null,
                    failure.CompilerMetrics));
                WriteReceipt(receiptPath, receipts);
                return exitCode;
            }
            var response = JsonSerializer.Deserialize<CompilationResponse>(
                File.ReadAllText(step.ResponsePath)) ??
                throw new InvalidOperationException("compiler response was empty");
            receipts.Add(new(
                step.Label,
                0,
                CompilerMetricsOutcome.Succeeded,
                elapsed,
                response.AdapterDuration,
                response.CompilerTiming,
                Convert.ToHexStringLower(SHA256.HashData(
                    File.ReadAllBytes(step.Request.ModulePath))),
                response.CompilerMetrics));
        }
        WriteReceipt(receiptPath, receipts);
        return 0;
    }

    private static void WriteReceipt(
        string receiptPath,
        ImmutableArray<CompilationSequenceStepReceipt>.Builder receipts) =>
        File.WriteAllText(receiptPath, JsonSerializer.Serialize(
            new CompilationSequenceReceipt(2, receipts.ToImmutable()),
            ReceiptJsonOptions) + "\n");
}
