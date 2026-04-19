#!/bin/bash
set -e

echo "🚀 开始构建 SwitchNro GUI..."

# 1. 进入 UI 目录并构建
cd src/SwitchNro.UI
dotnet build -c Debug -r osx-arm64 --self-contained false

# 2. 定位生成的原生可执行文件 (AppHost)
TARGET_BIN="bin/Debug/net10.0/osx-arm64/SwitchNro.UI"

if [ ! -f "$TARGET_BIN" ]; then
    echo "❌ 错误: 未能找到可执行文件 $TARGET_BIN"
    exit 1
fi

# 3. 授权签名 (关键步骤: 赋予 com.apple.security.hypervisor 特权)
echo "🔐 正在授予 HVF 特权权限..."
codesign --entitlements ../SwitchNro.Headless/Entitlements.plist -f -s - "$TARGET_BIN"

# 4. 验证签名
echo "🔍 验证签名状态:"
codesign -d --entitlements :- "$TARGET_BIN"

echo ""
echo "✅ 构建并授权成功！"
echo "--------------------------------------------------------"
echo "👉 请在你的本地 macOS 终端执行以下命令启动模拟器:"
echo "   ./src/SwitchNro.UI/$TARGET_BIN"
echo "--------------------------------------------------------"
