import assert from "node:assert/strict";
import test from "node:test";
import { snapshotDeploymentArtifact } from "./deployment-artifact.mjs";

const sourceArtifact = () => ({
  relativePath: "publish/My App.wasm",
  role: "application",
  mediaType: "application/wasm",
  sha256: "a".repeat(64),
  schemaVersion: null,
});

test("snapshots one exact immutable deployment artifact", () => {
  const source = sourceArtifact();
  const artifact = snapshotDeploymentArtifact(source);

  assert.equal(Object.isFrozen(artifact), true);
  assert.deepEqual(artifact, source);
  assert.notEqual(artifact, source);
  source.relativePath = "changed.wasm";
  assert.equal(artifact.relativePath, "publish/My App.wasm");
});

test("accepts a positive subordinate schema version", () => {
  const artifact = snapshotDeploymentArtifact({ ...sourceArtifact(), schemaVersion: 1 });
  assert.equal(artifact.schemaVersion, 1);
});
