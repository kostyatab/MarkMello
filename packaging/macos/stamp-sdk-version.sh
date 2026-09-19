#!/usr/bin/env bash
set -euo pipefail

# macOS 26+ draws the new window chrome (traffic lights, corner radius,
# system panels) only for executables linked against the 26 SDK or newer;
# older ones get the legacy compatibility look. The .NET apphost and the AOT
# binary linked by an older Xcode carry an older SDK version, so rewrite the
# SDK version in LC_BUILD_VERSION and re-sign the binary ad-hoc.

binary="${1:?Usage: stamp-sdk-version.sh <mach-o-executable>}"
sdk_version="26.0"

if ! command -v vtool >/dev/null || ! command -v codesign >/dev/null; then
  echo "vtool or codesign not found, $binary keeps its macOS SDK version."
  exit 0
fi

build_version="$(vtool -show-build "$binary")"
current_sdk="$(awk '$1 == "sdk" { print $2; exit }' <<<"$build_version")"
min_os="$(awk '$1 == "minos" { print $2; exit }' <<<"$build_version")"

if (( ${current_sdk%%.*} >= ${sdk_version%%.*} )); then
  exit 0
fi

vtool -set-build-version macos "$min_os" "$sdk_version" -replace -output "$binary" "$binary"
codesign --force --sign - "$binary"
