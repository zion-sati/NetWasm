# NetWasmProject worker

Publish the worker and its browser page:

```bash
dotnet publish -c Release
```

Serve `bin/Release/netwasm0.1/publish` with a static HTTP server, then open its
`index.html`. The page calls the worker asynchronously, receives progress through
the browser's ordinary worker messaging, and disposes the worker when the call
finishes.
