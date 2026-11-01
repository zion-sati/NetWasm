# NetWasmProject

Run the module and its page:

```bash
dotnet run
```

`dotnet run` publishes the page and serves it on localhost; open the address it
prints. `dotnet publish -c Release` writes the same site to
`bin/Release/netwasm0.1/publish` for any static HTTP server. The page opens the
C# module, calls its `greet` export
synchronously on the page, and closes it. The export calls back into
`app.host.mjs`, which writes to the page.

A synchronous export blocks the page while it runs. Return `Task` from an
export for longer work, or create the project with `--host worker` to run it
in a Web Worker.
