using System.Text;
using System.Text.Json;

namespace NetWasm.TimeZones;

internal sealed class TimeZoneBrowserLoaderEncoder : ITimeZoneBrowserLoaderEncoder
{
    public byte[] Encode(string assetFile)
    {
        ArgumentException.ThrowIfNullOrEmpty(assetFile);
        var literal = JsonSerializer.Serialize(assetFile);
        return Encoding.UTF8.GetBytes(
            "import { cli, filesystem } from \"@bytecodealliance/preview2-shim\";\n\n" +
            "export async function configureNetWasmTimeZone(options = {}) {\n" +
            "  const timeZone = options.timeZone;\n" +
            "  cli._setEnv(timeZone == null ? {} : { TZ: timeZone });\n" +
            "  filesystem._clearPreopens();\n" +
            "  if (timeZone == null || timeZone === \"UTC\" || timeZone === \"Etc/UTC\") return;\n" +
            "  const assetUrl = options.assetUrl ?? new URL(" + literal + ", import.meta.url);\n" +
            "  const response = await fetch(assetUrl);\n" +
            "  if (!response.ok) throw new Error(`Timezone asset fetch failed: ${response.status} ${response.statusText}`);\n" +
            "  const contents = new Uint8Array(await response.arrayBuffer());\n" +
            "  filesystem._setPreopens({\n" +
            "    \"/netwasm-timezones\": {\n" +
            "      dir: { \"netwasm-timezones.nwtz\": { source: contents } },\n" +
            "    },\n" +
            "  });\n" +
            "}\n");
    }
}
