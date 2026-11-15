// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

extern int native_mule_managed_entry(int value);

int native_mule_invoke_named(int value)
{
    return native_mule_managed_entry(value);
}
