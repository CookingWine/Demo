# Aster · Avalonia 桌面端

跨平台桌面 Demo：深色玻璃质感、无边框圆角窗口，用一套「客户交付工作区」演示客户档案、审批待办、三步核对与 AI 助手。

`Avalonia 11.3.22` · `.NET 8` · `macOS / Windows / Linux` · `零第三方 UI 组件`

> **项目定位：单机演示应用。** 没有后端、没有账号体系、没有真实业务数据。
> 登录不做校验，客户 / 待办 / 指标 / 对话全部读写本机 `local-cache.json`，删掉缓存就是空工作区。
> 唯一的网络请求是用户主动配置的 AI 对话。

## 主要功能

| 模块 | 当前能力 |
| --- | --- |
| **启动 / 登录** | 2 秒启动动画；账号密码登录（无校验）、记住账号、显示密码、忘记密码与联系管理员两步弹窗 |
| **工作台** | 三张快捷卡（新建客户 / KPI 考勤 / 待办事项）、Agent 对话区、指令输入框、会话级模型切换 |
| **客户工作空间** | 搜索 + 三种筛选、客户列表、新建 / 编辑 / 删除；详情含基础资料、人员记录、核对记录、文件与产出四个页签 |
| **协作与审批** | 待办列表、待处理 / 全部筛选、新建待办、逐条通过或退回，结果写回本地缓存 |
| **Workflow** | 确认范围与材料 → 复核差异 → 归档 三步向导；结果为固定样例，不读取真实附件 |
| **KPI 与考勤** | 指标列表来自缓存，可新增指标并持久化；考勤与响应时效为固定演示文案 |
| **设置** | 全屏浮层八个分区：账号、通用、数据看板、成员与权限、通知、存储与备份、AI 模型、关于 |
| **AI Agent** | OpenAI 兼容协议、SSE 流式输出、Function Calling 五个工具、写操作二次确认、本机审计日志 |
| **保留未启用** | 文档编辑器、音乐播放器、早期仪表盘（HomeView）：View / ViewModel 代码仍在，导航中无入口 |

当前构建的一级入口为 **工作台 / 审批 / 客户 / Workflow / 设置**，其中「设置」打开浮层而不是切换页面。

> `docs/screenshots/` 中的截图来自早期版本，仍包含已下线的文档 / 音乐入口和写死的示例客户，
> 与当前界面不一致；重新截图已列入「待办事项」。

## 目录结构

```text
.
├─ README.md                    本文件
├─ CHANGELOG.md                 变更记录（每次改动的内容、涉及文件与注意事项）
├─ .gitignore                   忽略构建产物、本地缓存与示例数据
├─ docs/screenshots/            早期版本界面截图（与当前界面有差异）
├─ tools/
│  └─ make_icon.py              应用图标生成（PNG / ICO / ICNS 一次出全套）
└─ Dome/
   ├─ Dome.sln
   ├─ run.sh                    macOS / Linux 一键启动
   ├─ make-app.sh               macOS .app 打包（Dock / Finder 图标靠它）
   └─ Aster/
      ├─ Aster.csproj           net8.0 + Avalonia 11.3.22
      ├─ NuGet.Config           NuGet 源（默认国内镜像）
      ├─ app.manifest           Windows 应用清单
      ├─ Program.cs             入口（AppBuilder + Inter 字体）
      ├─ App.axaml / .cs        Fluent 深色主题、资源与样式装配
      ├─ Assets/                图标：aster.ico / aster.icns / aster-*.png
      ├─ Services/
      │  ├─ LocalCache.cs       本地 JSON 缓存（唯一业务数据存储）
      │  ├─ AiChatService.cs    OpenAI 兼容流式客户端（SSE + Function Calling）
      │  └─ AgentAudit.cs       Agent 审计日志（NDJSON 追加写 / 轮转 / 清理）
      ├─ Styles/
      │  ├─ Theme.axaml         设计令牌 + 自定义 ControlTheme
      │  └─ Controls.axaml      按钮 / 输入框 / 导航 / 分段 / 开关 / 标签 / 滚动条
      ├─ Controls/
      │  ├─ Icon.cs             24×24 线性图标，颜色继承 Foreground
      │  ├─ Icons.cs            图标 path 数据
      │  ├─ Charts.cs           迷你折线、趋势图、环形图（DrawingContext 绘制）
      │  ├─ Visuals.cs          五瓣 Logo、音频柱、唱片、进度条、跳动指示器
      │  └─ MarkdownPreview.cs  Markdown → 控件树渲染
      ├─ ViewModels/            手写 MVVM（核心为 AdminViewModel）
      └─ Views/                 窗口 / 启动 / 登录 / 工作区 / 设置
```

