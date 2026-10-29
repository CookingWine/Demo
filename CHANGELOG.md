# 变更记录

记录每一次修改的内容、涉及文件和注意事项。换一台电脑继续开发时，拉取代码后先看这份文件，就能对齐到最新进度。

## 维护规则

- **每次改完代码，先更新本文件，再提交**；一条记录对应一次提交，标注 commit 短 hash。
- 记录写清楚三件事：**改了什么**（现象/需求）、**改了哪些文件**、**有什么要注意的**（坑、临时方案、后续待办）。
- 最新记录放在最上面，按日期倒序排列。

## 项目现状

| 项 | 内容 |
| --- | --- |
| 技术栈 | .NET 8 + Avalonia 11.3.22，零第三方 UI 库（图标/图表/Markdown 预览全部自绘） |
| 主工程 | `Dome/Aster`（`Aster.csproj`） |
| 编译 | `cd Dome/Aster && dotnet build`（本机 SDK 10.0.401 也能编译 net8.0，产物在 `bin/Debug/net8.0/`） |
| 运行 | `dotnet run`；登录框任意非空账号密码即可进入（无后端校验） |
| 当前模块 | 一级入口为 工作台 / 审批 / 客户 / Workflow / 设置；文档、音乐模块已在 rail 中移除，代码仍保留 |
| 页面状态 | 工作区六个页面由 `AdminPage` 枚举 + `AdminViewModel.Go(page)` 切换，`IsWorkbench` / `IsCustomers` / `IsDetail` / `IsApproval` / `IsWorkflow` / `IsKpi` 六个派生 bool 控制显隐 |
| AI 对话 | 工作台 Agent 已接真实大模型：`Services/AiChatService.cs` 走 OpenAI 兼容协议（SSE 流式 + Function Calling），在「设置 → AI 模型」配 API Key / Base URL / 模型名（存 local-cache.json 的 `ai` 段）；未配置 Key 时只提示不发请求 |
| 本地存储 | `Dome/Aster/Services/LocalCache.cs` + 运行期文件 `Dome/Aster/AppData/local-cache.json`（**已在 .gitignore 中忽略**）：登录记忆、窗口尺寸、AI 接入配置，以及工作区全部数据（客户 / 详情四页签 / 待办 / KPI / 对话）；**界面内容完全由该文件决定，代码里无演示数据**；界面上可新建客户 / 待办 / 指标，也可改审批状态与对话 |

---

## 2026-10-29

### 19. 修复 DataTemplate 内类型转换导致的运行时崩溃 —（与第 17/18 条同批提交）

- **现象**：进入工作台抛 `System.ArgumentException: Unable to resolve type vm:AdminViewModel`，
  来源 `AdminView.axaml` 第 299 行（确认卡片「确认执行」按钮）。
- **原因**：绑定路径写成
  `$parent[ItemsControl].((vm:AdminViewModel)DataContext).ConfirmPendingCommand`。
  其中 `((vm:…)DataContext)` 是 XAML 类型转换语法，需要运行期解析 `vm:` 命名空间前缀；
  Avalonia 11.3 的反射绑定在 `$parent[...]` 之后拿不到该映射，于是整个模板 Measure 失败。
  同文件核对记录行的 `GoWorkflowCommand` 用了同样写法，属同类潜在崩溃（此前核对页无数据未暴露）。
- **修复**：去掉类型转换，改为逐段反射取值
  `$parent[ItemsControl].DataContext.ConfirmPendingCommand`。`DataContext` 虽是 `object`，
  反射绑定仍能继续按属性名解析。共 3 处：确认 / 取消 / 查看结果。
- **文件**：`Dome/Aster/Views/AdminView.axaml`、本文件。
- **注意**：以后在 XAML 绑定路径里不要再写 `((vm:Xxx)DataContext)`；需要跨模板取 VM 属性时，
  直接用 `$parent[ItemsControl].DataContext.Property`，或给列表项注入命令。
- **验证**：`dotnet build` 0 warning 0 error；含两条消息的缓存启动 8 秒，窗口标题正常（Aster）、
  进程未退出。工作台消息模板的实际渲染需登录后人工确认。

### 18. Agent 审计日志 + 写操作二次确认 + 操作者去写死 — （提交后回填 hash）

- **改动**：按 AI Agent 规格落地三项。① Agent 不再自称「张客服」：系统提示、新建客户的默认负责人、
  新建表单的负责人预填、「我负责的客户」筛选统一取登录账号；② 新增独立审计日志
  `AppData/audit.ndjson`，记录模型请求、工具调用与确认结果，与业务缓存分开；③ 新建客户 / 新建待办 /
  审批三类写操作改为**对话内二次确认**：先出确认卡片列出操作与参数，点「确认执行」才落盘，
  取消 / 3 分钟超时 / 直接发新指令均不写入。
- **文件**：
  - 新增 `Dome/Aster/Services/AgentAudit.cs`（NDJSON 追加写、1 MB 轮转、脱敏摘要、清理门面）
  - `Dome/Aster/ViewModels/AdminViewModel.cs`（`OperatorName` 注入与 4 处替换；`ChatMessage` 增加
    `IsConfirmCard` / `ConfirmDetail` / `IsConfirmExpired` / `ConfirmState` / `ShowAgentBubble`；
    `RunAgentLoopAsync` 读写分离与轮次续跑；`PendingWrite` 状态机、`ConfirmPending` / `CancelPending` /
    `ExpirePending` / `AbandonPendingIfAny`；`StartAgentRun`；request/tool/confirm 审计接入；
    `SaveWorkspace` 跳过确认卡片；`ToolDecideTask` 缺省说明去承诺）
  - `Dome/Aster/Views/AdminView.axaml`（确认卡片模板块 + 两个按钮；普通 Agent 气泡改绑 `ShowAgentBubble`）
  - `Dome/Aster/ViewModels/MainWindowViewModel.cs`（构造与登录成功后注入操作者）
  - `Dome/Aster/ViewModels/SettingsViewModel.cs`（存储与备份新增「Agent 审计日志」行与清理）
  - `README.md`、`Dome/Aster/README.md`、本文件
- **实现要点**
  - **读写分离**：`list_*` 立即执行；`create_*` / `decide_task` 只暂存，不执行。
    assistant 的 `tool_calls` 仍整体回放（协议要求一次声明的所有调用都要有对应回执），
    只读工具回执先补齐，写工具回执等确认后补齐。
  - **轮次续跑**：`PendingWrite.RoundsUsed` 记录已消耗轮数，确认后 `RunAgentLoopAsync(..., startRound)`
    从原轮次继续，不会因为确认一次就重置 8 轮上限。
  - **确认卡片不落盘、不进上下文**：`SaveWorkspace` 与 `BuildAiHistory` 都跳过 `IsConfirmCard`；
    重启后不会留下点不动的按钮。卡片失效后由 `IsConfirmExpired` 把按钮置灰并显示原因。
  - **审计是旁路**：`AgentAudit.Append` 内部 `lock` + `AppendAllText`，任何 IO 异常只写 `LastError`，
    绝不影响对话；参数摘要只保留 name / title / task_id / approve 等标识，自由文本截 40 字符、总长 200。
  - **操作者来源**：`MainWindowViewModel` 在构造（「记住我」恢复的账号）与 `Login()` 成功后各注入一次，
    `AdminViewModel` 不直接读 `LocalCache.Data.Login`（未勾选记住我时那里是空的）。
- **注意**
  - 确认批次执行中若抛未预期异常，会先 `SaveWorkspace()` 保住已生效的部分，再提示「部分可能已生效」，
    不继续请求模型。
  - 审计文件与 `local-cache.json` 同目录，`AppData/` 已被 `.gitignore` 忽略；发布态落在 `%APPDATA%\Aster\Cache`。
  - 设置页清理审计后，该行描述不会即时刷新（`SettingRow.Desc` 是 init-only），与「缓存占用」同样的已知瑕疵。
  - 未做（规格里的 P1/P2）：HTTPS 强校验、Key 加密、幂等键、`list_*` 分页、工具权限分级、注入清洗、多实例写锁。
