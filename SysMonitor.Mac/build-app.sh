#!/bin/bash
# Builds dist/SysMonitor.app: tests first, then a universal release build
# (arm64 + x86_64, macOS 13+), wrapped in a bundle and signed ad hoc (R-025).
#
#   ./build-app.sh              tests, then the app
#   ./build-app.sh --no-tests   the app only
set -euo pipefail
cd "$(dirname "$0")"

VERSION="0.4.0"
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
echo "Built $APP $VERSION"
