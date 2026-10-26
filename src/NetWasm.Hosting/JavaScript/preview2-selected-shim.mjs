// A Preview 2 shim built from only the interface families an application
// links. preview2-shim's WASIShim imports every family, so a page that never
// touches the network or a filesystem would still ship their shims. The
// generated host entry imports the families the application's raw bindings
// require and passes them here; the import object matches WASIShim's for the
// configuration NetWasm's provider registrations create.

const familyKeys = ["cli", "clocks", "filesystem", "http", "io", "random", "sockets"];
const requiredFamilies = ["cli", "io"];
const configKeys = ["filesystem", "sandbox"];
const sandboxKeys = ["args", "enableNetwork", "env", "preopens"];

export function createSelectedPreview2ShimFactory(families) {
  const selected = readFamilies(families);
  return Object.freeze(config => createSelectedPreview2Shim(selected, config));
}

function createSelectedPreview2Shim(families, config) {
  const { filesystem: suppliedFilesystem, sandbox } = readConfig(config);
  const cli = families.cli;
  let filesystem = suppliedFilesystem;
  if (filesystem === undefined && sandbox.preopens !== undefined) {
    // WASIShim gives a sandbox without a caller filesystem its own isolated
    // one; an application that links no filesystem interface needs none.
    filesystem = families.filesystem?.createFilesystem({ preopens: sandbox.preopens });
  }
  const sockets = families.sockets === undefined ? undefined
    : families.sockets.createSockets
      ? families.sockets.createSockets({ enableNetwork: sandbox.enableNetwork })
      : families.sockets;
  const environment = createIsolatedEnvironment(sandbox.env, sandbox.args, cli);
  return Object.freeze({
    getImportObject(options) {
      const suffix = options?.asVersion ? `@${options.asVersion}` : "";
      const imports = {};
      const add = (name, value) => { imports[`wasi:${name}${suffix}`] = value; };
      add("cli/environment", environment);
      add("cli/exit", cli.exit);
      add("cli/stderr", cli.stderr);
      add("cli/stdin", cli.stdin);
      add("cli/stdout", cli.stdout);
      add("cli/terminal-input", cli.terminalInput);
      add("cli/terminal-output", cli.terminalOutput);
      add("cli/terminal-stderr", cli.terminalStderr);
      add("cli/terminal-stdin", cli.terminalStdin);
      add("cli/terminal-stdout", cli.terminalStdout);
      if (sockets !== undefined) {
        add("sockets/instance-network", sockets.instanceNetwork);
        add("sockets/ip-name-lookup", sockets.ipNameLookup);
        add("sockets/network", sockets.network);
        add("sockets/tcp", sockets.tcp);
        add("sockets/tcp-create-socket", sockets.tcpCreateSocket);
        add("sockets/udp", sockets.udp);
        add("sockets/udp-create-socket", sockets.udpCreateSocket);
      }
      if (filesystem !== undefined) {
        add("filesystem/preopens", filesystem.preopens);
        add("filesystem/types", filesystem.types);
      }
      add("io/error", families.io.error);
      add("io/poll", families.io.poll);
      add("io/streams", families.io.streams);
      if (families.random !== undefined) {
        add("random/random", families.random.random);
        add("random/insecure", families.random.insecure);
        add("random/insecure-seed", families.random.insecureSeed);
      }
      if (families.clocks !== undefined) {
        add("clocks/monotonic-clock", families.clocks.monotonicClock);
        add("clocks/wall-clock", families.clocks.wallClock);
      }
      if (families.http !== undefined) {
        add("http/types", families.http.types);
        add("http/incoming-handler", families.http.incomingHandler);
        add("http/outgoing-handler", families.http.outgoingHandler);
      }
      return imports;
    },
  });
}

// Matches preview2-shim's isolated environment: the sandbox's arguments and
// variables over the CLI family's environment.
function createIsolatedEnvironment(env, args, cli) {
  const entries = env ? Object.entries(env) : null;
  const argumentList = args || null;
  return {
    ...cli.environment,
    getEnvironment() {
      return entries ?? cli.environment.getEnvironment();
    },
    getArguments() {
      return argumentList ?? cli.environment.getArguments();
    },
    initialCwd() {
      return cli.environment.initialCwd();
    },
  };
}

function readFamilies(families) {
  if (families === null || typeof families !== "object" || Array.isArray(families)) {
    throw new TypeError("Preview 2 shim families are invalid");
  }
  const names = Object.keys(families);
  if (names.some(name => !familyKeys.includes(name))
      || requiredFamilies.some(name => !names.includes(name))
      || names.some(name => families[name] === null || typeof families[name] !== "object")) {
    throw new TypeError("Preview 2 shim families must include cli and io and name known families");
  }
  return Object.freeze({ ...families });
}

function readConfig(config) {
  if (config === null || typeof config !== "object"
      || Object.keys(config).some(key => !configKeys.includes(key))
      || config.sandbox === null || typeof config.sandbox !== "object"
      || Object.keys(config.sandbox).some(key => !sandboxKeys.includes(key))
      // NetWasm passes either a caller filesystem or sandbox preopens, never both.
      || config.filesystem !== undefined && config.sandbox.preopens !== undefined) {
    throw new TypeError("Preview 2 shim configuration is invalid");
  }
  return config;
}