- **验证**：`dotnet build`（独立输出目录）0 warning 0 error；空缓存启动路径未回归（代码审查）。
  真实确认流程需在「设置 → AI 模型」填入有效 Key 后人工走查：发一条「帮我建一个客户」应只出卡片不落盘。

### 17. 收敛为 Demo 演示定位：左侧导航数据驱动 + 全应用空态与文案去真实化 — （提交后回填 hash）

- **改动**：本地缓存为空时，左侧「客户 / 最近会话」不再显示写死的「青禾 · 月度核对」「8 月个税交叉验证」
  「新的工作会话」，改为从缓存数据派生并显示空态提示；工作台会话区补空态；「新建项目」统一改称「新建客户」
  并直接打开新建表单；同时清理 Workflow 假客户名、侧栏写死用户名、设置页「陈墨 / chenmo@aster.io」、
  详情页写死版本号、审批页与实际实现不符的承诺等误导性默认内容。
- **文件**：
  - `Dome/Aster/ViewModels/AdminViewModel.cs`（新增 `AllCustomers` / `HasAnyCustomer` / `HasMessages` /
    `WorkspaceChanged` / `OpenCustomerById`；`Select` 增加 `customer:{Id}` 与 `customer-new`；
    `WorkbenchCard` 增加 `Target` 并据此分派；Workflow 无客户守卫；作者与负责人默认值去真实化）
  - `Dome/Aster/ViewModels/MainWindowViewModel.cs`（抽出 `BuildWorkspaceSubNav` / `RefreshSubNav`，
    订阅 `WorkspaceChanged`；侧栏用户卡片改读登录账号；`SyncRailSelection` 支持 `customer:` 前缀）
  - `Dome/Aster/ViewModels/Items.cs`（新增 `SubNavEmptyEntry`）
  - `Dome/Aster/Views/ShellView.axaml`（空态条目模板；用户名截断；rail 头像首字母同源）
  - `Dome/Aster/Views/AdminView.axaml`（工作台副标题与会话空态、卡片/列表/详情/审批文案、
    人员与文件表头按数据显隐、最近更新空态、Workflow 无客户提示与按钮禁用、输入框水印）
  - `Dome/Aster/ViewModels/SettingsViewModel.cs`（账号卡与基本信息去真实化、Footer 改「仅本次运行有效」、
    关于分区新增「项目定位」、版本号与链接统一到 v1.0.0）
  - `Dome/Aster/Views/LoginView.axaml`（Demo 标语与 DEMO 胶囊、登录副标题、管理员名片与成功提示）
  - `Dome/Aster/Views/BootView.axaml`（启动屏版本号）、`README.md`、`Dome/Aster/README.md`、本文件
- **实现要点**
  - **数据出口只有四个成员**：壳层只依赖 `AllCustomers` / `HasAnyCustomer` / `HasMessages` / `WorkspaceChanged`，
    不直接碰 `LocalCache`，也不复用 `Customers`（那是搜索/筛选后的结果）。
  - **变化事件收口在 `SaveWorkspace`**：客户增删改、待办、指标、发消息全部走这一条落盘路径，
    在其末尾触发一次即可覆盖；流式增量只改消息正文不落盘，不会造成高频重建。
  - **重建导航不改当前页面**：`RefreshSubNav` 只 `Clear` + 重建 + 恢复高亮，不调用 `Admin.Select`，
    否则用户正在详情页/审批页时会被一次保存踢回工作台。
  - **空态是独立条目类型**：`SubNavEmptyEntry` 没有 Command、没有 Key，`OfType<SubNavLinkEntry>()`
    天然把它排除在选中逻辑外，不存在「看起来能点却没反应」。
  - **`Target` 替代 Title 分派**：以前改卡片文案会静默改掉点击行为，现在文案与行为解耦。
  - **保留旧 key**：`customer-qinghe` / `thread-tax` / `detail-*` 仍可路由，避免历史调用点失效；
    客户被删除后 `customer:{Id}` 找不到时退回客户列表，不抛异常。
- **注意**
  - 侧栏最多列 5 位客户，超出显示「查看全部客户（共 N 位）」，避免挤出「最近会话」。
  - 「最近会话」仍没有独立会话模型：有对话显示一条「当前工作会话」，没有就显示空态，不新增存储字段。
  - 设置页的外观 / 通知 / 备份等控件依旧不落盘，本次只把文案改成 Demo 口径并在关于页标注项目定位。
  - `HomeViewModel` / `MarkdownView` / `MusicView` 未挂载，本次未清理其内部演示数据，文档中标注为「未启用」。
- **验证**：`dotnet build`（独立输出目录，避开运行中的 exe 锁）0 warning 0 error；
  空缓存与含 1 客户 + 1 条对话两种状态启动，左侧分别显示空态与真实条目，工作台会话区空态正常显示。

## 2026-10-28

### 16.1 修正 AI 消息协议字段序列化 — `def13ef`

- **改动**：`AiChatMessage` 的 `Role` / `Content` 补 `[JsonPropertyName("role")]` / `[JsonPropertyName("content")]`。
  此前请求体里这两个字段的键名依赖序列化默认行为，OpenAI 兼容端点在部分实现下会因字段名不匹配返回 4xx。
- **文件**：`Dome/Aster/Services/AiChatService.cs`（2 行）、本文件。
- **注意**：只影响发往模型的请求体，不影响 `local-cache.json` 的结构与 `schemaVersion`。
- **验证**：当时只做了编译与冒烟；真实对话仍需在「设置 → AI 模型」填入有效 Key 后复验（见第 15 条）。

### 16. 工作台输入框工具行增加会话级模型选择器 — `0dae53f`

> 本条与第 15 条共用 AdminViewModel / AdminView / AiChatService 等同一批文件，
> 无法按文件拆成两次提交，故两条记录合并为一次提交 `0dae53f`，共用同一 hash。

- **改动**：把工具行右侧「5.5 Medium」静态装饰换成真的模型下拉（ghost 风格 ComboBox）。
  清单 = 内置预设（deepseek-chat / deepseek-reasoner / qwen-plus / kimi-k2）∪ 设置页自定义模型，
  OrdinalIgnoreCase 合并去重；选中**仅当前会话生效**——只写内存字段、不碰 local-cache.json，
  重启后回落设置页的全局默认模型；AI 回复期间选择器禁用（IsEnabled 绑 !IsAiBusy），
  避免同一轮工具循环前后段用了不同模型。
- **文件**：`Dome/Aster/Services/AiChatService.cs`（StreamChatAsync 增加可选参数 modelOverride）、
  `Dome/Aster/ViewModels/AdminViewModel.cs`（PresetModels / ModelOptions / SelectedModel 可写属性 /
  RefreshModelOptions；RunAgentLoopAsync 传会话模型）、
  `Dome/Aster/Views/AdminView.axaml`（工具行 ComboBox + modelPick ghost 样式）、`CHANGELOG.md`、`README.md`。
- **实现要点**
  - **服务层向后兼容**：modelOverride 是带默认值的尾参（null），非空时覆盖请求体的 model 字段，
    留空走设置页默认；只有 AdminViewModel 一处调用方，改签名无兼容面。
  - **SelectedModel 是可写属性而不是命令**：getter 回落链「会话字段 → 设置默认 → 预设首项」，
    setter 只写 `_sessionModel` 并发通知——与 Filter/TaskFilter 的 setter 模式一致，
    ComboBox 的 SelectedItem TwoWay 直绑即可，不需要 SelectionChanged 事件或逐项 Command
    （ComboBoxItem 由 ComboBox 内部生成，本来也没法逐项绑命令）。
  - **样式只覆盖控件自身**：`ComboBox.modelPick` 改透明底/无边框/26 高/13 号字/Text2 前景，
    不动全局 ComboBoxItem 样式，下拉弹层仍走全局紧凑菜单样式；
    悬停态用 `:pointerover /template/ Border#ContentBackground` 只描 8% 白，不出现突兀色块。
  - **已知边界**：切换到其它服务商（如通义）时，预设里的 deepseek 系模型名会得到 HTTP 4xx
    并以红色错误消息呈现——预设清单离线可用的代价，选设置里填的模型名即可避开。
