#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -ne 4 ]]; then
  echo "usage: eng/verify-host-tools-repeatability.sh <rid> <version> <input-cache> <first-package>" >&2
  exit 2
fi

rid="$1"
version="$2"
input_cache="$3"
first_package="$4"
test -f "$first_package" || {
  echo "First host-tools package is missing: $first_package" >&2
  exit 1
}

repeat_root="$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/netwasm-host-repeat.XXXXXX")"
trap 'rm -rf "$repeat_root"' EXIT
python3 eng/build-host-tools-package.py \
  --rid "$rid" --version "$version" \
  --output "$repeat_root/packages" --cache "$input_cache"
second_package="$repeat_root/packages/NetWasm.HostTools.$rid.$version.nupkg"
if ! cmp -s "$first_package" "$second_package"; then
  echo "Host-tools packaging is not byte-for-byte repeatable for $rid $version" >&2
  # Name the entries that differ, so a failed run shows where the build is not deterministic.
  python3 - "$first_package" "$second_package" <<'PY' >&2
import hashlib, sys, zipfile
first, second = (zipfile.ZipFile(path) for path in sys.argv[1:])
def entries(archive):
    return {info.filename: (info.file_size, hashlib.sha256(archive.read(info)).hexdigest(), info.date_time)
            for info in archive.infolist()}
a, b = entries(first), entries(second)
for name in sorted(set(a) | set(b)):
    if a.get(name) != b.get(name):
        print(f"  differs: {name}: {a.get(name)} vs {b.get(name)}")
        if name in a and name in b and a[name][0] == b[name][0]:
            # Same length: where the bytes differ, and in which Mach-O segment, ELF section or PE section they lie.
            x, y = first.read(name), second.read(name)
            offsets = [i for i in range(len(x)) if x[i] != y[i]]
            print(f"    {len(offsets)} bytes differ, first at {offsets[:12]}, last at {offsets[-1]}")
            # Each run of differing bytes, with both builds' bytes and, in a Mach-O, the load command or section holding it.
            ranges = []
            for offset in offsets:
                if ranges and offset <= ranges[-1][1] + 8:
                    ranges[-1][1] = offset
                else:
                    ranges.append([offset, offset])
            places = []
            if x[:4] in (b"\xcf\xfa\xed\xfe",):
                import struct
                count, commands_size = struct.unpack_from("<II", x, 16)
                at = 32
                for index in range(count):
                    cmd, size = struct.unpack_from("<II", x, at)
                    places.append((at, at + size, f"load command {index} ({hex(cmd)})"))
                    if cmd == 0x19:
                        for section in range(struct.unpack_from("<I", x, at + 64)[0]):
                            header = at + 72 + section * 80
                            name = (x[header + 16:header + 32].rstrip(b"\0") + b"," + x[header:header + 16].rstrip(b"\0")).decode()
                            size64, offset32 = struct.unpack_from("<QI", x, header + 40)
                            if offset32:
                                places.append((offset32, offset32 + size64, f"section {name}"))
                    at += size
            for start, end in ranges[:16]:
                place = next((label for low, high, label in places if low <= start < high), "")
                print(f"    [{start}, {end}] {place}: {x[start:end + 1].hex()} vs {y[start:end + 1].hex()}")
            if x[:4] in (b"\xcf\xfa\xed\xfe",):
                import struct
                ncmds = struct.unpack_from("<I", x, 16)[0]
                at = 32
                for _ in range(ncmds):
                    cmd, size = struct.unpack_from("<II", x, at)
                    if cmd == 0x19:  # LC_SEGMENT_64
                        segment = x[at + 8:at + 24].rstrip(b"\0").decode()
                        fileoff, filesize = struct.unpack_from("<QQ", x, at + 40)
                        hits = [o for o in offsets if fileoff <= o < fileoff + filesize]
                        if hits:
                            print(f"    segment {segment} [{fileoff}, {fileoff + filesize}): {len(hits)} bytes, first at {hits[:6]}")
                    elif cmd in (0x1B, 0x1D, 0x26, 0x29):  # LC_UUID, LC_CODE_SIGNATURE, LC_FUNCTION_STARTS, LC_DATA_IN_CODE
                        if cmd == 0x1B:
                            print(f"    LC_UUID {x[at + 8:at + 24].hex()} vs {y[at + 8:at + 24].hex()}")
                        else:
                            dataoff, datasize = struct.unpack_from("<II", x, at + 8)
                            hits = [o for o in offsets if dataoff <= o < dataoff + datasize]
                            if hits:
                                print(f"    load command {hex(cmd)} data [{dataoff}, {dataoff + datasize}): {len(hits)} bytes")
                    at += size
PY
  exit 1
fi
python3 - "$rid" "$version" "$first_package" <<'PY'
import hashlib
import sys
from pathlib import Path

rid, version, package = sys.argv[1:]
with Path(package).open("rb") as stream:
    digest = hashlib.file_digest(stream, "sha256").hexdigest()
print(f"Repeat-pack PASS: {rid} {version} sha256={digest}")
PY
