namespace NetWasm.GeneratorChecks.Contracts;

public interface IRepository<T>
{
    T Echo(T value);
}

public sealed class Repository<T> : IRepository<T>
{
    public T Echo(T value) => value;
}
