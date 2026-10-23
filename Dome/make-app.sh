#!/bin/zsh
# ============================================================
# make-app.sh —— 把 Aster 打成 macOS 的 .app（带 Dock / Finder 图标）
#
# 为什么需要这一步：Aster 平时用 `dotnet run` 启动，进程的宿主是 dotnet，
# macOS 的 Dock 显示的是宿主图标而不是 Window.Icon。只有把它装进标准的
# .app 目录结构（Contents/MacOS + Info.plist + Contents/Resources/*.icns），
# 系统才会认这个包自己的图标与名字。
#
# 用法：
#   ./Dome/make-app.sh                      # 框架依赖（约 30MB，要求本机装了 .NET 8 运行时，几秒出包）
#   SELF_CONTAINED=1 ./Dome/make-app.sh     # 自带运行时（约 80MB，拷给别人也能跑；首次要下运行时包）
#   RID=osx-x64 ./Dome/make-app.sh          # Intel 机器
#
# 产物：Dome/dist/Aster.app（已在 .gitignore 里忽略）
# ============================================================
set -e
cd "$(dirname "$0")"

NAME="Aster"
VERSION="1.4.2"
RID="${RID:-osx-arm64}"
SELF_CONTAINED="${SELF_CONTAINED:-0}"
OUT="$PWD/dist"
BUNDLE="$OUT/$NAME.app"

export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"

if [ "$SELF_CONTAINED" = "1" ]; then SC="true"; else SC="false"; fi

# 框架依赖模式下，Finder 启动的进程拿不到终端里的 DOTNET_ROOT，
# 所以把 dotnet 的实际安装目录写进 Info.plist 的 LSEnvironment 兜底。
DOTNET_DIR=""
if [ "$SELF_CONTAINED" != "1" ]; then
  DOTNET_BIN="$(command -v dotnet || true)"
  [ -n "$DOTNET_BIN" ] && DOTNET_DIR="$(cd "$(dirname "$DOTNET_BIN")" && pwd)"
fi

echo "==> publish（$RID，self-contained=$SC）"
# 只清掉旧包；publish 目录直接覆盖写，不整体删（避免误删 dist 里别的东西）
rm -rf "$BUNDLE"
dotnet publish "Aster/Aster.csproj" -c Release -r "$RID" --self-contained "$SC" \
  -p:UseAppHost=true -o "$OUT/publish" | tail -3

# .app 的标准骨架：可执行文件放 MacOS/，图标放 Resources/
mkdir -p "$BUNDLE/Contents/MacOS" "$BUNDLE/Contents/Resources"
# 注意 publish/. 里的那个点：写成 "$OUT/publish/" 会把整个目录塞进 MacOS/ 变成
# MacOS/publish/Aster，系统就找不到可执行文件（踩过一次）。
cp -R "$OUT/publish/." "$BUNDLE/Contents/MacOS/"
cp "Aster/Assets/aster.icns" "$BUNDLE/Contents/Resources/$NAME.icns"

# 框架依赖模式要告诉 apphost 去哪儿找运行时（值取自当前 shell 的 dotnet 位置）
ENV_BLOCK=""
if [ -n "$DOTNET_DIR" ]; then
  ENV_BLOCK="  <key>LSEnvironment</key>
  <dict>
    <key>DOTNET_ROOT</key><string>$DOTNET_DIR</string>
  </dict>"
fi

# CFBundleIconFile 只写文件名（不带扩展名），系统会去 Resources/ 找 Aster.icns
cat > "$BUNDLE/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>$NAME</string>
  <key>CFBundleDisplayName</key><string>$NAME</string>
  <key>CFBundleIdentifier</key><string>studio.aster.desktop</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleExecutable</key><string>$NAME</string>
  <key>CFBundleIconFile</key><string>$NAME</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSPrincipalClass</key><string>NSApplication</string>
$ENV_BLOCK</dict>
</plist>
PLIST

# ad-hoc 签名：Apple Silicon 上未签名的包会被 Gatekeeper 直接拒绝
codesign --force --deep --sign - "$BUNDLE" >/dev/null 2>&1 || echo "（ad-hoc 签名失败，可忽略）"

echo "==> 完成：$BUNDLE"
echo "    打开：open \"$BUNDLE\""
