#!/usr/bin/env bash
# Spike MM-9: assemble an ad-hoc signed host app with the Quick Look extension.
# usage: make-bundle.sh <libmmpreview.dylib> <out-dir> [bundle-id]
set -euo pipefail
SP="$(cd "$(dirname "$0")" && pwd)"
A=$SP/appex
dylib=$1; out=$2; bid=${3:-com.markmello.qlspike}
build=$(mktemp -d); trap 'rm -rf "$build"' EXIT
sdk="$(xcrun --show-sdk-path)"
swiftc -O -module-name MarkMelloQuickLook -parse-as-library -application-extension \
  -target arm64-apple-macos12.0 -sdk "$sdk" -framework QuickLookUI \
  -Xlinker -e -Xlinker _NSExtensionMain -o "$build/MarkMelloQuickLook" "$A/PreviewProvider.swift"
swiftc -O -target arm64-apple-macos12.0 -sdk "$sdk" -o "$build/host" "$A/host.swift"
app=$out/MarkMelloQLSpike.app
rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/PlugIns"
cp "$build/host" "$app/Contents/MacOS/MarkMelloQLSpike"
sed "s|BUNDLE_ID|$bid|" "$A/HostInfo.plist" > "$app/Contents/Info.plist"
ax=$app/Contents/PlugIns/MarkMelloQuickLook.appex
mkdir -p "$ax/Contents/MacOS" "$ax/Contents/Frameworks"
cp "$build/MarkMelloQuickLook" "$ax/Contents/MacOS/"
sed "s|BUNDLE_ID|$bid|" "$A/Info.plist" > "$ax/Contents/Info.plist"
cp "$dylib" "$ax/Contents/Frameworks/libmmpreview.dylib"
[[ -f "$(dirname "$dylib")/libonigwrap.dylib" ]] && cp "$(dirname "$dylib")/libonigwrap.dylib" "$ax/Contents/Frameworks/"
# inside-out ad-hoc signing
for f in "$ax"/Contents/Frameworks/*.dylib; do codesign --force --sign - "$f"; done
codesign --force --sign - --entitlements "$A/appex.entitlements" "$ax"
codesign --force --sign - "$app"
codesign --verify --deep --strict --verbose=2 "$app"
echo "$app"
