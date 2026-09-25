using System;

namespace NetWasm.Fixtures.ComponentModel;

public static class TimeZoneHostingProgram
{
    public static int Main()
    {
        var selected = Environment.GetEnvironmentVariable("TZ");
        if (selected == "UTC")
        {
            return !string.IsNullOrEmpty(TimeZoneInfo.Local.Id) &&
                !TimeZoneInfo.Local.SupportsDaylightSavingTime &&
                TimeZoneInfo.Local.GetUtcOffset(DateTime.UnixEpoch) == TimeSpan.Zero ? 0 : 90;
        }

        if (TimeZoneInfo.Local.Id != "Australia/Melbourne")
        {
            return 91;
        }

        for (var index = 0; index < 12; index++)
        {
            if (NetWasm.Tests.TimeZoneInfoFacade.Fixture.EntryPoint.Run(index) != (index + 1) * 100 + 1)
            {
                return index + 1;
            }
        }

        return 0;
    }
}
