using System;
using System.Threading.Tasks;

namespace NetWasm.Fixtures.LibraryProfileEdges;

public static class Program
{
    public static async Task<int> Main()
    {
        var failures = 0;
        for (var input = 0; input < 15; input++)
        {
            Console.WriteLine($"CASE {input} START");
            try
            {
                switch (input)
                {
                    case 0: LifetimeCases.ChangeTokenDisposal(); break;
                    case 1: LifetimeCases.OptionsLifetime(); break;
                    case 2: await LifetimeCases.CacheLifetime(); break;
                    case 3: await HttpCases.ShortReads(false); break;
                    case 4: await HttpCases.ShortReads(true); break;
                    case 5: await HttpCases.PendingReadCancellation(); break;
                    case 6: await HttpCases.MalformedBody(); break;
                    case 7: await HttpCases.MetadataWrites(); break;
                    case 8: await HttpCases.PipelineFailureAndOwnership(); break;
                    case 9: await HttpCases.ContentContextRead(); break;
                    case 10: await PipelineCases.SegmentsAndPartialConsumption(); break;
                    case 11: await PipelineCases.ResumeThreshold(); break;
                    case 12: await PipelineCases.PendingCancellationAndFailure(); break;
                    case 13: await FactoryCases.LiveLeasesAcrossExpiry(); break;
                    case 14: await FactoryCases.NamedPendingCancellation(); break;
                }
                Console.WriteLine($"CASE {input} PASS");
            }
            catch (Exception)
            {
                failures++;
                Console.WriteLine($"CASE {input} FAIL");
            }
        }
        return failures == 0 ? 0 : 1;
    }

    public static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Independent contract failed.");
    }
}
