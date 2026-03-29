#!/usr/bin/env bash
# Build Telepathy 2 .yak for Rhino Package Manager (Yak).
# Usage:
#   ./packaging/build-yak.sh        # stage net7.0 .gha (Rhino 8 Win + Mac)
#   ./packaging/build-yak.sh r7     # stage net48 .gha (Rhino 7)
#
# Then: yak login  &&  yak push packaging/stage/telepathy-2/*.yak
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TARGET="${1:-r8}"
if [[ "$TARGET" == "r7" ]]; then
  TFM="net48"
else
  TFM="net7.0"
fi

YAK=""
if command -v yak >/dev/null 2>&1; then
  YAK="yak"
elif [[ -x "/Applications/Rhino 8.app/Contents/Resources/bin/yak" ]]; then
  YAK="/Applications/Rhino 8.app/Contents/Resources/bin/yak"
elif [[ -x "/Applications/Rhino 7.app/Contents/Resources/bin/yak" ]]; then
  YAK="/Applications/Rhino 7.app/Contents/Resources/bin/yak"
fi

if [[ -z "$YAK" ]]; then
  echo "Yak CLI not found. Install Rhino 8/7 or add 'yak' to PATH."
  exit 1
fi

echo "Building Telepathy 2 (staging $TFM)…"
dotnet build "$ROOT/Telepathy.csproj" -c Release

STAGE="$ROOT/packaging/stage"
rm -rf "$STAGE"
mkdir -p "$STAGE/telepathy-2"

cp "$ROOT/packaging/telepathy-2/manifest.yml" "$STAGE/telepathy-2/"
cp "$ROOT/Resources/sender.png" "$STAGE/telepathy-2/icon.png"
cp "$ROOT/bin/Release/$TFM/Telepathy.gha" "$STAGE/telepathy-2/"

( cd "$STAGE/telepathy-2" && "$YAK" build )

echo ""
echo "Package: $STAGE/telepathy-2/"
echo "Publish: $YAK push $STAGE/telepathy-2/*.yak"
echo "Rhino 7: run $0 r7 and push those .yak files too."
