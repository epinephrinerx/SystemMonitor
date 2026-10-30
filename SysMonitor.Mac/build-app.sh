#!/bin/bash
# Builds dist/SysMonitor.app and dist/SysMonitor-<version>.dmg: tests first,
# then a universal release build (arm64 + x86_64, macOS 13+), wrapped in a
# bundle with its icon, signed ad hoc (R-025), and put in a disk image with a
# link to Applications to drag it onto.
#
#   ./build-app.sh              tests, then the app
#   ./build-app.sh --no-tests   the app only
set -euo pipefail
cd "$(dirname "$0")"

VERSION="0.5.0"
BUNDLE_ID="io.github.epinephrinerx.sysmonitor"
APP="dist/SysMonitor.app"
ARCHS=(--arch arm64 --arch x86_64)

if [[ "${1:-}" != "--no-tests" ]]; then
    swift test
fi

swift build -c release "${ARCHS[@]}" --product SysMonitor
BIN="$(swift build -c release "${ARCHS[@]}" --show-bin-path)/SysMonitor"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/SysMonitor"

# The icon, drawn at 1024 px and cut down to every size an .icns holds.
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
swift tools/make-icon.swift "$WORK/icon.png"
ICONSET="$WORK/AppIcon.iconset"
mkdir -p "$ICONSET"
for px in 16 32 128 256 512; do
    sips -z $px $px "$WORK/icon.png" --out "$ICONSET/icon_${px}x${px}.png" >/dev/null
    sips -z $((px * 2)) $((px * 2)) "$WORK/icon.png" --out "$ICONSET/icon_${px}x${px}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"

# LSUIElement: no Dock icon and no main menu. The menu bar item is how the
# app is reached when its windows are hidden.
cat > "$APP/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>                 <string>System Monitor</string>
    <key>CFBundleDisplayName</key>          <string>System Monitor</string>
    <key>CFBundleExecutable</key>           <string>SysMonitor</string>
    <key>CFBundleIconFile</key>             <string>AppIcon</string>
    <key>CFBundleIdentifier</key>           <string>${BUNDLE_ID}</string>
    <key>CFBundlePackageType</key>          <string>APPL</string>
    <key>CFBundleShortVersionString</key>   <string>${VERSION}</string>
    <key>CFBundleVersion</key>              <string>${VERSION}</string>
    <key>LSMinimumSystemVersion</key>       <string>13.0</string>
    <key>LSUIElement</key>                  <true/>
    <key>NSHighResolutionCapable</key>      <true/>
</dict>
</plist>
EOF

codesign --force --sign - --timestamp=none "$APP"
codesign --verify --strict --verbose=1 "$APP"
lipo -info "$APP/Contents/MacOS/SysMonitor"

# The disk image: the app and a link to Applications, nothing else.
DMG="dist/SysMonitor-$VERSION.dmg"
STAGE="$WORK/dmg"
mkdir -p "$STAGE"
cp -R "$APP" "$STAGE/"
ln -s /Applications "$STAGE/Applications"
rm -f "$DMG"
# macOS 27 deprecates hdiutil's create in favour of diskutil image; older
# systems have only hdiutil.
if diskutil image create from --help >/dev/null 2>&1; then
    diskutil image create from --format UDZO --volumeName "System Monitor $VERSION" "$STAGE" "$DMG" >/dev/null
else
    hdiutil create -volname "System Monitor $VERSION" -srcfolder "$STAGE" -ov -format UDZO "$DMG" >/dev/null
fi
echo "Built $APP and $DMG ($VERSION)"
