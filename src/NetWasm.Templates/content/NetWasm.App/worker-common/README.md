# NetWasmProject worker

Run the worker and its browser page:

```bash
dotnet run
```

`dotnet run` publishes the page and serves it on localhost; open the address it
prints. `dotnet publish -c Release` writes the same site to
`bin/Release/netwasm0.1/publish` for any static HTTP server. The page calls the
worker asynchronously, receives progress through
the browser's ordinary worker messaging, and disposes the worker when the call
finishes.
