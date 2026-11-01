// SHA-256 (FIPS 180-4) in JavaScript, for browsers without crypto.subtle.

const roundConstants = Uint32Array.of(
  0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
  0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
  0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
  0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
  0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
  0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
  0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
  0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2);

export function sha256(data) {
  const bytes = data instanceof ArrayBuffer ? new Uint8Array(data)
    : ArrayBuffer.isView(data) ? new Uint8Array(data.buffer, data.byteOffset, data.byteLength)
    : null;
  if (bytes === null) throw new TypeError("SHA-256 input must be an ArrayBuffer or a view of one");
  const state = Uint32Array.of(
    0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19);
  const schedule = new Uint32Array(64);
  const whole = bytes.length - bytes.length % 64;
  const input = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  for (let offset = 0; offset < whole; offset += 64) compress(state, schedule, input, offset);

  // The last one or two blocks: the rest of the input, a one bit, zeros and
  // the input's length in bits.
  const tail = new Uint8Array(bytes.length - whole < 56 ? 64 : 128);
  tail.set(bytes.subarray(whole));
  tail[bytes.length - whole] = 0x80;
  const last = new DataView(tail.buffer);
  const bits = bytes.length * 8;
  last.setUint32(tail.length - 8, Math.floor(bits / 0x100000000));
  last.setUint32(tail.length - 4, bits >>> 0);
  for (let offset = 0; offset < tail.length; offset += 64) compress(state, schedule, last, offset);

  const digest = new ArrayBuffer(32);
  const output = new DataView(digest);
  for (let index = 0; index < 8; index++) output.setUint32(index * 4, state[index]);
  return digest;
}

function compress(state, w, block, offset) {
  for (let t = 0; t < 16; t++) w[t] = block.getUint32(offset + t * 4);
  for (let t = 16; t < 64; t++) {
    const x = w[t - 15];
    const y = w[t - 2];
    const s0 = (x >>> 7 | x << 25) ^ (x >>> 18 | x << 14) ^ x >>> 3;
    const s1 = (y >>> 17 | y << 15) ^ (y >>> 19 | y << 13) ^ y >>> 10;
    w[t] = w[t - 16] + s0 + w[t - 7] + s1;
  }
  let [a, b, c, d, e, f, g, h] = state;
  for (let t = 0; t < 64; t++) {
    const s1 = (e >>> 6 | e << 26) ^ (e >>> 11 | e << 21) ^ (e >>> 25 | e << 7);
    const t1 = h + s1 + (e & f ^ ~e & g) + roundConstants[t] + w[t] | 0;
    const s0 = (a >>> 2 | a << 30) ^ (a >>> 13 | a << 19) ^ (a >>> 22 | a << 10);
    const t2 = s0 + (a & b ^ a & c ^ b & c) | 0;
    h = g;
    g = f;
    f = e;
    e = d + t1 | 0;
    d = c;
    c = b;
    b = a;
    a = t1 + t2 | 0;
  }
  state[0] += a;
  state[1] += b;
  state[2] += c;
  state[3] += d;
  state[4] += e;
  state[5] += f;
  state[6] += g;
  state[7] += h;
}
