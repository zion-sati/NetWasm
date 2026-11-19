// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jab;
using Mediator.Switch;

// SwitchMediator over Jab, a compile-time container: neither creates anything by reflection.
var container = new Container();
using (var scope = container.CreateScope())
{
    var mediator = new AppMediator(new JabServiceProvider(scope));
    Console.WriteLine(await mediator.Send(new Ping("hello")));
    Console.WriteLine(await mediator.Send(new Add(2, 40)));
    await mediator.Send(new Record("first"));
    try
    {
        await mediator.Send(new Fail("boom"));
    }
    catch (InvalidOperationException error)
    {
        Console.WriteLine("failed: " + error.Message);
    }
    await mediator.Publish(new Happened(3));
}
foreach (var line in container.GetService<Journal>().Lines)
{
    Console.WriteLine(line);
}

// The mediator resolves every listener of a notification as an IEnumerable, which Jab generates only for a root service.
[ServiceProvider(RootServices = [typeof(IEnumerable<INotificationHandler<Happened>>)])]
[Singleton(typeof(Journal))]
[Transient(typeof(PingHandler))]
[Transient(typeof(AddHandler))]
[Transient(typeof(RecordHandler))]
[Transient(typeof(FailHandler))]
[Transient(typeof(INotificationHandler<Happened>), typeof(FirstListener))]
[Transient(typeof(INotificationHandler<Happened>), typeof(SecondListener))]
internal partial class Container;

// What the mediator resolves, from the container's generated typed lookups.
internal sealed class JabServiceProvider(object scope) : ISwitchMediatorServiceProvider
{
    public T Get<T>() where T : notnull => scope is IServiceProvider<T> typed
        ? typed.GetService()
        : throw new InvalidOperationException("the container has no " + typeof(T).Name);
}

public sealed class Journal
{
    public List<string> Lines { get; } = [];
}

public sealed record Ping(string Message) : IRequest<string>;

public sealed class PingHandler : IRequestHandler<Ping, string>
{
    public Task<string> Handle(Ping request, CancellationToken cancellationToken) => Task.FromResult("Pong: " + request.Message);
}

public sealed record Add(int Left, int Right) : IRequest<int>;

public sealed class AddHandler : IRequestHandler<Add, int>
{
    public async Task<int> Handle(Add request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        return request.Left + request.Right;
    }
}

public sealed record Record(string Text) : IRequest<Unit>;

public sealed class RecordHandler(Journal journal) : IRequestHandler<Record, Unit>
{
    public Task<Unit> Handle(Record request, CancellationToken cancellationToken)
    {
        journal.Lines.Add("recorded " + request.Text);
        return Task.FromResult(Unit.Value);
    }
}

public sealed record Fail(string Reason) : IRequest<string>;

public sealed class FailHandler : IRequestHandler<Fail, string>
{
    public Task<string> Handle(Fail request, CancellationToken cancellationToken) => throw new InvalidOperationException(request.Reason);
}

public sealed record Happened(int Count) : INotification;

public sealed class FirstListener(Journal journal) : INotificationHandler<Happened>
{
    public Task Handle(Happened notification, CancellationToken cancellationToken)
    {
        journal.Lines.Add("first heard " + notification.Count);
        return Task.CompletedTask;
    }
}

public sealed class SecondListener(Journal journal) : INotificationHandler<Happened>
{
    public async Task Handle(Happened notification, CancellationToken cancellationToken)
    {
        await Task.Yield();
        journal.Lines.Add("second heard " + notification.Count * 2);
    }
}

[SwitchMediator]
public partial class AppMediator;