- **验证**：`dotnet build` 0 warning 0 error；冒烟运行 12 秒启动无异常。

### 15. 工作台 Agent 接入真实大模型：OpenAI 兼容流式对话 + Function Calling 操作工作区 — `0dae53f`

- **改动**：工作台的 Agent 对话从「600ms 定时器 + 固定话术」升级为真实大模型：
  走 OpenAI 兼容协议（DeepSeek / Kimi / 通义兼容模式 / Ollama 均可），SSE 流式逐字输出、
  带多轮上下文；模型可通过 Function Calling 真实操作工作区——查询客户 / 待办、新建客户、
  新建待办、审批通过 / 退回，操作与界面按钮共用同一批数据函数并写回 `local-cache.json`。
  设置浮层新增「AI 模型」分区（API Key / Base URL / 模型名 + 保存），配置即存即生效。
- **文件**：`Dome/Aster/Services/AiChatService.cs`（新增，约 380 行）、
  `Dome/Aster/Services/LocalCache.cs`（+AiConfigCache 配置段与 Normalize 兜底）、
  `Dome/Aster/ViewModels/AdminViewModel.cs`（ChatMessage 改可变通知；Send 换成 AI 循环；
  新增 5 个工具与调度器；SaveWorkspace 跳过错误消息）、
  `Dome/Aster/ViewModels/SettingsViewModel.cs`（新增 BuildAi 分区）、
  `Dome/Aster/Views/AdminView.axaml(.cs)`（流式消息样式 + ChatContentAppended 滚动事件）、
  `README.md`、`CHANGELOG.md`。
- **实现要点**
  - **零第三方依赖不破**：AI 客户端只用 BCL 的 `HttpClient` + `System.Text.Json` 手写 SSE
    行解析（`data:` 行 → 增量 chunk → `[DONE]`），tool_calls 按 `index` 聚合分片
    （id / name / arguments 都可能拆在多个 chunk 里）；`HttpClient.Timeout=Infinite`，
    单轮 120s 超时用 linked CTS 控制，否则长流式回复会被默认 100s 腰斩。
  - **流式上屏**：`ChatMessage` 从 init-only 改为继承 ObservableObject（Text 可变可通知）；
    增量先缓冲，UI 线程 33ms 合并刷新一次（onDelta 在后台读流线程，经
    `Dispatcher.UIThread.Post` 搬运），首段增量整段替换「正在输入…」占位文案；
    正文变了但集合没变，View 滚动靠 VM 新事件 `ChatContentAppended`（code-behind 订阅）。
  - **工具循环**：`Send` → 快照历史（system 提示 + 最近 20 条，剔除错误 / 工具回执 / 占位消息）
    → 每轮流式请求；返回 tool_calls 则先把 assistant 声明回放进对话（服务端按
    tool_call_id 对账），执行后以 role=tool 回传结果，最多 8 轮防死循环。
    五个工具：`list_customers` / `list_tasks` / `create_customer` / `create_task` / `decide_task`，
    执行体复用 `RefreshCustomers` / `RefreshTasks` / `NextCustomerCode` / `SaveWorkspace` 等现有函数，
    刻意不走 `Go(page)`、不碰表单字段（Form*）。
  - **防重入**：`IsAiBusy` 是唯一真值，`SendCommand` 的 CanExecute 绑它
    （RelayCommand 无自动重查，setter 里手动 `RaiseCanExecuteChanged`），Enter 路径在 Send 内再拦一道。
  - **缓存兼容**：`LocalCacheData` 新增 `Ai` 段但 `schemaVersion` 保持 2——旧文件缺字段时
    反序列化取属性默认值，任何一次落盘会把 `ai` 段补进文件，老数据无损升级。
  - **错误处理**：未配置 Key / 网络失败 / 超时都转成红色错误消息（`IsError`，作者名与正文变红），
    错误消息不落盘、不进上下文；工具参数非法返回结构化 `{ok:false}` 让模型自己纠正。
- **验证**：`dotnet build` 0 warning 0 error；冒烟运行 12 秒无异常退出，
  `local-cache.json` 自动补出 `"ai"` 段（默认 deepseek 端点），`schemaVersion` 仍为 2。
  真实对话需在「设置 → AI 模型」填入有效 API Key 后于工作台输入框验证。

### 14. 验证本地数据不入库 + 模拟首次启动自动建文件，还原误改的 BuildAvaloniaApp 可见性 — `76e3091`

- **改动**：三件事。① 排查「git 上传是否会把本地数据带上」——结论是不会，`Dome/Aster/AppData/`
  与 `samples/` 均已被仓库根 `.gitignore` 忽略，`git ls-files` 中没有任何 AppData 跟踪项，
  `git check-ignore` 确认 `local-cache.json` 命中第 49 行规则；② 把工作区一个遗留未提交改动
  （`BuildAvaloniaApp` 从 `public` 改成 `private`）还原为 `public`——Program.cs 头部注释明确约定
  该方法必须 public static，XAML 预览器与设计器要反射调用它；③ 模拟「clone 下来第一次跑」：
  备份现有缓存到 `samples/appdata-backup-1028/`（也被 gitignore，不会入库）→ 删除 `AppData/`
  → `dotnet build` + `dotnet run` → 程序自动创建全新默认结构的 `local-cache.json`。
- **文件**：`Dome/Aster/Program.cs`（仅还原可见性，1 行）、`CHANGELOG.md`；
  `samples/appdata-backup-1028/AppData/local-cache.json` 为旧数据备份（本地留存，不入库）。
- **实现要点**
  - 首次建文件的链路在 `LocalCache.LoadFromDisk`：文件缺失 → `new LocalCacheData()`（默认结构）
    → `WriteToDisk` 先 `Directory.CreateDirectory` 再原子写（.tmp + Move 替换），所以目录不存在也能直接跑。
  - 全新默认文件长这样：`schemaVersion=2`、`login.userName` 为空、`window` 宽高为 0
    （MainWindow 会回退 XAML 设计稿尺寸）、`workspace` 四个列表全空 —— 设置页/工作区都会走各自的空态。
  - `git status` 在清空 AppData 后完全干净，等价于 GitHub 上 clone 下来的原始状态，可直接复现本次验证。
- **验证**：`dotnet build` 0 warning 0 error；`dotnet run --no-build` 启动后 1 秒内
  `Dome/Aster/AppData/local-cache.json` 自动生成（310 B，默认结构，中文不转义），
  应用运行 13 秒无异常后正常关闭；`git status -sb` 无任何未跟踪/已修改条目。

## 2026-10-26

### 13. 客户支持编辑与删除（复用同一张表单 + 删除二次确认） — `5ae8d3d`

- **改动**：上一版只能新建，已有客户改不了也删不掉。本次在客户详情页头部加「编辑」「删除」两个按钮：
  编辑复用新建那张浮层（标题变「编辑客户」，字段预填现值），保存后原位替换并写回缓存；
  删除走一层更小的确认浮层，确认后移除该客户、退回列表页，**不会**连带删除关联待办。
  同时把详情页那个「让 Agent 更新资料」的占位按钮（实际只是跳到 Workflow，文案与行为不一致）换成「编辑」。
- **文件**：`Dome/Aster/ViewModels/AdminViewModel.cs`（+约 190 行：编辑目标 Id、删除确认状态、4 个命令、编辑分支）、
  `Dome/Aster/Views/AdminView.axaml`（详情页按钮组、表单标题/副标题改绑定、删除确认浮层）、`README.md`、`CHANGELOG.md`。