更细的模块说明见 [`Dome/Aster/README.md`](Dome/Aster/README.md)。

## 环境依赖

| 项 | 要求 |
| --- | --- |
| SDK | .NET 8 SDK（`dotnet --list-sdks` 能看到 `8.0.x`） |
| 系统 | macOS 11+ / Windows 10+ / 常见 Linux 桌面 |
| 数据库 / 原生库 | 无，业务数据只用一个 JSON 文件 |
| 图标脚本（可选） | Python 3 + `numpy` + `Pillow` |

> 工程目标框架是 `net8.0`。更高版本 SDK（例如 10.x）可以编译，但直接运行仍需要 .NET 8 运行时；
> 或者使用自包含发布，把运行时一起打进去。

### 安装

```bash
git clone git@github.com:CookingWine/Demo.git
cd Demo

# 若 SDK 装在用户目录（如 ~/.dotnet），先让终端能找到它
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"

cd Dome/Aster
dotnet restore
```

> **NuGet 源**：`Dome/Aster/NuGet.Config` 默认指向国内镜像 `https://nuget.azure.cn/v3/index.json`。
> 需要官方源时删除该文件，或改成 `https://api.nuget.org/v3/index.json`。

## 使用与运行

### 启动

```bash
# 方式一：一键脚本（macOS / Linux）
./Dome/run.sh

# 方式二：常规方式
cd Dome/Aster
dotnet run
```

只编译不运行：`dotnet build`，产物在 `Dome/Aster/bin/Debug/net8.0/`。

**演示登录**：账号与密码默认预填 `admin` / `admin`，直接点「登录」即可；任意非空内容都能进入，没有后端校验。

### 首次使用

新环境首次运行时工作区是空的，界面会给出空态提示：

1. 在工作台点「新建客户」，或在客户工作空间点「＋ 新建客户」；
2. 在协作与审批页点「＋ 新建待办」；
3. 在 KPI 与考勤页填写指标名称并点「新增」。

保存即写入 `Dome/Aster/AppData/local-cache.json`，全程不需要联网，也不需要手工改文件。

### 配置 AI 对话

1. 打开「设置 → AI 模型」；
2. 填写 API Key、Base URL、模型名，点「保存配置」；
3. 回到工作台，在底部输入框发送指令。

支持 DeepSeek、Kimi、通义兼容模式、Ollama 等 OpenAI 兼容端点。输入框右下角可切换**会话级模型**
（预设清单与设置自定义合并，仅当前会话生效，重启回落默认）。未配置 Key 时对话只给引导提示，不发请求。

### 编译与发布

```bash
cd Dome/Aster

# 编译
dotnet build

# 发布（按目标平台替换 RID）
dotnet publish -c Release -r osx-arm64 --self-contained false
# Windows: -r win-x64 ；Linux: -r linux-x64
```

### 打包成 macOS 应用（带 Dock 图标）

```bash
./Dome/make-app.sh                    # 框架依赖（默认），要求本机装了 .NET 8 运行时
SELF_CONTAINED=1 ./Dome/make-app.sh   # 自带运行时，体积更大，拷给别人也能跑
RID=osx-x64 ./Dome/make-app.sh        # Intel 机器
```

产物为 `Dome/dist/Aster.app`，脚本已包含 ad-hoc 签名。

> `dotnet run` 的宿主进程是 dotnet，Dock 里显示的是宿主图标；只有装进标准 `.app` 结构，
> 系统才会使用 `Info.plist` 与 `Contents/Resources/Aster.icns`。

## 本地数据

工作区界面上画出来的所有内容都来自一个 JSON 文件，代码里没有硬编码演示数据。

