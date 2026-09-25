#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: prepare-compiler-qualification-inputs.sh <output-directory>

Restores the released compatibility libraries used by compiler acceptance tests
and creates the writable receipt directory required by linked corpus tests.
EOF
}

if [[ $# -ne 1 ]]; then
  usage >&2
  exit 64
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output_root="$(python3 -c 'import os, sys; print(os.path.abspath(sys.argv[1]))' "$1")"
manifest="${repo_root}/eng/compiler-qualification.json"
compatibility_version="$(python3 - "$manifest" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as stream:
    version = json.load(stream)["compatibilityLibrariesVersion"]
if not isinstance(version, str) or not version.strip():
    raise SystemExit("compatibilityLibrariesVersion must be a non-empty string")
print(version)
PY
)"

package_root="${output_root}/packages"
reference_root="${output_root}/compatibility-references"
receipt_root="${output_root}/linked-receipts"
seed_project="${output_root}/CompatibilityPackages.csproj"

rm -rf "$package_root" "$reference_root" "$receipt_root"
mkdir -p "$package_root" "$reference_root" "$receipt_root"

cat > "$seed_project" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup>
    <PackageDownload Include="NetWasm.System.Text.Json" Version="[${compatibility_version}]" />
    <PackageDownload Include="NetWasm.System.Memory" Version="[${compatibility_version}]" />
    <PackageDownload Include="NetWasm.System.Text.Encodings.Web" Version="[${compatibility_version}]" />
    <PackageDownload Include="NetWasm.System.IO.Pipelines" Version="[${compatibility_version}]" />
    <PackageDownload Include="NetWasm.System.Text.RegularExpressions" Version="[${compatibility_version}]" />
  </ItemGroup>
</Project>
EOF

NUGET_PACKAGES="$package_root" dotnet restore "$seed_project" --nologo \
  --source https://api.nuget.org/v3/index.json

while IFS='|' read -r package assembly; do
  package_directory="$(printf '%s' "$package" | tr '[:upper:]' '[:lower:]')"
  cp "${package_root}/${package_directory}/${compatibility_version}/lib/NetWasm,Version=v0.1/${assembly}" \
    "${reference_root}/${assembly}"
done <<'EOF'
NetWasm.System.Text.Json|System.Text.Json.dll
NetWasm.System.Memory|System.Memory.dll
NetWasm.System.Text.Encodings.Web|System.Text.Encodings.Web.dll
NetWasm.System.IO.Pipelines|System.IO.Pipelines.dll
NetWasm.System.Text.RegularExpressions|System.Text.RegularExpressions.dll
EOF

printf 'Prepared compiler qualification inputs from NetWasm.Libraries %s in %s\n' \
  "$compatibility_version" "$output_root"
