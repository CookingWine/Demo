# Aster · Avalonia 桌面端

按 `aster-design.html` 设计稿 1:1 实现的 Avalonia 11 桌面应用（深色主题 + `#6C8BFF` 强调色）。

## 运行

需要 .NET 8 SDK（`dotnet --list-sdks` 应能看到 `8.0.x`）。
本项目使用的 SDK 安装在本机 `~/.dotnet`，因此首次运行先让终端能找到它：

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
# 想永久生效可写入 ~/.zshrc
```

然后二选一：

```bash
# 方式一：一键脚本（推荐）
cd /Users/jiaqili/Desktop/Demo/Avalonia/Dome
./run.sh

# 方式二：手动
cd /Users/jiaqili/Desktop/Demo/Avalonia/Dome/Aster
dotnet run
```

只编译不运行：`dotnet build`；产物在 `Aster/bin/Debug/net8.0/`。
直接双击/执行 `bin/Debug/net8.0/Aster` 时，同样需要 `DOTNET_ROOT` 指向 `~/.dotnet`。

> Windows 下把 `DOTNET_ROOT` 换成 SDK 安装目录即可，其余命令一致。

> 项目内附带 `NuGet.Config`，默认指向国内镜像 `nuget.azure.cn`（首次 restore 更快）。
> 如需切回官方源，删除该文件或把地址改为 `https://api.nuget.org/v3/index.json`。

## 界面流程

1. **启动屏**：2 秒进度动画 → 自动进入登录页。
2. **登录**：账号密码默认为空，输入任意非空内容即可进入工作台（纯演示，无后端校验）；「忘记密码」「联系管理员」为两步弹窗。
3. **工作台**（点左上角 Aster 图标随时回来）：问候语、四张指标卡、最近编辑、快捷操作、继续播放、今日节奏曲线。
4. **文档**：左侧目录树（分组折叠 / 搜索 / 分组筛选）+ 中间编辑器（行号 + 三档视图：编辑·分栏·预览）+ 右侧 Markdown 实时预览。
5. **音乐**：歌单列表 + 唱片舞台（旋转唱片、歌词、进度/音量可拖动）+ 右侧音频可视化与播放队列。
6. **管理**：概览（趋势图 / 环形图 / 成员表）、成员与权限、订单、审计日志、数据报表、导出记录。
7. **设置**：左上角齿轮图标打开全屏模态，7 个分区（账号 / 通用 / 数据看板 / 成员与权限 / 通知 / 存储与备份 / 关于）。

窗口为无边框自绘（圆角 14 / 阴影），标题栏支持拖拽、双击最大化；登录页整页可拖拽（输入框、按钮与弹窗区域除外）；
四边/四角热区可拖拽缩放，最小 1320×850（即设计稿尺寸）；右上角三枚按钮为最小化/最大化/关闭。

## 代码结构

```
Aster/
├─ Program.cs                 入口（AppBuilder + Inter 字体）
├─ App.axaml(.cs)             Fluent 深色主题 + 资源/样式装配
├─ Styles/
│  ├─ Theme.axaml             设计令牌：四级背景、文字、强调/语义色、渐变、ControlTheme
│  └─ Controls.axaml          控件样式：按钮/输入框/导航/分段/开关/标签/滚动条
├─ Controls/                  自绘控件（不依赖第三方库）
│  ├─ Icon.cs / Icons.cs      24×24 线性图标，颜色继承 Foreground
│  ├─ Charts.cs               迷你折线、趋势图、环形图
│  ├─ Visuals.cs              五瓣 Logo、音频柱、旋转唱片、进度条、跳动指示器
│  └─ MarkdownPreview.cs      Markdown → 控件树（标题/引用/列表/表格/代码块）
├─ ViewModels/                纯 C# MVVM（ObservableObject + RelayCommand）
└─ Views/                     Shell / 工作台 / 文档 / 音乐 / 管理 / 设置 / 登录 / 启动屏
```

## 实现说明

- 不依赖 AvaloniaEdit、Markdig、LibVLC、LiveCharts：编辑器用 `TextBox` + 自绘行号，预览用控件树渲染，
  图表/可视化全部用 `DrawingContext` 自绘，因此 `dotnet restore` 只需要 Avalonia 官方包。
- 音乐播放为交互演示（进度条随计时器前进、唱片旋转、柱状图动画），未接入音频解码。
- 主题色、圆角、字号均来自 `Styles/Theme.axaml`，改一处即可全局换色。
