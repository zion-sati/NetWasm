// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

// Prints the zone the host selected and one fixed instant in it.
var zone = TimeZoneInfo.Local;
var instant = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
var local = TimeZoneInfo.ConvertTimeFromUtc(instant, zone);
Console.WriteLine($"{zone.Id} {local:yyyy-MM-dd HH:mm}");
