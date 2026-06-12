#!/usr/bin/env bash
# 打包脚本：Windows 版 BrowserDemo 应用（自包含，含 .NET 运行时）
# 输出目录：C:\CodeSpace\Objects\Browser\Publish/Release/Win-SelfContained
# 依赖：.NET 8 SDK、git、bash (Git Bash / MSYS2 / WSL)

set -euo pipefail

# ========== 配置 ==========
PROJECT_DIR="$(cd "$(dirname "$0")/.." && pwd)/Demo/BrowserDemo"
OUTPUT_BASE="$(cd "$(dirname "$0")/.." && pwd)/Publish/Release/Win-SelfContained"
APP_NAME="BrowserDemo"

# ========== 准备输出目录 ==========
rm -rf "$OUTPUT_BASE"
mkdir -p "$OUTPUT_BASE"

echo "===== 1. 发布应用 (SelfContained, Win-x64, ReadyToRun) ====="
dotnet publish "$PROJECT_DIR/BrowserDemo.csproj" \
  -c Release \
  -r win-x64 \
  --self-contained \
  -p:PublishSingleFile=true \
  -p:PublishTrimmed=false \
  -p:UseAppHost=true \
  -o "$OUTPUT_BASE/app" \
  -v quiet

# 确认输出非空
if [ ! -f "$OUTPUT_BASE/app/${APP_NAME}.exe" ]; then
  echo "ERROR: 发布失败，未找到 ${APP_NAME}.exe"
  exit 1
fi
echo "  OK 发布完成"

echo "===== 2. 整理文件布局 ====="
# 目录结构:
#   /
#   ├── ${APP_NAME}.exe                <- 用户直接双击的主程序
#   ├── *.dll                          <- 原生 DLL（必须和 exe 同级，Windows 加载路径）
#   ├── bin/                           <- .NET 托管 DLL + .pdb + .xml
#   ├── docs/                          <- 使用说明
#   ├── logs/                          <- 程序日志目录
#   ├── resources/                     <- WebView2 原生 runtimes
#   └── config/                        <- *.json 配置文件

mkdir -p "$OUTPUT_BASE/bin"
mkdir -p "$OUTPUT_BASE/docs"
mkdir -p "$OUTPUT_BASE/logs"
mkdir -p "$OUTPUT_BASE/resources"
mkdir -p "$OUTPUT_BASE/config"

APP_DIR="$OUTPUT_BASE/app"

# 1) 复制 exe + pdb 到根目录
cp "$APP_DIR/${APP_NAME}.exe" "$OUTPUT_BASE/"
cp "$APP_DIR/${APP_NAME}.pdb" "$OUTPUT_BASE/" 2>/dev/null || true
cp "$APP_DIR/${APP_NAME}.runtimeconfig.json" "$OUTPUT_BASE/" 2>/dev/null || true

# 2) 原生 DLL 保留在根目录
#    包括：CLR 引擎、WebView2Loader、WPF 原生桥接库、VC++ 运行时等
#    Windows 加载原生 DLL 时只搜索 exe 同级目录 + 系统路径
NATIVE_PATTERNS=(
  "WebView2Loader.dll"
  "*_cor3.dll"
  "DirectWriteForwarder.dll"
  "hostfxr.dll" "hostpolicy.dll" "coreclr.dll"
  "clrjit.dll" "clretwrc.dll" "clrgc.dll" "mscorrc.dll"
  "mscordaccore*" "mscordbi.dll"
  "msquic.dll"
)

for f in "$APP_DIR"/*.dll; do
  [ -f "$f" ] || continue
  base=$(basename "$f")
  for pat in "${NATIVE_PATTERNS[@]}"; do
    if [[ "$base" == $pat ]]; then
      cp "$f" "$OUTPUT_BASE/"
      continue 2
    fi
  done
done

# 3) .NET 托管 DLL + .pdb + .xml 移入 bin/
#    托管程序集都以命名空间开头（Microsoft.*/System.*/BrowserDemo.* 等）
for f in "$APP_DIR"/*; do
  [ -f "$f" ] || continue
  base=$(basename "$f")
  ext="${base##*.}"
  case "$ext" in
    dll|pdb|xml)
      case "$base" in
        Microsoft.*|System.*|"$APP_NAME".*)
          cp "$f" "$OUTPUT_BASE/bin/"
          ;;
      esac
      ;;
  esac
done

# 4) 配置文件移入 config/
for f in "$APP_DIR"/*.json; do
  [ -f "$f" ] || continue
  base=$(basename "$f")
  # runtimeconfig.json 已复制到根目录，跳过
  [[ "$base" == *.runtimeconfig.json ]] && continue
  cp "$f" "$OUTPUT_BASE/config/"
done

# 5) 文档文件移入 resources/
for ext in txt md; do
  for f in "$APP_DIR"/*.$ext; do
    [ -f "$f" ] || continue
    cp "$f" "$OUTPUT_BASE/resources/"
  done
done

# 6) WebView2 原生库归入 resources/runtimes/
if ls "$APP_DIR"/runtimes/**/native/*.dll 1>/dev/null 2>&1; then
  mkdir -p "$OUTPUT_BASE/resources/runtimes"
  cp -r "$APP_DIR"/runtimes "$OUTPUT_BASE/resources/runtimes/"
fi

# 7) 写入使用说明
cat > "$OUTPUT_BASE/docs/使用说明.txt" << EOF
================================================================
  SmartAI Browser Demo - Windows 自包含版本
================================================================

运行方式:
  双击 ${APP_NAME}.exe 即可启动。

系统要求:
  - Windows 10/11 (x64)
  - 本版本已包含 .NET 8 运行时，无需预先安装 .NET
  - WebView2 Runtime（Windows 10/11 通常已内置）

文件说明:
  ${APP_NAME}.exe          主程序（根目录快捷入口）
  *.dll                    原生依赖库（和 exe 同级）
  bin/                     .NET 托管文件 (.dll / .pdb / .xml)
  config/                  配置文件 (.json)
  docs/                    使用说明与文档
  logs/                    程序日志目录（运行时自动写入）
  resources/               WebView2 原生库等资源

AI 配置:
  首次运行请在界面中配置 AI 提供商和 API Key，
  设置会保存在程序同目录的 ai_settings.json 中。

数据目录:
  WebView2 配置文件: %LocalAppData%/SmartAI-Browser-Demo/webview2-profile/
  聊天记录:          %LocalAppData%/SmartAI-Browser-Demo/conversations/
  应用日志:          程序所在目录的 logs/ 文件夹

技术支持:
  如遇问题，请查看 logs/ 目录下的日志文件并联系开发团队。
================================================================
EOF

echo "  OK 文件布局整理完成"

echo "===== 3. 清理临时文件 ====="
rm -rf "$APP_DIR"
echo "  OK 清理完成"

echo ""
echo "===== 发布完成 ====="
echo "输出目录: $OUTPUT_BASE"
echo "主程序:   $OUTPUT_BASE/${APP_NAME}.exe"
echo "总大小:   $(du -sh "$OUTPUT_BASE" | cut -f1)"
echo ""
echo "将此目录整体复制到目标 Windows 机器即可运行，无需安装 .NET。"
