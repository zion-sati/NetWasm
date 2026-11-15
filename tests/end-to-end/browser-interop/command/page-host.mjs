// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

export function show(value) {
  document.querySelector("#managed").textContent = value;
}

export async function connect() {
  await Promise.resolve();
  return 21;
}

export function createObject() { return { value: 42 }; }

export function isNullObject(value) { return value === null; }

export function assertReleased() {
  if (document.querySelector("#released").textContent !== "released")
    throw new Error("Subscription was not disposed before command completion.");
}

export function onMessage(callback) {
  const button = document.querySelector("#deliver");
  const receive = () => callback(new Uint8Array([21]));
  button.addEventListener("click", receive);
  return {
    dispose() {
      button.removeEventListener("click", receive);
      document.querySelector("#released").textContent = "released";
    },
  };
}
