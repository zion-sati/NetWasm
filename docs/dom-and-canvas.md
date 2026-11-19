# DOM and Canvas from C#

A page module's C# code reaches the DOM through small JavaScript functions it
imports, and the page reaches C# through the methods the module exports. This
guide builds one page that does the three things people ask about most:

- reads and writes the DOM from a click handler;
- draws pixels computed in C# to a Canvas on every animation frame, without
  copying them;
- draws on top of them with a call that crosses into JavaScript with nothing
  converted.

[JavaScript interop](javascript-interop.md) is the reference for every
signature used here.

## Create the module

```sh
dotnet new install NetWasm.Templates
dotnet new netwasm-app -n Paint --host page
cd Paint
```

The template makes a `JsModule` project for the page host: `Program.cs` holds
the C# exports and imports, `app.host.mjs` holds the JavaScript functions C#
imports, and `app.mjs` opens the module from `index.html`. Replace those four
files with the ones below.

## The page

`index.html`:

```html
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Paint</title>
</head>
<body>
  <main>
    <h1>Paint</h1>
    <p>
      <label>Name <input id="name" value="browser"></label>
      <button id="greet">Greet</button>
      <output id="result"></output>
    </p>
    <p><button id="toggle">Pause</button> <span id="frames"></span></p>
    <canvas id="canvas" width="256" height="160"></canvas>
  </main>
  <script type="module" src="./app.mjs"></script>
</body>
</html>
```

`app.mjs` opens the module, starts it, and stops it when the page goes away:

```js
import { open } from "./browser/Paint.mjs";

const app = await open();
app.start();

window.addEventListener("pagehide", async () => {
  app.stop();
  await app.close();
});
```

## The JavaScript side

The module's imports run in the page, so they can use the DOM directly.
`app.host.mjs`:

```js
// Imports run in the page, so they can use the DOM directly.
export function setText(selector, text) {
  document.querySelector(selector).textContent = text;
}

export function getValue(selector) {
  return document.querySelector(selector).value;
}

// Calls the C# handler on every click until C# disposes the subscription.
export function onClick(selector, handler) {
  const element = document.querySelector(selector);
  const listener = () => handler();
  element.addEventListener("click", listener);
  return { dispose() { element.removeEventListener("click", listener); } };
}

let context;
let image;

export function useCanvas(selector) {
  const canvas = document.querySelector(selector);
  context = canvas.getContext("2d");
  image = context.createImageData(canvas.width, canvas.height);
}

// Only primitive parameters: C# calls this as a direct WebAssembly call.
export function fillRect(x, y, width, height, rgb) {
  context.fillStyle = `#${rgb.toString(16).padStart(6, "0")}`;
  context.fillRect(x, y, width, height);
}

// rgba views C#'s pixel buffer, valid only during this call: copy it into the image.
export function putPixels(rgba) {
  image.data.set(rgba);
  context.putImageData(image, 0, 0);
}

// Calls the C# frame handler once per animation frame until C# disposes the subscription.
export function onFrame(frame) {
  let active = true;
  let id = requestAnimationFrame(function tick(time) {
    frame(time);
    if (active) id = requestAnimationFrame(tick);
  });
  return { dispose() { active = false; cancelAnimationFrame(id); } };
}
```

A function that takes a C# delegate receives it as a JavaScript function and
returns an object with a `dispose()` method. C# gets that back as a
`JSSubscription`; disposing it calls `dispose()` and releases the delegate.

## The C# side

`Program.cs`:

```csharp
using System;
using System.Runtime.InteropServices.JavaScript;

public static partial class Paint
{
    [JSImport("setText", "app.host")]
    private static extern void SetText(string selector, string text);

    [JSImport("getValue", "app.host")]
    private static extern string GetValue(string selector);

    [JSImport("onClick", "app.host")]
    private static extern JSSubscription OnClick(string selector, Action handler);

