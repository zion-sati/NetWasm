// NetWasm Community License 1.0; see the repository source-path license map.
/** @typedef {{args: string[], files: Record<string, Uint8Array>, outputs: string[]}} ToolRequest */
/** @typedef {{exitCode: number, files: Record<string, Uint8Array>, stdout: string,
 * stderr: string, consoleBytes: number, memoryBytes: number}} ToolResult */
/** @typedef {(name: string) => Promise<Uint8Array>} VerifiedAssetLoader */

const encoder = new TextEncoder();
export class ToolLimitError extends Error {}
export function toolLimits(options = {}) {
  const limits = { maximumInputBytes: 1048576, maximumOutputBytes: 1048576,
    maximumDiagnosticBytes: 65536, maximumArgumentBytes: 65536,
    maximumAssetBytes: 33554432, maximumFiles: 128, maximumPathBytes: 255,
    ...options };
  for (const [name, value] of Object.entries(limits))
    if (!Number.isSafeInteger(value) || value < 0) throw Error(`Invalid tool limit: ${name}`);
  return limits;
}
function filename(name, limits) {
  if (typeof name !== 'string' || !/^[a-zA-Z0-9_.-]+$/.test(name) || name === '.' || name === '..')
    throw Error('Invalid virtual filename');
  if (encoder.encode(name).byteLength > limits.maximumPathBytes)
    throw new ToolLimitError('Tool path limit exceeded');
}
/** Snapshot mutable byte inputs before awaiting an asset loader. */
export function snapshotRequest({ args = [], files = {}, outputs = [] }, limits) {
  if (!Array.isArray(args) || args.some(arg => typeof arg !== 'string' || arg.includes('\0')))
    throw Error('Tool arguments must be strings without NUL');
  if (args.reduce((sum, arg) => sum + encoder.encode(arg).byteLength + 1, 0) > limits.maximumArgumentBytes)
    throw new ToolLimitError('Tool argument limit exceeded');
  if (!Array.isArray(outputs)) throw Error('Tool outputs must be filenames');
  const entries = Object.entries(files);
  if (entries.length + outputs.length > limits.maximumFiles) throw new ToolLimitError('Tool file count limit exceeded');
  let count = 0;
  for (const [name, bytes] of entries) {
    filename(name, limits);
    if (!(bytes instanceof Uint8Array)) throw Error('Tool inputs must be Uint8Array');
    count += bytes.byteLength;
  }
  for (const name of outputs) {
    filename(name, limits);
    if (Object.hasOwn(files, name)) throw Error('Tool output cannot also be an input');
  }
  if (new Set(outputs).size !== outputs.length) throw Error('Duplicate tool output');
  if (count > limits.maximumInputBytes) throw new ToolLimitError('Tool input limit exceeded');
  return { args: args.slice(), files: Object.fromEntries(entries.map(([name, bytes]) => [name, bytes.slice()])),
    outputs: outputs.slice() };
}
export async function loadToolAsset(loader, name, limits) {
  const bytes = await loader(name);
  if (!(bytes instanceof Uint8Array)) throw Error('Verified asset loader must return Uint8Array');
  if (bytes.byteLength > limits.maximumAssetBytes) throw new ToolLimitError('Tool asset limit exceeded');
  return bytes;
}
export function diagnostics(limits) {
  const decoders = { stdout: new TextDecoder(), stderr: new TextDecoder() };
  let stdout = '', stderr = '', count = 0, denied;
  return {
    append(stream, bytes) {
      count += bytes.byteLength;
      if (count > limits.maximumDiagnosticBytes) {
        denied = new ToolLimitError('Tool console limit exceeded'); throw denied;
      }
      const text = decoders[stream].decode(bytes, { stream: true });
      if (stream === 'stdout') stdout += text; else stderr += text;
    },
    line(stream, text) { this.append(stream, encoder.encode(String(text) + '\n')); },
    check() { if (denied) throw denied; },
    result() { return { stdout: stdout + decoders.stdout.decode(),
      stderr: stderr + decoders.stderr.decode(), consoleBytes: count }; },
  };
}
export function copyOutputs(outputs, read, limits) {
  const result = Object.create(null); let count = 0;
  for (const name of outputs) {
    const bytes = read(name);
    count += bytes.byteLength;
    if (count > limits.maximumOutputBytes) throw new ToolLimitError('Tool output limit exceeded');
    result[name] = bytes.slice();
  }
  return result;
}
