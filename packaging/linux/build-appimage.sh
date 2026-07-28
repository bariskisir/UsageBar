#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 4 ]]; then
  echo "Usage: $0 <published-binary> <version> <x86_64|aarch64> <output-directory>" >&2
  exit 2
fi

published_binary=$1
version=$2
appimage_arch=$3
output_directory=$4

case "$appimage_arch" in
  x86_64 | aarch64) ;;
  *)
    echo "Unsupported AppImage architecture: $appimage_arch" >&2
    exit 2
    ;;
esac

script_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repository_root=$(CDPATH= cd -- "$script_directory/../.." && pwd)
work_directory=$(mktemp -d)
app_directory="$work_directory/UsageBar.AppDir"
appimage_tool="$work_directory/appimagetool-x86_64.AppImage"

mkdir -p \
  "$app_directory/usr/bin" \
  "$app_directory/usr/share/doc/usagebar" \
  "$output_directory"

cp "$published_binary" "$app_directory/usr/bin/UsageBar"
cp \
  "$script_directory/AppRun" \
  "$script_directory/usagebar.desktop" \
  "$script_directory/usagebar.svg" \
  "$app_directory/"
cp \
  "$repository_root/LICENSE" \
  "$repository_root/README.md" \
  "$app_directory/usr/share/doc/usagebar/"

ln -s usagebar.svg "$app_directory/.DirIcon"
chmod +x "$app_directory/AppRun" "$app_directory/usr/bin/UsageBar"

curl --fail --location --silent --show-error \
  "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage" \
  --output "$appimage_tool"
chmod +x "$appimage_tool"

ARCH="$appimage_arch" APPIMAGE_EXTRACT_AND_RUN=1 "$appimage_tool" \
  "$app_directory" \
  "$output_directory/UsageBar-${version}_${appimage_arch}.AppImage"
