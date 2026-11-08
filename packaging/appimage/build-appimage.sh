#!/usr/bin/env bash
# Builds a self-contained D2Compare AppImage.
# Usage: packaging/appimage/build-appimage.sh <version-tag> [output-dir]
set -euo pipefail

TAG="${1:?usage: build-appimage.sh <version-tag> [output-dir]}"
OUT_DIR="$(realpath -m "${2:-publish}")"

APPIMAGETOOL_VERSION="1.9.1"
APPIMAGETOOL_SHA256="ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT

APPDIR="$WORK_DIR/D2Compare.AppDir"
mkdir -p "$APPDIR/usr/bin" "$OUT_DIR"

# Self-contained so the AppImage runs without a system .NET install
dotnet publish "$REPO_ROOT/src/D2Compare.UI/D2Compare.csproj" \
    -r linux-x64 --self-contained true -o "$APPDIR/usr/bin"

install -m 755 "$SCRIPT_DIR/AppRun" "$APPDIR/AppRun"
install -m 644 "$SCRIPT_DIR/d2compare.desktop" "$APPDIR/d2compare.desktop"
install -m 644 "$SCRIPT_DIR/d2compare.png" "$APPDIR/d2compare.png"

TOOL="$WORK_DIR/appimagetool"
curl -sSfL -o "$TOOL" \
    "https://github.com/AppImage/appimagetool/releases/download/$APPIMAGETOOL_VERSION/appimagetool-x86_64.AppImage"
echo "$APPIMAGETOOL_SHA256  $TOOL" | sha256sum -c -
chmod +x "$TOOL"

# Extract-and-run avoids needing FUSE on build machines (e.g. CI runners)
ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 "$TOOL" --no-appstream "$APPDIR" \
    "$OUT_DIR/D2Compare-$TAG-x86_64.AppImage"