- **实现要点**
  - **一张表单两种模式**：用 `_editingCustomerId`（null=新建）区分，而不是维护两套字段。
    标题与副标题是计算属性 `CustomerFormTitle` / `CustomerFormHint`，切换目标时手发通知 —— 少发一次就会
    「打开编辑态标题还写着新建客户」。
  - **编辑必须重建对象**：`CustomerItem` 的字段都是 `init-only`，改不了原实例，所以按 Id 找到下标后造一个新对象替换；
    Id 原样保留，人员 / 核对 / 文件三个列表直接复用旧实例的引用（表单改不到它们，深拷贝反而会出现两份数据）。
  - **`Activities` 是 `IReadOnlyList`**，不能就地 `Insert`，要新建 `List` 把「更新客户档案」放第一条再 `AddRange` 旧记录。
  - **编号查重排除自己**：`c.Id != _editingCustomerId`，否则原样保存也会报「编号已存在」。
  - **编辑后刷新详情**：如果改的正是当前打开的那条，`Current` 仍指向旧对象，必须 `OpenCustomer(updated, _detailTab)`
    重进详情，标题与基础资料才会换成新值（页签保持不变）。
  - **删除后置空 Current**：先把 `Current = null` 再回列表，否则详情页还挂着已移除的实例，派生属性会显示旧名字。
  - **不级联删除待办**：待办是独立记录（可能还要留档），确认框里明确写了「关联的待办不会被一起删除」。
- **注意**
  - 删除不可撤销，没有软删除与回收站；要恢复只能重新填写。
  - 待办（TaskItem）目前只有「新建 + 审批」，还没有编辑与删除，属于下一步。
  - 客户详情四页签（人员 / 核对 / 文件）仍然只能通过编辑缓存文件维护，表单只管基础资料。
- **验证**：`dotnet build` 0 warning 0 error；临时自测（`ASTER_SELFTEST=1`，验证后已移除）跑完整链路：
  新建 → `customers=2`、进详情 → `IsDetail=True`、点编辑 → 标题「编辑客户」且字段预填、改名改城市保存 →
  `name=自测客户（已改名） city=洛阳 id 不变`、详情标题同步、最近更新首条为「更新客户档案」、
  点删除 → 确认框 `open=True target=自测客户（已改名）`、确认后 `customers 2→1 Current=null 回到列表页`；
  控制台无任何绑定错误。带一条客户数据启动程序 11 秒无异常。

### 12. 空工作区可在界面上新建写入（客户 / 待办 / 指标），数据全部留在本机 — `4299b56`

- **改动**：v2 把工作区数据搬进缓存文件之后，界面只能读不能写 —— 新环境第一次打开是一片空态，
  想有内容必须自己编辑 `local-cache.json`。本次补齐「写入端」：客户页加「＋ 新建客户」（6 行表单浮层）、
  审批页加「＋ 新建待办」（4 行表单浮层）、KPI 页加内联「新增指标」一行。三条路径都是
  **校验 → 改内存集合 → 刷新列表 → 整体写回缓存 → 关表单**，全流程只碰本机文件，不联网。
- **文件**：`Dome/Aster/ViewModels/AdminViewModel.cs`（+约 380 行：表单状态、7 个命令、3 个 Save 方法、编号生成）、
  `Dome/Aster/Views/AdminView.axaml`（两个模态浮层 + 三处入口 + 空态文案 + KPI 空态）、
  `.gitignore`（新增「示例数据」段：`samples/`、`*.sample.json`）、`README.md`、`CHANGELOG.md`。
- **实现要点**
  - **表单字段全存 string**：TextBox / ComboBox 绑的都是字符串，数字转换只在 `SaveCustomer` 里做一次
    （`TryParseCount`：空串当 0、非数字或负数退回报错）。这样「空表单直接保存」也能成功，非法输入不会被静默吃掉。
  - **写盘顺序固定**：先改集合、再 `RefreshXxx`、再 `SaveWorkspace()`、最后关表单。反过来会出现
    「文件里有、界面还没有」的一帧，且写失败时更难察觉。`SaveWorkspace` 走 `SaveSoon`（700ms 防抖、整体写回）。
  - **编号自增与查重**：`NextCustomerCode()` 扫现有 `KH-xxx` 取最大值 +1（三位补零）；用户手填编号会查重，
    重了就报错 —— 列表里两行同编号会让「按编号定位」全部指错。
  - **客户下拉随数据变**：`CustomerOptions` 首项固定「（不关联客户）」占位（ComboBox 不能绑 null 项），
    新建客户后调 `RefreshCustomerOptions()` 重建，并还原原来的选中项（选中项不在列表时退回第一项，避免空白）。
  - **新建待办后切回「待处理」筛选**：新任务状态固定「待处理」，如果用户正停在「全部记录」，不切回来就看不到刚建的那条。
  - **计算属性手动补通知**：`HasFormError` / `HasTaskFormError` / `HasMetricFormError` / `HasMetrics` 都不是自动属性，
    setter 里或使用处必须 `OnPropertyChanged` —— 否则空态提示与错误行不会出现/消失。
  - **KPI 用内联行而不是弹窗**：只有三个短字段，弹一层浮层反而更重；客户/待办字段多，才需要模态卡片。
- **注意**
  - 只能**新建**，还不能编辑或删除已有条目（`CustomerItem` 的字段都是 init-only，改值要新建实例再替换，
    属于下一步工作）；批量修改仍然建议关掉程序后编辑文件。
  - 新建客户的详情页四页签（人员 / 核对 / 文件）是空的，只写了一条「新建客户档案」到「最近更新」。
  - 界面上没有任何网络调用（全工程搜不到 `HttpClient` / `WebRequest` / `Socket`），
    缓存目录 `Dome/Aster/AppData/` 与示例数据 `samples/` 都在 `.gitignore` 里，不会推到远端。
- **验证**：`dotnet build` 0 warning 0 error；用临时自测入口（`ASTER_SELFTEST=1`，验证后已移除）构造
  `AdminViewModel` + `AdminView` 并依次执行三个新建命令，控制台无任何绑定错误，`local-cache.json` 里出现
  客户（`KH-001`）、待办（`待处理`）、指标三条记录；清空缓存后正常启动，工作区回到空态且不报错。

---

## 2026-10-25

### 11. 登录页默认预填 admin / admin — `d5d260d`

- **改动**：登录页的账号、密码输入框原来是空的，每次启动都要手敲一遍；现在默认预填 `admin` / `admin`，
  打开程序直接点「登录」就能进。登录校验逻辑没动（仍然只是判空，任意非空内容都能进，没有后端）。
- **文件**：`Dome/Aster/ViewModels/MainWindowViewModel.cs`（两个后备字段的初始值）、`README.md`、
  `Dome/Aster/README.md`（演示登录说明）、`CHANGELOG.md`。
- **实现要点**
  - 只改 `_userName` / `_password` 的字段初始化器，不碰 `Login()`：预填是「省一次输入」，不是「加了账号校验」。
  - 账号的默认值会被「记住我」覆盖：`RestoreRememberedLogin`（构造函数步骤 0）在缓存里存在 `login.userName`
    且 `remember` 为 true 时直接写 `_userName`（不走 setter），所以老用户看到的仍是自己上次的账号。
  - 密码不进本地缓存，所以每次启动都从 `admin` 起步；配合 `PasswordChar` 的遮罩，界面上显示为圆点。
- **注意**
  - `LogoutCommand` 本来就不清空 `UserName` / `Password`，所以退出登录回到登录页时看到的是上次输入的值，
    而不是这两个默认值 —— 这是原有行为，本次没有改动。
  - 想改默认账号，改这两个字段即可；不要顺手把 `Login()` 里的判空改成硬编码比对，那会让「任意非空都能进」这条演示前提失效。
