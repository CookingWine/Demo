#!/bin/zsh
# Aster 启动脚本（macOS / Linux）
# 若本机已把 dotnet 安装到系统路径，可去掉下面的 DOTNET_ROOT / PATH 设置。
set -e
cd "$(dirname "$0")"

export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"

exec dotnet run --project Aster/Aster.csproj --configuration Debug