| 文件 | 位置 | 内容 | 写入方式 |
| --- | --- | --- | --- |
| `local-cache.json` | 开发态：`Dome/Aster/AppData/`；发布态：用户 ApplicationData 目录（Windows 为 `%APPDATA%\Aster\Cache`） | 登录记忆、窗口尺寸、AI 配置、客户 / 待办 / 指标 / 对话 | 原子写 + 700ms 合并写 |
| `audit.ndjson` | 与 `local-cache.json` 同目录 | Agent 请求、工具调用、确认结果 | 逐行追加，超过 1 MB 轮转为 `audit.ndjson.1` |

自定义位置：设置环境变量 `ASTER_DATA_DIR` 指向任意目录。

文件结构（字段名 camelCase，中文不转义，可直接打开编辑）：

```jsonc
{
  "schemaVersion": 2,
  "appVersion": "1.0.0.0",
  "updatedAt": "2026-10-30T09:00:00+08:00",
  "login":  { "remember": true, "userName": "演示账号", "lastLoginAt": "..." },
  "window": { "width": 1320, "height": 850 },
  "ai":     { "apiKey": "sk-…", "baseUrl": "https://api.deepseek.com", "model": "deepseek-chat" },
  "workspace": {
    "customers": [],   // 客户及其详情：基础资料 / 最近更新 / 人员 / 核对 / 文件
    "tasks": [],       // 待办与审批状态
    "metrics": [],     // KPI 指标
    "messages": []     // 工作台对话
  }
}
```

几个约定：

- `workspace` 下的数组是**全量快照**，每次保存整体覆盖；数组为空时对应页面就是空态。
- 想批量导入数据，请**先关闭程序**再改文件，运行期手改会被内存里的数据覆盖。
- 文件缺失会自动创建；结构版本不符或 JSON 解析失败会备份成 `.bak` 后重建为空工作区。
- 连续操作 700ms 内只落一次盘；登录成功、退出登录、关闭窗口三处走同步写。
- 想清空数据：删除 `Dome/Aster/AppData/`，或使用「设置 → 存储与备份 → 缓存占用 → 清理」。
- `ai.apiKey` 以明文保存在本机文件中，不会上传，但也不要提交到仓库或共享给别人。

## AI Agent 说明

工作台的对话走 OpenAI 兼容协议（`Services/AiChatService.cs`），使用 BCL 的 `HttpClient` +
`System.Text.Json` 手写 SSE 流式解析与工具调用聚合，不引入任何 AI SDK。

模型可调用五个白名单工具：

| 工具 | 作用 | 执行方式 |
| --- | --- | --- |
| `list_customers` | 查询客户 | 立即执行 |
| `list_tasks` | 查询待办 | 立即执行 |
| `create_customer` | 新建客户 | 二次确认后写入 |
| `create_task` | 新建待办 | 二次确认后写入 |
| `decide_task` | 通过 / 退回待办 | 二次确认后写入 |

写操作会先在对话里生成一张确认卡片，列出将执行的操作与参数；点「确认执行」才写入并继续生成回复。
点「取消」、3 分钟未确认、或直接发送新指令，均不会写入任何数据。确认卡片是瞬时界面消息，不写入缓存，
重启后不会残留。

其他边界：上下文最多 20 条消息、工具调用上限 8 轮、单次请求超时 120 秒；审计日志记录模型名、端点主机、
工具名、参数摘要与结果，不含 API Key 与完整正文。

## 技术实现

- **零第三方 UI 依赖**：只引用 Avalonia 官方包（Avalonia / Desktop / Fluent / Fonts.Inter），
  未引入 AvaloniaEdit、Markdig、LibVLCSharp、LiveCharts。编辑器用 `TextBox` + 自绘行号，
  Markdown 预览解析成控件树，图表与可视化全部走 `DrawingContext`。
- **手写 MVVM**：`ObservableObject` + `RelayCommand`，没有 MVVM 框架；页面切换由 `AdminPage` 枚举驱动。
- **无边框窗口**：`SystemDecorations=None` + 透明窗口，自绘标题栏与窗口按钮；
  四边 / 四角 8 个透明热区负责缩放，最小尺寸 1320×850，窗口尺寸记入本地缓存。
- **主题集中**：色值、圆角、字号在 `Styles/Theme.axaml`，样式全部走 `DynamicResource`，
  替换资源字典即可换肤。
- **本地存储零依赖**：`System.Text.Json` 在共享框架内，原子写 + 损坏自愈 + 延迟合并写。
- **AI 零依赖**：`HttpClient` + `System.Text.Json` 手写 SSE 与 Function Calling。