- **验证**：`dotnet build` 0 warning 0 error；删掉本地缓存后启动，登录页账号框显示 `admin`、密码框显示 6 个圆点；
  直接点「登录」，420ms 后进入工作台（空数据态）——全程没有键盘输入。

### 10. 工作区改为「本地数据驱动」：清空内置演示数据，界面全部从缓存文件读取 — `6268b34`

- **改动**：上一版只把「用户操作过的状态」（审批结果、对话）写进缓存，客户、客户详情、KPI 仍是每次启动
  从 `BuildCustomers` / `BuildTasks` / `BuildMetrics` 硬编码重建。本次把这批数据整体搬进
  `local-cache.json` 的 `workspace` 段，删掉全部硬编码演示数据，界面改成「读文件 → 渲染」：
  **文件里有什么就画什么，没有就是空的**（各页有对应空态提示）。同时把已有缓存文件清空（换结构版本，见下）。
- **文件**：
  - 改 `Dome/Aster/Services/LocalCache.cs`：结构版本 1 → 2，新增 `workspace`（customers / tasks / metrics / messages）
    与 `CustomerCache`、`ActivityCache`、`StaffCache`、`RunCache`、`FileEntryCache`、`MetricCache`；
    `TaskStateCache` 升级为完整的 `TaskCache`（标题 / 类型 / 客户 / 期限也进文件）；`Normalize` 改成逐层兜底
  - 改 `Dome/Aster/ViewModels/AdminViewModel.cs`：删 `BuildCustomers` / `BuildTasks` / `BuildMetrics`（约 130 行），
    新增 `LoadWorkspace` / `SaveWorkspace` / `ToCustomerItem`；`FillDetail` 从「按档案字段编造行」改成「复制缓存里的行」；
    `CustomerItem` 增加 `Activities` / `Staff` / `Runs` / `Files` 四个只读列表；新增 `HasCustomers` / `HasTasks` 与空数据守卫
  - 改 `Dome/Aster/Views/AdminView.axaml`：客户列表、审批列表各加一条空态提示（`!HasCustomers` / `!HasTasks`）
  - 改 `Dome/Aster/ViewModels/MainWindowViewModel.cs`：二级导航「工作台」的写死徽标 `"12"` 去掉（改为 null）
  - 改 `README.md`：本地数据存储章节改成完整 schema + 维护说明；已知限制同步更新
- **新的缓存结构（schemaVersion = 2）**
  ```jsonc
  "workspace": {
    "customers": [{ "id","name","short","code","type","owner","city","contact","service","tax",
                    "file","date","state","people","pending",
                    "activities":[…], "staff":[…], "runs":[…], "files":[…] }],
    "tasks":    [{ "id","title","kind","customer","due","status","note" }],
    "metrics":  [{ "title","value","note" }],
    "messages": [{ "author","text","isUser" }]
  }
  ```
- **实现要点**
  - **一个入口读、一个入口写**：`LoadWorkspace()` 只在构造函数里跑一次，把缓存映射成
    `_allCustomers` / `_allTasks` / `Metrics` / `Messages`；`SaveWorkspace()` 在发消息、审批之后
    把五个集合整体写回（`LocalCache.SaveSoon` 防抖）。不做增量写，因为文件才几十 KB，
    一次原子写比维护五份局部写更不容易出错。
  - **详情数据跟着客户走**：`CustomerCache` 自带 `activities / staff / runs / files` 四个数组。
    `FillDetail` 现在只是把这四张表复制进 `ObservableCollection`，不再按 `Code[3..]`、`People` 这些字段
    现编批次号与人员行 —— 想改某个客户的核对记录，改 JSON 即可，不用碰代码。
  - **空数据是合法状态**：构造函数 `if (_allCustomers.Count > 0) OpenCustomer(...)`；
    `Select` 里六个详情 key 统一走 `OpenFirstCustomer(tab, hasFirst)`，没有客户就退到客户列表页。
    这两处是「客户为空」唯一会抛异常的地方（下标访问），其余集合都是 `foreach`。
  - **计算属性补通知**：`HasCustomers` 在 `RefreshCustomers` 里补发，`HasTasks` 在 `RefreshTasks` 里补发 ——
    否则审批完最后一条，空态提示不会出现。
  - **结构版本不一致即清空**：老文件是 `schemaVersion: 1`，加载时判定版本不符 → 备份成 `.bak` → 重建成空工作区。
    也就是说这次「清空已有数据」不是靠手删文件，而是数据结构升级的必然结果（本地实测确实生成了 `.bak` 之外的干净文件）。
- **注意**
  - **运行期改文件会被覆盖**：程序在发消息 / 审批时会把内存里的整份 `workspace` 写回，
    手工维护 `local-cache.json` 请先关掉程序。
  - 界面上能改的只有「审批状态」与「对话」；客户 / 人员 / 核对 / 文件 / KPI 只能靠编辑文件补充。
  - `state` / `status` / `tone` 是配色开关（琥珀 = 待处理类、绿 = 完成类、红 = 退回类、其余灰），
    写错值不会报错，只会落到灰色兜底。
  - 壳层的「项目 · 青禾 · 月度核对」「最近会话」仍是 `MainWindowViewModel` 里的静态导航条目，
    不属于数据（点了会落到第一个客户或工作台），本次只摘掉了那个假的待办数徽标。
  - 卡片上的「3 项 / 12 条」胶囊一并改成中性的「查看」，避免与空列表或实际数量打架。
- **验证**：`dotnet build` 0 warning 0 error。三步截图验证：
  ① 空缓存：工作台无对话、客户页「本地缓存里还没有客户…」、审批页「当前筛选下没有待办…」、客户计数「共 0 个客户」；
  ② 手工往 `workspace` 写 1 个客户（含 2 条最近更新 / 2 条人员 / 1 条核对 / 2 个文件）+ 2 条待办 + 2 条 KPI + 1 条对话，
     重启后工作台对话、客户列表行、详情四个页签、审批两张卡、KPI 两行全部按文件渲染；
  ③ 在界面上点「通过审批」，回读文件确认 `tasks[0].status` 变 `已通过`，且 `customers` / `staff` / `metrics`
     内容完好无损（整体写回没有丢字段）。验证用的「跳过登录」临时代码已还原，工作区 `git status` 干净。

### 9. 新增本地缓存文件作为本地数据存储（local-cache.json） — `d3f29d7`

- **改动**：工程此前是「零持久化」——所有界面状态关掉程序就没了，「记住我」勾不勾一样（只是绑定占位），
  设置页写的「数据目录 / 数据存储：本地文件 · JSON」也是没有实现的文案。本次补上一个真正的本地缓存层，
  用一个 JSON 文件当轻量本地数据库，并把四处真实状态接上去；同时把该文件加进 `.gitignore`，不随代码上传 GitHub。
- **文件**：
  - 新增 `Dome/Aster/Services/LocalCache.cs`（约 500 行，含 5 个数据模型 + 读写门面，唯一新增文件）
  - 新增运行期产物 `Dome/Aster/AppData/local-cache.json`（不入库，首启自动创建）
  - 改 `Dome/Aster/ViewModels/MainWindowViewModel.cs`：「记住我」落盘与回填（构造函数步骤 0）
  - 改 `Dome/Aster/ViewModels/AdminViewModel.cs`：对话记录与审批结果落盘与回填（构造函数步骤 3.5）
  - 改 `Dome/Aster/Views/MainWindow.axaml.cs`：窗口尺寸关闭时保存、启动时恢复，并在首次显示后收进当前屏幕
  - 改 `Dome/Aster/ViewModels/SettingsViewModel.cs`：数据目录改真实路径、缓存占用显示实际大小、清理按钮接线
  - 改 `.gitignore`：新增「本地缓存 / 运行时数据」段，忽略 `Dome/Aster/AppData/`
  - 改 `README.md`：新增「本地数据存储」章节并更新目录结构