    [JSImport("useCanvas", "app.host")]
    private static extern void UseCanvas(string selector);

    [JSImport("fillRect", "app.host")]
    private static extern void FillRect(double x, double y, double width, double height, int rgb);

    [JSImport("putPixels", "app.host")]
    private static extern void PutPixels(ReadOnlySpan<byte> rgba);

    [JSImport("onFrame", "app.host")]
    private static extern JSSubscription OnFrame(Action<double> frame);

    private const int Width = 256;
    private const int Height = 160;
    private static readonly byte[] Pixels = new byte[Width * Height * 4];
    private static JSSubscription? _greet;
    private static JSSubscription? _toggle;
    private static JSSubscription? _frames;
    private static int _frameCount;

    [JSExport("start")]
    public static void Start()
    {
        UseCanvas("#canvas");
        _greet = OnClick("#greet", () => SetText("#result", $"Hello, {GetValue("#name")}, from C#."));
        _toggle = OnClick("#toggle", Toggle);
        Toggle();
    }

    [JSExport("stop")]
    public static void Stop()
    {
        _frames?.Dispose();
        _toggle?.Dispose();
        _greet?.Dispose();
    }

    private static void Toggle()
    {
        if (_frames is null)
        {
            _frames = OnFrame(Draw);
            SetText("#toggle", "Pause");
        }
        else
        {
            _frames.Dispose();
            _frames = null;
            SetText("#toggle", "Resume");
        }
    }

    private static void Draw(double time)
    {
        var t = time / 1000;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var i = (y * Width + x) * 4;
                Pixels[i] = (byte)(128 + 127 * Math.Sin(x / 16.0 + t));
                Pixels[i + 1] = (byte)(128 + 127 * Math.Sin(y / 16.0 + t * 1.3));
                Pixels[i + 2] = (byte)(128 + 127 * Math.Sin((x + y) / 24.0 + t * 0.7));
                Pixels[i + 3] = 255;
            }
        }
        PutPixels(Pixels);

        var left = (int)(t * 60) % (Width - 32);
        FillRect(left, Height / 2 - 16, 32, 32, 0xffffff);
        SetText("#frames", $"frame {++_frameCount}");
    }
}
```

## Run it

```sh
dotnet run
```

`dotnet run` publishes the page and serves it on localhost; open the address it
prints. Type a name and select **Greet**, and the page shows the greeting from
C#; the canvas animates until you select **Pause**. `dotnet publish -c Release`
writes the same site to `bin/Release/net10.0-netwasm/publish` for any static HTTP
server.

## How the pieces fit

- **Reading and writing the DOM.** `SetText` and `GetValue` pass strings, which
  are copied across the boundary. Selectors keep this example short; to touch
  one element often, import a function that returns it as a `JSObject` and pass
  that back, and dispose it when you are done.
- **Event handlers.** `OnClick` passes a C# `Action`. JavaScript calls it like
  any function, and each call runs the C# lambda synchronously on the page.
  Disposing the `JSSubscription` removes the listener.
- **The animation loop.** `OnFrame` passes an `Action<double>`, so
  `requestAnimationFrame` calls C# once per frame with its timestamp. A frame
  handler runs on the page's thread, so it must finish within the frame; for
  heavier work, move it to a [Web Worker](web-workers.md) and post the pixels
  back.
- **Pixels without a copy.** `PutPixels` takes a `ReadOnlySpan<byte>`, so
  JavaScript receives a `Uint8Array` that views the C# array's memory instead of
  a copy. The view is valid only while the call runs: `putPixels` copies it into
  the `ImageData` before returning. See
  [Choose a copy or a view](javascript-interop.md#choose-a-copy-or-a-view).
- **Direct calls.** `FillRect` takes only numbers, so it compiles to a direct
  WebAssembly call into the JavaScript function, with nothing copied through
  memory: the cheapest way to make many small drawing calls.
