// The page side of a worker client's file mounts (#142): what the page mounts reaches every worker
// the client created, and every worker it creates later, each of which keeps its own in-memory
// copy. sync writes each live worker's changes back to what the user picked.

export function createWorkerFileMounts() {
  const sessions = new Set();
  const mounts = new Map();
  const each = (action, ...arguments_) => Promise.all([...sessions].map(session => session.files(action, ...arguments_)));

  return Object.freeze({
    // Replays the page's mounts to a worker that is ready; the returned action forgets it.
    async attach(session) {
      for (const [guestPath, source] of mounts) await session.files("mount", guestPath, source);
      sessions.add(session);
      return () => { sessions.delete(session); };
    },
    async mount(guestPath, source) {
      if (mounts.has(guestPath)) throw new TypeError(`'${guestPath}' is already mounted`);
      mounts.set(guestPath, source);
      try {
        await each("mount", guestPath, source);
      } catch (error) {
        mounts.delete(guestPath);
        throw error;
      }
    },
    async unmount(guestPath) {
      if (!mounts.delete(guestPath)) throw new TypeError(`'${guestPath}' is not mounted`);
      await each("unmount", guestPath);
    },
    async sync(guestPath) {
      if (!mounts.has(guestPath)) throw new TypeError(`'${guestPath}' is not mounted`);
      await each("sync", guestPath);
    },
  });
}