## 当前进度

| 模块 | 状态 | 说明 |
| --- | --- | --- |
| 应用壳 / 登录 | 已完成（演示） | 启动、登录、无边框窗口、尺寸记忆；无账号校验 |
| 工作台 / Agent | 已完成（演示） | 快捷卡、对话、5 个工具、确认卡片；需自备 API Key |
| 客户工作空间 | 已完成（基础 CRUD） | 搜索 / 筛选 / 新建 / 编辑 / 删除；详情三个页签只读 |
| 协作与审批 | 基本完成 | 新建、筛选、通过 / 退回；待办不能编辑或删除 |
| Workflow | 演示完成 | 三步流程可走通；结果为固定样例，归档不回写 |
| KPI 与考勤 | 部分完成 | 指标可新增并持久化；考勤为固定文案，指标不自动计算 |
| 设置 | 部分完成 | AI 配置、缓存 / 审计清理、退出登录真实生效；其余控件仅界面演示 |
| 文档 / 音乐 / HomeView | 未启用 | View / ViewModel 保留，导航无入口，未接数据层 |
| 自动化测试 | 缺失 | 无测试工程，验证依赖 `dotnet build` 与手工走查 |

最近一次改动见 [`CHANGELOG.md`](CHANGELOG.md)（2026-10-30，第 20 条：本次文档重写）。

## 待办事项

- [ ] **重拍界面截图**：`docs/screenshots/` 仍是含文档 / 音乐模块的旧版，且登录页版本号、设置分区都已变化。
- [ ] **统一版本号**：`make-app.sh` 写的是 `1.4.2`，界面与 `LocalCache` 是 `1.0.0`；建议改为单一来源。
- [ ] **决定未启用模块去留**：文档、音乐、HomeView 是恢复入口并接数据层，还是删除代码。
- [ ] **客户详情补写入端**：人员记录、核对记录、文件与产出三个页签目前只读，不能增删改。
- [ ] **待办与指标补编辑 / 删除**：目前只能新建，待办可通过 / 退回，指标只能新增。
- [ ] **Workflow 归档回写**：当前「确认归档」只跳回客户列表，不写核对记录；界面文案却承诺可在详情查看。
- [ ] **修正 KPI 页文案或实现计算**：页面写「任务完成率随演示操作更新」，实际指标是静态文本。
- [ ] **设置项持久化**：主题、通知、备份、成员与权限等控件目前只做界面演示，不落盘。
- [ ] **删除客户的可恢复性**：增加软删除 / 回收站，并处理关联待办（当前不级联删除）。
- [ ] **缓存 / 审计清理后的即时刷新**：清理后设置页的描述文案要重开浮层才更新。
- [ ] **AI 安全与健壮性**：Key 加密、HTTPS 强校验、写操作幂等键、`list_*` 分页、多实例写锁。
- [ ] **补自动化测试与 CI**：至少覆盖缓存读写、AI SSE 解析、工具参数校验与关键 ViewModel 状态机。

## 已知限制

- 定位为 Demo：无后端、无权限体系、无真实业务数据；登录页任意非空账号密码都能进入。
- 全应用唯一的网络请求是 AI 对话；无遥测上报，其余数据只写本机 JSON。
- Agent 只能调用五个白名单工具，**没有删除能力**；不能修改设置、读取附件、发送邮件或调用第三方系统。
- 工作区数据由程序整体写回：运行期手工编辑缓存文件会被内存覆盖，维护数据请先关闭程序。
- 删除客户不可撤销（没有软删除与回收站），也不会连带删除关联待办。
- 设置页除 AI 配置、存储清理与退出登录外，其余开关 / 下拉 / 按钮不持久化；账号卡与「关于」的应用信息为 Demo 占位（运行环境版本是真实读取）。
- 文档、音乐模块未启用；音乐若恢复入口仍是交互演示，不接音频解码。Markdown 预览暂不支持图片与嵌套列表。
- 审计日志按行追加，只在写入前判断 1 MB 阈值，只保留一份 `.1` 历史。

## 相关文档

- [`CHANGELOG.md`](CHANGELOG.md)：按日期倒序的变更记录，含实现要点与踩坑说明。
- [`Dome/Aster/README.md`](Dome/Aster/README.md)：模块级速览（运行、代码结构、数据文件）。