- **缓存文件结构**（字段名 camelCase，中文不转义，可直接用编辑器打开）：
  ```json
  {
    "schemaVersion": 1,
    "appVersion": "1.0.0.0",
    "updatedAt": "2026-10-25T18:58:24+08:00",
    "login":      { "remember": true, "userName": "张客服", "lastLoginAt": "..." },
    "messages":   [ { "author": "张客服", "text": "...", "isUser": true } ],
    "taskStates": [ { "id": "t1", "status": "已通过", "note": "..." } ],
    "window":     { "width": 1600, "height": 900 }
  }
  ```
- **实现要点**
  - **存放位置分三档**：`ASTER_DATA_DIR` 环境变量 > 从可执行文件目录往上找 `Aster.csproj`（找到＝开发态，写仓库内
    `AppData/`，正好被 gitignore 忽略）> 用户漫游目录 `%APPDATA%\Aster\Cache`（发布态）。
    所以**开发态和发布包用的是两个不同的文件**，调试改坏的数据不会污染用户环境。
  - **原子写**：先写 `local-cache.json.tmp` 再 `File.Move(..., overwrite: true)` 替换目录项，
    任何时刻读到的都是完整文件；写入失败只记录 `LastError`、绝不抛异常，缓存写不进去也不该让界面崩。
  - **损坏自愈**：文件缺失 → 建默认结构；`schemaVersion` 不符 → 备份成 `.bak` 后重建；JSON 解析失败 → 同样备份重建；
    反序列化后再 `Normalize` 一遍（把 `"login": null` 这类手改出来的值补成对象，过滤列表里的 null 元素）。
  - **延迟合并写**：`SaveSoon()` 用 700ms 防抖计时器合并连续改动（搜索框那种逐字改状态的场景不会产生一串小文件写），
    `Save()` 同步落盘用于「必须当场记住」的时刻（登录成功、退出登录、关窗口）。
  - **各处接了哪些状态**
    - 「记住我」：勾选即存账号、取消勾选即清账号；登录成功再同步写一次（`Save`，不用防抖）。**只存账号名，不存密码**。
    - 工作台对话：发消息与 Agent 回复各存一次，存的是全量快照，重启后 `Clear` + 回放还原完整顺序。
    - 审批结果：`Decide()` 后把 `_allTasks` 全量状态写成快照，读取时按 `Id`（t1~t7）回填，
      所以种子任务顺序变了也不会串行；回填必须卡在 `RefreshTasks()` 之前，否则会先渲染七条再删。
    - 窗口尺寸：`Closing` 时保存（只在 `WindowState.Normal` 下存），构造时恢复；`OnOpened` 再按屏幕
      `WorkingArea / Scaling` 收一次，避免 4K 屏存的 3000 宽换到笔记本上开屏即超框。
      只记尺寸不记位置——窗口本来就是居中打开，记坐标换显示器后可能落在屏幕外。
  - **设置页**：「存储与备份 → 数据目录」显示的是 `LocalCache.DirectoryPath`（与程序真正写入的位置一致），
    「缓存占用」的描述按文件实际大小现拼，「清理」按钮绑 `LocalCache.Reset()`（删文件后立刻按默认结构重建）。
- **注意**
  - `AppData/` 不入库：`clone` 下来没有这个目录是正常的，首次运行自动创建。想让某台机器的数据隔离，
    设 `ASTER_DATA_DIR` 指到别处即可（比如演示前清空、或把数据放到网盘目录）。
  - 改 `LocalCacheData` 的字段含义时**必须**把 `CurrentSchemaVersion` +1，否则老文件会被当成同版本读进来；
    本项目不做逐版本迁移，版本不符就整份重建（缓存里全是可再生的展示数据，重建比写迁移划算）。
  - 「清理」按钮点了文案不会刷新：`SettingRow.Desc` 是 init-only 且没有通知，要看到新大小得重开设置浮层，动作本身即时生效。
  - 回填只覆盖「用户操作过的部分」：客户 / 人员 / 核对表 / KPI 仍是每次从内置种子重建，
    所以清掉缓存文件后界面会回到出厂演示状态，这两件事都是预期行为。
  - `LoginCache` 里没有密码字段，这是刻意的——哪怕项目其它地方把密码明文放在内存里，也不落盘。
- **验证**：`dotnet build` 0 warning 0 error；实机跑了一遍完整闭环（截图 + 直接查 JSON 双确认）：
  ① 首启自动生成 265 字节的默认文件；② 手工写入 `userName` 后重启，登录框自动回填账号；
  ③ 写入 `window 1600×900` 后重启，窗口按该尺寸居中打开；④ 登录（账号被写回缓存）→ 发一条对话（
  `messages` 变 5 条）→ 审批通过一条（`taskStates` 7 条，t1 = 已通过）→ 关窗口（`updatedAt` 更新）；
  ⑤ 再重启：登录框仍是上次账号、工作台 5 条对话齐全、审批页显示「6 项」且已通过的那条不见了；
  ⑥ `git check-ignore -v Dome/Aster/AppData/local-cache.json` 命中 `.gitignore:49`，`git status` 里看不到该目录。

## 2026-10-23

### 8. 补充应用图标（PNG / ICO / ICNS 三套 + macOS .app 打包）

- **改动**：按设计稿（暗紫圆角方块 + 五瓣粉花）给桌面程序补上应用图标。之前工程里没有任何图标资源：
  exe 是默认的 .NET 图标、窗口没有 `Icon`、macOS 只能顶着 dotnet 的通用图标跑。
- **文件**：
  - 新增 `tools/make_icon.py`（图标唯一源，用 numpy + Pillow 按参数绘制）
  - 新增 `Dome/Aster/Assets/`：`aster-1024.png`、`aster-512.png`、`aster-256.png`、`aster-128.png`、`aster.ico`、`aster.icns`
  - 新增 `Dome/make-app.sh`（macOS `.app` 打包，含 Info.plist 与 ad-hoc 签名）
  - 改 `Dome/Aster/Aster.csproj`：`<ApplicationIcon>Assets/aster.ico</ApplicationIcon>` + `<AvaloniaResource Include="Assets/**" />`
  - 改 `Dome/Aster/Views/MainWindow.axaml`：根节点加 `Icon="/Assets/aster-256.png"`
  - 改 `Dome/Aster/App.axaml`：根节点加 `Name="Aster"`（否则 macOS 菜单栏显示 "Avalonia Application"）
  - 改 `README.md`（新增「应用图标」章节与打包说明）、`Dome/Aster/README.md`（图标小节）、`.gitignore`（忽略 `Dome/dist/`）
- **实现要点**
  - **图标是脚本画的，不是位图**：几何与 `Controls/Visuals.cs` 的 `LogoMark` 同源（5 片椭圆绕中心旋转 72°），
    每瓣由「外层深色椭圆 + 内层浅色椭圆」两层叠成，再用 `lit = (花瓣轴 · 光方向 + 1) / 2` 按左上光源分档，
    所以左下两瓣最亮、右下最暗——和参考稿的明暗关系一致。花瓣还带一个 `taper` 收腰系数（内端窄、外端宽），
    否则标准椭圆会把瓣与瓣之间的缝堵死，整朵花看起来是五个圆。
  - **每档尺寸单独渲染**：`render(size)` 内部按 `size*4` 画再 LANCZOS 降采样（≥512 用 2 倍），
    所以 16px 图标也是从 64px 画布缩下来的，不是拿 1024 硬缩。
  - **三套格式各司其职**：`.ico` 给 Windows exe 内嵌（资源管理器 / 任务栏 / Alt+Tab 读的是它），
    `.png` 给 `Window.Icon`（三端都由 Skia 解码，不依赖平台 ICO 解码器），`.icns` 给 macOS 的 `.app`。
  - **macOS 必须打 .app 才有 Dock 图标**：`Window.Icon` 只影响窗口，Dock 读的是 bundle 的 `Info.plist` +
    `Contents/Resources/*.icns`。`make-app.sh` 默认自带运行时（约 80MB，双击即开），
    `SELF_CONTAINED=0` 则走框架依赖并在 `Info.plist` 里写 `LSEnvironment.DOTNET_ROOT`
    （Finder 启动的进程拿不到终端里的环境变量，不写就报 "You must install .NET"）。
