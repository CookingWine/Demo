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
| 当前模块 | 只有「管理」和「设置」两个一级入口；文档、音乐模块已在 rail 中移除，代码仍保留 |
| 页面状态 | 管理页有 工作台 / 列表页 / 流程页 三种状态，由 `AdminViewModel.SetState` 切换 |

---

## 2026-10-22

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
