namespace NetWasm.TimeZones;

internal interface IPosixRuleParser
{
    PosixFutureRule? Parse(string value);
}