- **注意**
  - 改动图标只改 `tools/make_icon.py` 顶部的常量再重跑，别直接编辑 PNG；重跑会覆盖 `Assets/` 下全部产物。
  - `Assets/**` 必须在 csproj 里声明为 `AvaloniaResource`，否则 `Icon="/Assets/..."` 运行期取不到、窗口直接抛异常（编译不报错）。
  - `cp -R "$OUT/publish/" "$BUNDLE/Contents/MacOS/"` 这种写法会把 publish 目录整个塞进去（变成 `MacOS/publish/Aster`），
    系统报「可执行文件缺失」；要多写一个 `/.`（脚本里已注明）。
  - 图标是粉色系，与界面主色 `#6C8BFF` 蓝不同调；如需统一，改 `PETAL_DARK` / `PETAL_LITE` / `INNER_*` 四个常量即可。
  - macOS 菜单栏 / Dock 悬停标题读的是 `Application.Name`，不是 `Info.plist` 的 `CFBundleName`；
    只改 plist 会看到名字仍是 "Avalonia Application"（已改 App.axaml）。
  - `SELF_CONTAINED=1` 首次构建要下 `Microsoft.NETCore.App.Runtime.osx-arm64` 运行时包，
    走 `nuget.azure.cn` 镜像时可能很慢甚至卡住（本次实测卡了 8 分钟仍未下完，最终改用框架依赖模式验证）。
- **验证**：`dotnet build` 0 warning 0 error；`dotnet run` 启动无资源加载异常；
  `make-app.sh` 产物 `Aster.app` 的 `Info.plist` 通过 `plutil -p` 校验、`sips` 读出 icns 为 1024×1024、
  `codesign -dv` 显示 adhoc 签名；`open Aster.app` 后 `lsappinfo` 报告 `pid` 存在且 `LSDisplayName` 为 "Aster"；
  ICO 内含 7 档尺寸（Pillow 校验）。

---

## 2026-10-22

### 7. 注释加深到「逐段讲解」级（第二轮） — `0e32a11`

- **改动**：在上一轮基础上把 36 个源文件的注释再挖一层，新增约 5180 行注释；全工程注释占比达到 40%（14723 行文件中 5983 行注释）。**代码仍然零改动**。
- **文件**：与第 5 条同一批文件（`Program.cs`、`App.axaml(.cs)`、`Controls/*`、`ViewModels/*`、`Views/*`、`Styles/*`）；README 未改。
- **新增的注释形态**
  - C#：每个方法体加「前置状态 → 步骤编号 → 后置状态 / 谁被通知」三段式；魔法数字与颜色值标出来历（如 `1080` 是内容列上限、`420ms` 是模拟登录耗时、`2` 是画刷留白阈值）；派生属性标注「哪个 XAML 绑定它」；定时器标出时序与防重入判断。
  - XAML：每个主要区块补 ASCII 结构示意（标注固定/伸缩区域）；每条关键绑定注明来源属性与写入方；`ControlTheme` 补模板元素命名表（`Border#Bg` / `Ellipse#Knob` / `Path#Check` 谁被哪个伪类命中）；尺寸链逐个相加核算（如设置浮层 940→764→716→684→360）。
  - `Styles/Theme.axaml` 增补令牌速查表（层级 · 键名 · 用途）与「取值优先级」说明；`Controls.axaml` 标注每个样式类的实际使用方。
- **纠正的既有注释（本轮重点，之前写错的事实）**
  - **编译绑定**：项目 `AvaloniaUseCompiledBindingsByDefault=false` 且没有 `x:CompileBindings="True"`，所以 `x:DataType` 只是类型提示，属性名写错不会编译报错、只在运行期留下绑定错误日志。`HomeView` / `LoginView` / `MarkdownView` / `ShellView` 四处原先写成「编译期校验」的描述已改正。
  - `MainWindowViewModel._moduleKey` 的说明从「只有 admin 一个模块」改为实际存在的四个 key（workbench / approval / clients / workflow）。
- **本轮记录下来的实现现状（注释里标注，未改代码）**
  - `SubNavWidth` 恒为 260、`_moduleKey` 只写不读、`IsSubNavUserVisible` 恒 true 导致 rail 底部头像恒不可见；`SelectModule(item, bool raise)` 的 `raise` 与 `if (item.IsSettings)` 分支都已走不到。
  - `HomeViewModel` 创建后从未赋给 `ActiveView`；`MarkdownViewModel` / `MusicViewModel` 无实例化点 —— 三个页面的模板目前是备用状态。
  - 双写 `IsVisible`：`MarkdownView.ApplyMode` 在后台直接改 `PreviewPane.IsVisible`，与 XAML 的 `ShowPreview` 绑定争夺同一属性。
  - `Theme.axaml` 里 `Bg0` / `Bg4` 全工程零引用；`Bg1` 才是实际窗口底色。
- **注意**：`Dome/Aster/Controls/Icons.cs`、`Charts.cs` 在改动前工作区存在一份与 HEAD 不同的格式化差异（`Sliders` 被压成一行、using 顺序、若干冗余括号），已统一回 HEAD 的写法，功能等价；如果那份格式化是在别处手动做的，需要重新应用一次。
- **验证**：`dotnet build` 0 warning 0 error；剥离注释后与 HEAD 逐行比对，36 个文件代码行完全一致。

### 6. 按 HR Agent 原型重构工作区界面（布局 + 内容） — `d2a2811`

- **改动**：参考 `HR Agent · 会话与交叉验证工作空间.html` 交互原型，把原来的「管理」模块整体换成工作区结构：侧栏四个入口（工作台 / 协作与审批 / 客户工作空间 / Workflow）+ 项目与最近会话，内容区六个页面；保留原有深色令牌与 `#6C8BFF` 强调色，不引入新配色。
- **文件**：`ViewModels/AdminViewModel.cs`（重写）、`Views/AdminView.axaml`（重写）、`Views/AdminView.axaml.cs`、`ViewModels/MainWindowViewModel.cs`、`README.md`。
- **页面结构**
  - **工作台**：今日工作台标题 + 三张快捷卡（新建项目 / KPI 考勤 / 待办事项）+ Agent 对话 + 底部指令输入框；卡片点击分别进入客户列表 / KPI / 审批。
  - **客户工作空间**：标题组 + 说明条 + 搜索框 + 筛选下拉 + 计数 + 8 列表格（名称编号 / 服务类型 / 在册人员 / 负责人 / 最近核对 / 待处理 / 最后更新 / 查看），4 条演示客户。
  - **客户详情**：返回链接 + 客户头部（图标 / 名称 / 编号·类型·负责人 / 只读资料）+ 四个页签（基础资料＝档案字段 + 最近更新；人员记录；核对记录；文件与产出），数据在 `AdminViewModel.FillDetail` 里按客户生成。
  - **协作与审批**：待处理 / 全部记录切换 + 任务卡（标题 / 状态 / 客户·类型·期限）+ 通过审批 / 退回补充按钮，处理后状态与说明即时更新。
  - **Workflow**：步骤指示条 + 三步（01 确认客户·板块·所属期并勾选虚构样例 → 02 结果表 + 复核说明 → 03 归档），未勾选或未填说明时给行内错误提示。
  - **KPI 与考勤**：三行指标 + 口径说明条。
