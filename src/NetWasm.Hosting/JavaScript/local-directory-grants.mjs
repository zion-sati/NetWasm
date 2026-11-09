// A local launch's directories. The program starts in the shell's working directory, seen at
// its own absolute path (a Windows drive's path as /c/...), so relative paths resolve as they do
// on desktop .NET wherever that directory is granted. The request's working directory grant
// mounts it read-write; its temporary directory grant mounts a private directory, made for the
// launch and removed after it, at /tmp, and points TMPDIR there. Explicit preopens at the same
// guest paths win. The expanded request names only explicit preopens, so the hosts after the
// launcher see no directory grants.

const factoryKeys = ["makeTemporaryDirectory", "platform", "removeDirectory", "workingDirectory"];
const temporaryGuestPath = "/tmp";

export function createLocalDirectoryGrants(options) {
  assertExactDataObject(options, factoryKeys, "local directory grant options");
  for (const name of ["makeTemporaryDirectory", "removeDirectory", "workingDirectory"]) {
    if (typeof options[name] !== "function") {
      throw new TypeError(`local directory grant '${name}' action is required`);
    }
  }
  if (typeof options.platform !== "string") {
    throw new TypeError("local directory grant platform is required");
  }

  return Object.freeze(async function grantLocalDirectories(request) {
    const workingDirectory = options.workingDirectory();
    const initialDirectory = guestPath(workingDirectory, options.platform);
    const preopens = [...request.grants.preopens];
    const taken = new Set(preopens.map(preopen => preopen.guestPath));
    if (request.grants.workingDirectory && !taken.has(initialDirectory)) {
      preopens.push(Object.freeze({ hostPath: workingDirectory, guestPath: initialDirectory, access: "readWrite" }));
    }

    let environment = request.environment;
    let grantedEnvironment = request.grants.environment;
    let temporaryDirectory = null;
    if (request.grants.temporaryDirectory && !taken.has(temporaryGuestPath)) {
      temporaryDirectory = await options.makeTemporaryDirectory();
      preopens.push(Object.freeze({ hostPath: temporaryDirectory, guestPath: temporaryGuestPath, access: "readWrite" }));
      environment = Object.freeze([
        ...environment.filter(variable => variable.name !== "TMPDIR"),
        Object.freeze({ name: "TMPDIR", value: `${temporaryGuestPath}/` }),
      ].sort((left, right) => left.name < right.name ? -1 : 1));
      grantedEnvironment = Object.freeze([...new Set([...grantedEnvironment, "TMPDIR"])].sort());
    }

    return Object.freeze({
      request: Object.freeze({
        ...request,
        environment,
        grants: Object.freeze({
          ...request.grants,
          environment: grantedEnvironment,
          preopens: Object.freeze(preopens),
          temporaryDirectory: false,
          workingDirectory: false,
        }),
      }),
      initialDirectory,
      async release() {
        if (temporaryDirectory !== null) await options.removeDirectory(temporaryDirectory);
      },
    });
  });
}

// The guest path a host directory is seen at: itself on POSIX hosts; on Windows, its drive as
// the first segment, lower case, with forward slashes.
export function guestPath(hostPath, platform) {
  if (platform !== "win32") return hostPath === "/" ? "/" : hostPath.replace(/\/+$/u, "");
  const drive = /^([A-Za-z]):[\\/]?(.*)$/u.exec(hostPath);
  if (drive === null) throw new TypeError("the working directory is not on a drive");
  const rest = drive[2].replace(/\\/gu, "/").replace(/\/+$/u, "");
  return rest.length === 0 ? `/${drive[1].toLowerCase()}` : `/${drive[1].toLowerCase()}/${rest}`;
}

function assertExactDataObject(value, keys, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError(`${label} is invalid`);
  }
  const prototype = Object.getPrototypeOf(value);
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const actualKeys = Object.keys(descriptors).sort();
  if (prototype !== null && prototype !== Object.prototype
      || actualKeys.length !== keys.length
      || actualKeys.some((key, index) => key !== keys[index])
      || Object.values(descriptors).some(
        descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError(`${label} shape is invalid`);
  }
}
