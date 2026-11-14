// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// From dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce (src/libraries/System.Private.CoreLib/src/System/TimeZoneInfo.Cache.cs),
// the one member CurrentSystemTimeZone needs of upstream's cache, which NetWasm's asset-backed TimeZoneInfo does not port.

namespace System
{
    public sealed partial class TimeZoneInfo
    {
        internal static DateTime TransitionTimeToDateTime(int year, TransitionTime transitionTime)
        {
            DateTime value;
            TimeSpan timeOfDay = transitionTime.TimeOfDay.TimeOfDay;

            if (transitionTime.IsFixedDateRule)
            {
                // create a DateTime from the passed in year and the properties on the transitionTime

                int day = transitionTime.Day;
                // if the day is out of range for the month then use the last day of the month
                if (day > 28)
                {
                    int daysInMonth = DateTime.DaysInMonth(year, transitionTime.Month);
                    if (day > daysInMonth)
                    {
                        day = daysInMonth;
                    }
                }

                value = new DateTime(year, transitionTime.Month, day) + timeOfDay;
            }
            else
            {
                if (transitionTime.Week <= 4)
                {
                    //
                    // Get the (transitionTime.Week)th Sunday.
                    //
                    value = new DateTime(year, transitionTime.Month, 1) + timeOfDay;

                    int dayOfWeek = (int)value.DayOfWeek;
                    int delta = (int)transitionTime.DayOfWeek - dayOfWeek;
                    if (delta < 0)
                    {
                        delta += 7;
                    }
                    delta += 7 * (transitionTime.Week - 1);

                    if (delta > 0)
                    {
                        value = value.AddDays(delta);
                    }
                }
                else
                {
                    //
                    // If TransitionWeek is greater than 4, we will get the last week.
                    //
                    int daysInMonth = DateTime.DaysInMonth(year, transitionTime.Month);
                    value = new DateTime(year, transitionTime.Month, daysInMonth) + timeOfDay;

                    // This is the day of week for the last day of the month.
                    int dayOfWeek = (int)value.DayOfWeek;
                    int delta = dayOfWeek - (int)transitionTime.DayOfWeek;
                    if (delta < 0)
                    {
                        delta += 7;
                    }

                    if (delta > 0)
                    {
                        value = value.AddDays(-delta);
                    }
                }
            }
            return value;
        }
    }
}