- **壳层改动**：`MainWindowViewModel` 一级 rail 改为 工作台 / 审批 / 客户 / Workflow / 设置；二级栏改为工作台、协作与审批、客户工作空间、Workflow 四个入口 + 「项目 · 青禾」+ 最近会话；`SelectSubNav` 增加 `SyncRailSelection`，点侧栏或项目/会话时一级 rail 高亮同步。
- **宽度规则不变**：六个页面内容列仍由 `AdminView.OnContentSizeChanged` 统一赋 `min(1080, 可视宽度 - 96)` 并居中。
- **注意**：`AdminViewModel.Select` 现在支持 `detail / detail-people / detail-runs / detail-files` 等深链 key，方便从任意入口直接落到客户详情的某个页签；演示数据全为内置对象，没有持久化与后端。
- **验证**：`dotnet build` 0 warning 0 error；逐页截图检查工作台 / 客户列表 / 客户详情 / 审批 / Workflow（含点击进入第二步）/ KPI 共 6 个页面。

### 5. 为全部 C# 与 axaml 源码补充中文注释 — `dec36d8`

- **改动**：工程内 37 个源文件（26 个 `.cs`、11 个 `.axaml`，含 Styles 两个）全部补注释，新增约 2470 行注释，**代码本身零改动**。
- **文件**：`Program.cs`、`App.axaml(.cs)`、`Controls/*`（Icon / Icons / Charts / Visuals / MarkdownPreview）、`ViewModels/*`（ObservableObject / Items / MainWindow / Admin / Settings / Home / Markdown / Music）、`Views/*`（MainWindow / ShellView / LoginView / BootView / AdminView / SettingsOverlay / HomeView / MarkdownView / MusicView 及其 code-behind）、`Styles/Theme.axaml`、`Styles/Controls.axaml`。
- **注释层次**
  - 每个文件顶部一段文件头：职责、在启动/渲染链中的位置、与谁协作、容易踩的坑。
  - 类型与方法用 `/// <summary>`（必要时 `/// <remarks>`）：说清输入输出、副作用、何时该用。
  - 非直观处加行内块注释，解释「为什么这样写」而不是复述代码，重点覆盖 Avalonia 机制：`AffectsRender`、`StyledProperty` / `AddOwner` 属性继承、`DataTemplate` 按类型隐式匹配、`ControlTheme` 与样式类的分工、`/template/ #Name` 模板内选择器、`Classes.selected` 条件类、无边框窗口的拖动与八向缩放热区、`DispatcherTimer` 动画与启停时机、`ObservableCollection` 复用实例、派生属性必须手动补通知。
  - `Styles/Theme.axaml` 补了令牌命名体系（Bg0~Bg4 / Text0~Text3 / Line 三档 / W 系列白度）、渐变各自用途、字体栈顺序，以及「资源层 vs 样式层」的分工。
- **排查到的两个「注释与实现不符」点（已在注释里如实标注，未改代码）**
  - `LoginCommand` 的 `CanExecute = !IsBusy` 实际不会让登录按钮变灰：`RelayCommand` 从不触发 `CanExecuteChanged`，而 `Button` 只在挂上 `Command` 时查询一次。要生效需在 `IsBusy` 变化时调 `RaiseCanExecuteChanged()`。
  - `MusicViewModel.FilterCommand` 收到的是 `SegmentItem` 但按 `string` 解析，页签筛选实际永远落到「全部」，选中态也不更新。
- **注意**：本项目 axaml 的文件头注释放在 `<?xml?>` 声明之后、根元素之前，XamlX 可正常编译（已 `dotnet build` 验证 0 警告 0 错误）；`MainWindowViewModel` 里「自动变灰」那段旧注释已同步改为准确表述。
- **验证**：`dotnet build` 通过；另用脚本剥离全部注释后与 HEAD 逐行比对，37 个文件代码行完全一致。

### 4. 新增本变更记录

- **改动**：新增 `CHANGELOG.md`，把当天的改动补记进来，并在根 `README.md` 的目录结构里登记。
- **文件**：`CHANGELOG.md`（新增）、`README.md`。
- **注意**：后续每次改动都要同步更新本文件，再一起提交。

### 3. 工作台与列表页内容列随窗口宽度自适应 — `5f35586`

- **改动**：原来工作台卡片固定 `Width=840`、底部输入框固定 `Width=800` 且额外 `Margin="80"`，列表页卡片同样固定 840 并整体靠左。窗口一放大，内容全挤在左侧、右侧大片留白。现在内容列按窗口宽度计算并居中。
- **文件**：`Dome/Aster/Views/AdminView.axaml`、`AdminView.axaml.cs`。
- **实现要点**
  - 列宽规则统一为 `min(1080, 可视宽度 - 96)`（左右各 48 留白），超出上限居中，不再继续拉宽。
  - 工作台：`ScrollViewer` 内套一层撑满的 `Grid`，`WorkbenchContent`（内容 `StackPanel`）与 `WorkbenchComposer`（输入框 `Border`）同宽居中。
  - 列表页（客户工作空间 / 信息导入中心 / 账号与权限共用 `IsSection` 分支）：`SectionContent` 居中，卡片去掉固定宽度随列拉伸。
  - 卡片由固定 `Height=142` 改为 `MinHeight`；末列用 `Margin="0,0,-20,0"` 抵消右间距。
  - 宽度赋值统一走 `AdminView.OnContentSizeChanged`（工作台与列表页两个宿主都挂 `SizeChanged`）。
- **注意**：判断宽度是否变化时**必须先判 `double.IsNaN(Width)`**，因为 `Math.Abs(NaN - w) > 0.5` 恒为 `false`，首次赋值会被整段跳过（这个坑实际踩过，表现为输入框只有 300 多像素宽）。
- **验证**：1320×850 与 1700×850 两种窗口截图，窄窗内容 890 居中、宽窗 1080 居中。

### 2. 无边框窗口支持鼠标拖拽缩放 — `d3c72c8`

- **改动**：窗口是无边框透明窗口（`SystemDecorations=None`），系统不提供可拖拽的边框，之前无法调整大小。
- **文件**：`Dome/Aster/Views/MainWindow.axaml`、`MainWindow.axaml.cs`。
- **实现要点**
  - 在根 `Panel` 末尾叠一层无背景 `Grid`，放 8 个 `Border.grip` 透明热区：四边 6px、四角 14×14（四角声明在四边之后，保证角落优先命中）。
  - 各热区设 `Tag`（North/South/West/East/NorthWest…）与对应光标，`OnResizePressed` 解析成 `WindowEdge` 后调用 `BeginResizeDrag`，交给系统处理（保留吸附等原生行为）。
  - `MinWidth/MinHeight` 由 `1080×700` 改为 `1320×850`，即以当前设计稿尺寸为下限，只能放大；最大化状态下忽略热区。
- **注意**：登录页和主界面是同一个 `MainWindow` 的两个视图，所以缩放逻辑一处实现、两处生效。

### 1. 登录页支持拖拽移动窗口 — `41a4a75`

- **改动**：拖拽逻辑原先只在 `ShellView` 的自绘标题栏里，登录阶段整个窗口拖不动。
- **文件**：`Dome/Aster/Views/LoginView.axaml`、`LoginView.axaml.cs`。
- **实现要点**
  - 根 `Grid` 挂 `PointerPressed`：按住左键调用 `Window.BeginMoveDrag`，双击切换最大化/还原。
  - `IsInteractive` 排除 `Button`（`CheckBox` 继承自 `ToggleButton`→`Button`，一并覆盖）、`TextBox`，以及 `Border.field` 输入框外框（点输入框内边距应聚焦而不是拖窗口）。
  - 事件挂在内层 `Grid` 而不是根 `Panel`——弹窗遮罩是根 `Panel` 的兄弟节点，这样点弹窗不会冒泡上来把窗口拖走。
- **注意**：`ShellView` 与 `LoginView` 现在各有一份拖拽逻辑，如果后续要统一，可以抽成共用的 `WindowChrome` 辅助类。
