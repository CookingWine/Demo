# Aster · Avalonia 桌面端

单机 **Demo 演示应用**：深色主题 + `#6C8BFF` 强调色 + 无边框圆角窗口。
没有后端、没有账号校验，客户 / 待办 / 指标 / 对话全部来自本机 JSON 缓存，删掉缓存就是一个空工作区。

> 完整的功能说明、数据格式与已知限制见仓库根目录的 [`README.md`](../../README.md)。

## 运行

需要 .NET 8 SDK（`dotnet --list-sdks` 应能看到 `8.0.x`）。工程目标框架是 `net8.0`，
本机 SDK 10 也能编译，但运行需要 .NET 8 运行时（或使用自包含发布）。

```bash
cd Dome/Aster
dotnet run
```

只编译不运行：`dotnet build`，产物在 `bin/Debug/net8.0/`。

> 登录页默认预填 `admin` / `admin`，任意非空账号密码都能进入 —— 这是刻意保留的演示行为，
> 不是漏写的校验。

> 项目内附带 `NuGet.Config`，默认指向国内镜像 `nuget.azure.cn`（首次 restore 更快）。
> 如需切回官方源，删除该文件或把地址改为 `https://api.nuget.org/v3/index.json`。

## 模块

| 模块 | 说明 |
| --- | --- |
| 启动 / 登录 | 2 秒进度启动屏；账号密码 / 记住我 / 显示密码 / 忘记密码 / 联系管理员（均为演示弹窗） |
| 工作台 | 快捷卡（新建客户 / KPI / 待办）+ Agent 对话；无对话时显示空态 |
| 客户工作空间 | 列表 / 搜索 / 筛选、新建 / 编辑 / 删除、详情四页签（基础 / 人员 / 核对 / 文件） |
| 协作与审批 | 待办列表、新建待办、通过 / 退回，结果只写本机缓存 |
| KPI 与考勤 | 指标列表与新增，数据来自缓存 |
| Workflow | 三步演示向导（确认范围 → 复核差异 → 归档），使用虚构样例 |
| 设置 | 全屏浮层 8 个分区；仅 AI 模型配置、数据目录 / 缓存清理、退出登录真实生效 |

左侧二级导航的「客户」与「最近会话」由 `AdminViewModel` 的数据派生：
没有客户时显示「还没有客户」，没有对话时显示「暂无会话记录」，不写死任何示例条目。

文档与音乐模块**未启用**：`MarkdownView` / `MusicView` 与对应 VM 仍在仓库中，
但 rail 没有入口、`MainWindowViewModel` 也不创建它们的实例，属于保留代码。

## 代码结构

```
Aster/
├─ Program.cs                 入口（AppBuilder + Inter 字体）
├─ App.axaml(.cs)             Fluent 深色主题 + 资源/样式装配
├─ Assets/                    应用图标：aster.ico / aster.icns / aster-*.png
├─ Services/
│  ├─ LocalCache.cs           本地缓存读写（local-cache.json，唯一持久化）
│  └─ AiChatService.cs        OpenAI 兼容流式客户端（SSE + Function Calling）
├─ Styles/
│  ├─ Theme.axaml             设计令牌 + 自定义 ControlTheme
│  └─ Controls.axaml          按钮 / 输入框 / 导航 / 分段 / 开关 / 标签 / 滚动条
├─ Controls/                  自绘控件（图标、图表、可视化、Markdown 预览）
├─ ViewModels/                手写 MVVM（ObservableObject + RelayCommand）
└─ Views/                     Shell / 登录 / 启动屏 / 工作台（AdminView）/ 设置浮层
```

## 数据

业务数据只有一份，由 `Services/LocalCache.cs` 管理的 `local-cache.json`：

- 开发态：`Dome/Aster/AppData/local-cache.json`（已被 `.gitignore` 忽略）
- 发布态：用户漫游目录，如 Windows `%APPDATA%\Aster\Cache`
- 自定义：环境变量 `ASTER_DATA_DIR`

工作区数据（客户 / 待办 / 指标 / 对话）全部整体写回该文件；运行期手工编辑会被内存覆盖，
需要改数据请先关闭程序。文件缺失或结构版本不符时会自动重建为空工作区（旧文件备份成 `.bak`）。

同目录下另有两个**独立**文件：

| 文件 | 内容 | 写入方式 |
| --- | --- | --- |
| `audit.ndjson` | Agent 请求 / 工具调用 / 确认结果 | 逐行追加，超 1 MB 轮转为 `audit.ndjson.1` |
| `audit.ndjson.1` | 上一份审计 | 轮转时覆盖 |

审计不参与 `local-cache.json` 的整体写回，清理它不会影响业务数据；设置页「存储与备份」
可以看到大小并清理。审计记录只含时间、模型名、端点主机、工具名、参数摘要、耗时与结果，
不含 API Key 与完整对话正文。

## 实现说明

- 不依赖 AvaloniaEdit、Markdig、LibVLC、LiveCharts：编辑器用 `TextBox` + 自绘行号，
  预览用控件树渲染，图表 / 可视化全部 `DrawingContext` 自绘，`restore` 只需要 Avalonia 官方包。
- AI 对话同样零依赖：`HttpClient` + `System.Text.Json` 手写 SSE 与工具调用聚合。
- 主题色、圆角、字号均来自 `Styles/Theme.axaml`。
