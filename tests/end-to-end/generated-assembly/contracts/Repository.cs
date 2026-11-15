// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.GeneratorChecks.Contracts;

public interface IRepository<T>
{
    T Echo(T value);
}

public sealed class Repository<T> : IRepository<T>
{
    public T Echo(T value) => value;
}
