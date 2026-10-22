using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Media;

// ============================================================================
// Markdown 文档模块的 ViewModel —— 文件树 + 编辑器 + 实时预览
// 三栏结构：左「文件树」= Groups；中「编辑区」= Tabs + Text；右「预览区」= MarkdownPreview 渲染 Text
// 数据来源：全部为内置假数据。BuildDocs 造出扁平列表 AllDocs，RebuildTree 再按 Filter + Search
//           派生成分组的 Groups；正文取自文件末尾的 Contents 字典（原始字符串字面量，逐字符勿改）
// 启用状态：该模块当前已从一级导航移除、未在大厅启用，代码保留待恢复，因此数据未接真实存储
// 易踩的坑：① Groups 每次都要整组重建（ItemsControl 无法直接换源）；
//           ② Mode 直接存中文枚举值，且 SetMode 必须连带通知 5 个派生属性；
//           ③ Text 的 setter 带副作用（重算行号、置脏），SelectDoc 里故意绕过 setter
// ============================================================================
namespace Aster.ViewModels;

/// <summary>
/// 文件树里的单个文档节点（假数据项）。
/// 继承 SelectableItem 拿到 IsSelected，XAML 通过 Classes.selected 绑定出选中背景。
/// </summary>
public sealed class DocItem : SelectableItem
{
    /// <summary>文件名（含 .md 后缀）。同时是 Tabs 去重与 Contents 取正文的主键。</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>所属分组名（Notes / Journal / Drafts / Archive），RebuildTree 据此分桶。</summary>
    public string GroupName { get; init; } = string.Empty;
    /// <summary>是否有未保存修改。文件树里名字右侧的 4px 琥珀色圆点绑定它。</summary>
    public bool IsDirty { get; init; }
    /// <summary>是否收藏，Filter = "star" 时作为筛选条件。</summary>
    public bool IsStarred { get; init; }
    /// <summary>是否最近访问，Filter = "recent" 时作为筛选条件。</summary>
    public bool IsRecent { get; init; }
    /// <summary>点击节点时执行的命令。由 VM 在 BuildDocs 时统一注入 SelectDocCommand，参数为自身。</summary>
    public ICommand Command { get; init; } = null!;
    /// <summary>列表展示名。两个分支目前返回同一值，是为将来「脏文档加 * 前缀」预留的扩展点。</summary>
    public string DisplayName => IsDirty ? Name : Name;
    // 使用现状：这个属性当前没有任何绑定消费者 —— 文件树模板里绑的是 Name（见 MarkdownView.axaml）。
    // 两个分支返回同一值，所以它既是「未使用的预留扩展点」，也是「代码意图尚未落地」的痕迹：
    // 想加脏标记前缀时，改这里并在模板里把 Name 换成 DisplayName 即可。
}

/// <summary>
/// 文件树中的一个分组（可折叠）。
/// IsExpanded 的 setter 除自身外还要通知 IsCollapsed 与 ChevronTransform，
/// 因为折叠图标的角度是由 IsExpanded 算出来的派生属性，不会自动刷新。
/// </summary>
public sealed class DocGroup : ObservableObject
{
    private bool _isExpanded = true;

    /// <summary>分组标题，显示在折角图标与文件图标之后。</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>
    /// 分组右侧的计数。
    /// 注意语义不一致：all 态取假数据里的原始总数（counts[i]），其它筛选态取实际命中条数。
    /// </summary>
    public int Count { get; init; }
    /// <summary>色调标识（accent / violet / muted），决定文件夹图标的着色。</summary>
    public string Tone { get; init; } = "accent";
    /// <summary>该分组下的文档节点。重建时整组换新，不复用旧实例。</summary>
    public ObservableCollection<DocItem> Items { get; } = new();
    /// <summary>点击分组标题时执行的折叠/展开命令，由 VM 注入 SelectGroupCommand。</summary>
    public ICommand ToggleCommand { get; init; } = null!;

    /// <summary>是否展开。setter 内连带通知 IsCollapsed 与 ChevronTransform 两个派生属性。</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!Set(ref _isExpanded, value)) return;
            OnPropertyChanged(nameof(IsCollapsed));
            OnPropertyChanged(nameof(ChevronTransform));
        }
    }

    /// <summary>是否收起。XAML 里给子级 ItemsControl 的 IsVisible 绑定的是它的反相语义。</summary>
    public bool IsCollapsed => !_isExpanded;

    /// <summary>折角图标的 Path 数据（一段向下折线），此处固定不变，变化的是它的旋转变换。</summary>
    public string ChevronData => "m6 9 6 6 6-6";
    // 使用现状：无绑定消费者。XAML 里折角图标绑的是常量 Icons.ChevronDown（见 MarkdownView.axaml），
    // 所以这段 Path 数据目前是「重复定义」：两处取值必须一致，改一处不会有任何编译期提示。

    /// <summary>折角图标的旋转：展开 0°，收起 -90°（顺时针转为指向右侧）。</summary>
    public ITransform ChevronTransform => new RotateTransform(IsExpanded ? 0 : -90);

    /// <summary>文件夹图标画刷，把 Tone 字符串映射成带 0.9 透明度的主题色。</summary>
    public IBrush FolderBrush => Tone switch
    {
        "violet" => Palette.Alpha(Palette.VioletColor, 0.9),
        "muted" => Palette.Alpha("#626A7D", 0.9),
        _ => Palette.Alpha(Palette.AccentColor, 0.9)
    };
}

/// <summary>
/// 编辑区顶部的一个标签页。
/// 只存名字，正文不缓存在这里 —— 切换标签时会按 Name 回 AllDocs 找文档再走 SelectDoc。
/// </summary>
public sealed class EditorTab : SelectableItem
{
    /// <summary>标签文字，等于对应 DocItem.Name，也是回查文档时使用的键。</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>点击标签切换文档的命令（SelectTabCommand）。</summary>
    public ICommand Command { get; init; } = null!;
    /// <summary>点击右侧 × 关闭标签的命令（CloseTabCommand）。</summary>
    public ICommand CloseCommand { get; init; } = null!;
}

/// <summary>
/// Markdown 文档模块的主 ViewModel（假数据版）。
/// 负责三件事：维护文档假数据与筛选后的文件树、维护当前文档正文与脏状态、切换编辑/分栏/预览三档模式。
/// 所有状态都只存在内存里，没有落盘逻辑；当前模块尚未挂到一级导航，恢复时接真实存储即可。
/// </summary>
public sealed class MarkdownViewModel : ObservableObject
{
    // 筛选态，与公开属性 Filter 分离存放：外部只能读，内部改字段后再手动通知
    private string _filter = "all";
    // 搜索关键字，非空时 RebuildTree 会额外按文件名做忽略大小写的包含匹配
    private string _search = string.Empty;
    // 当前显示模式，直接用中文枚举值（"编辑" / "分栏" / "预览"），XAML 与 code-behind 都按字符串比较
    private string _mode = "分栏";
    // 当前打开的文档，null 表示没有选中文档（此时工具栏显示占位文案）
    private DocItem? _current;
    // 编辑器正文，与 TextBox 双向绑定；setter 里带副作用（重算行号 + 置脏）
    private string _text = string.Empty;
    // 当前文档是否有未保存修改。注意它不是 DocItem.IsDirty 的镜像，是编辑器侧的运行态
    private bool _isDirty;
    // 工具条右侧的状态文案（"已保存" / "未保存"）
    private string _saveState = "已保存";

    /// <summary>
    /// 构造顺序很关键：先注册命令 → 造假文档 → 建初始标签页与模式段 → 重建文件树 → 选中第一篇。
    /// 命令必须最先创建，因为 BuildDocs 里的 DocItem.Command 直接引用 SelectDocCommand。
    /// </summary>
    public MarkdownViewModel()
    {
        // ── 构造顺序总览（六步，后一步依赖前一步的产物，顺序不能调换）──
        // 步骤 1：注册 6 个命令 → 步骤 2：造 13 篇假文档（AllDocs）→ 步骤 3：预置 3 个标签页
        // → 步骤 4：建 Modes 并选中「分栏」→ 步骤 5：首次建树（Groups）→ 步骤 6：选中第一篇。
        // 为什么「命令先于一切」：步骤 2 的 DocItem.Command 要引用 SelectDocCommand，
        // 步骤 3 的 EditorTab.Command / CloseCommand 要引用 SelectTabCommand / CloseTabCommand。
        // 这些成员都是只读属性（get-only / init），一旦对象构造完成就再也改不了，
        // 顺序错了只能拿到 null，界面表现为「点节点没反应」甚至直接 NullReferenceException。
        // 为什么命令体可以引用「此刻还是空集合」的 AllDocs 与 Tabs：
        // RelayCommand 只在构造时保存委托，委托体要到用户点击时才执行，
        // 构造期不会求值，因此「先建命令、后灌数据」是安全的。
        // 运行时序提醒：本类目前没有任何地方 new（模块已从一级导航移除），
        // 所以下面整条链路实际不会在界面上跑起来，恢复挂载时直接可用。
        // 文件树顶部筛选：参数是字符串标识（all/recent/star/dirty/trash），缺省回落 all
        FilterCommand = new RelayCommand(o => ApplyFilter(o as string ?? "all"));
        // 点击分组标题：只翻转 IsExpanded，通知界面的事交给 DocGroup 的 setter
        SelectGroupCommand = new RelayCommand(o =>
        {
            if (o is DocGroup g) g.IsExpanded = !g.IsExpanded;
        });
        // 点击文件节点：把命令参数还原成 DocItem，再走统一的选中流程
        SelectDocCommand = new RelayCommand(o =>
        {
            if (o is DocItem d) SelectDoc(d);
        });
        // 点击标签页：标签只存名字，所以按名字回 AllDocs 找到文档再切换；
        // 找不到对应文档时（例如标签指向了不存在的假数据）退化为仅切换标签选中态
        SelectTabCommand = new RelayCommand(o =>
        {
            if (o is EditorTab t)
            {
                var doc = AllDocs.FirstOrDefault(d => d.Name == t.Name);
                if (doc is not null) SelectDoc(doc);
                else
                {
                    foreach (var tab in Tabs) tab.IsSelected = tab == t;
                }
            }
        });
        // 关闭标签：只从 Tabs 集合移除，文档本身仍在 AllDocs 里，可以再次点开
        // 已知缺口：移除后不判断「关掉的是不是当前标签」，也不改动 Current / IsSelected，
        // 于是会出现「标签没了、编辑区还显示这篇文档」的中间态；关掉全部标签后标签栏整排为空，
        // 此时界面没有任何东西可以把文档「重新打开」，只剩左侧文件树可点。
        CloseTabCommand = new RelayCommand(o =>
        {
            if (o is EditorTab t) Tabs.Remove(t);
        });
        // 顶部三段控件：SegmentItem.Label 就是模式名，直接作为 SetMode 的入参
        ModeCommand = new RelayCommand(o =>
        {
            if (o is SegmentItem s) SetMode(s.Label);
        });

        // 六个命令都指向本实例的私有方法或 lambda，lambda 捕获的是 this，
        // 所以命令执行时读到的永远是「当时的字段值」，而不是创建命令那一刻的快照。
        // 步骤 1 到此结束：此后这些字段（FilterCommand 等）都不再被赋值，可以安全地被 13 个节点共享。

        // ── 步骤 2：造 13 篇假文档（只写 AllDocs，不动界面）──
        // 依赖：步骤 1 的 SelectDocCommand（BuildDocs 内部会把它塞进每一条 DocItem）。
        // 产物：AllDocs 里 4 组共 13 条记录，分组顺序即 BuildDocs 里的书写顺序。
        // 本步不建树：Groups 依赖 Filter / Search 两个字段（此时是初值 "all" / 空串），
        // 建树统一放到步骤 5，避免同一件事在两处各写一遍。
        // 造 13 篇假文档（分组、脏标记、收藏、最近访问等字段都在这一步固定）
        BuildDocs();
        // ── 步骤 3：预置三个标签页，模拟「上次退出时留下的打开记录」──
        // 依赖：Tabs（字段初始化器已 new 出空集合）、SelectTabCommand / CloseTabCommand（步骤 1）。
        // 这里用对象初始化器一次写完三个字段，Item 的 Name 必须与 BuildDocs 的 Name 逐字符一致
        // （含 .md 后缀、含数字里的短横），因为 SelectTabCommand 是拿 Name 回 AllDocs 做字符串查找的，
        // 写错不会报错，只会「点了标签但正文不换」。
        // 第一个 IsSelected=true 只保证首帧有高亮；步骤 6 的 SelectDoc 会把整排选中态按当前文档重算。
        // 预开三个标签页模拟「上次退出时留下的打开记录」，第一个默认选中
        Tabs.Add(new EditorTab { Name = "架构设计.md", Command = SelectTabCommand, CloseCommand = CloseTabCommand, IsSelected = true });
        Tabs.Add(new EditorTab { Name = "技术选型.md", Command = SelectTabCommand, CloseCommand = CloseTabCommand });
        Tabs.Add(new EditorTab { Name = "2026-10-20.md", Command = SelectTabCommand, CloseCommand = CloseTabCommand });
        // ── 步骤 4：建模式分段控件，并选中「分栏」──
        // 依赖：ModeCommand（步骤 1）。三个 SegmentItem 共用同一个命令实例，
        // 靠 XAML 里 CommandParameter="{Binding}" 把条目自身回传，VM 再用 s.Label 分辨是哪一档；
        // 因此 SegmentItem.Label 不只是显示文案，同时是 SetMode 的入参（两处语义绑死）。
        // 下标 1 就是「分栏」，且这个字面量必须与字段 _mode 的初值完全一致：
        // 前者决定哪颗按钮渲染成选中态（样式类 .selected），后者决定布局走哪个分支，
        // 两者错位会出现「高亮在分栏、布局却按编辑算」的首帧错乱。
        // 模式分段控件：三个选项共用同一个 ModeCommand，靠 CommandParameter 即自身来区分
        Modes = new ObservableCollection<SegmentItem>
        {
            new("编辑", ModeCommand),
            new("分栏", ModeCommand),
            new("预览", ModeCommand)
        };
        // 默认「分栏」，与字段 _mode 的初值保持一致，否则首次渲染会高亮错位
        Modes[1].IsSelected = true;

        // ── 步骤 5：首次建树 ──
        // 依赖：AllDocs（步骤 2）已就绪、_filter = "all"、_search = 空串、SelectGroupCommand（步骤 1）。
        // 产物：Groups 里 4 个 DocGroup，各自的 Items 装的是 AllDocs 中同组的同一批 DocItem 引用。
        // 这一步只产出「可绑定的数据」，界面刷新靠 Groups 的集合变更通知，不涉及任何选中态。
        // 步骤 6：选中第一篇 ──
        // 必须先建树再选文档：SelectDoc 要在 AllDocs 的节点上打 IsSelected，
        // 而文件树上那颗按钮是由 Groups 里的模板生成出来的；树不存在时，
        // 选中态就算写进了对象，也没有任何控件会去读它。
        // 先建树再选文档：选中文档时要在树节点上打 IsSelected，树必须已存在
        RebuildTree();
        // AllDocs[0] 是 "架构设计.md"（Notes 组第一篇），在 Contents 里有真实正文，所以首屏中间是完整示例。
        // 用下标 0 而非按名字查：BuildDocs 保证至少 13 条，不会越界；按名字查反而多一次字符串比较。
        // 这一步是整条链上唯一「同时改数据与界面状态」的调用：Current / Text / LineNumbers /
        // IsDirty / SaveState / 标签选中态 全在这里落定，也是构造函数里最后一条语句。
        SelectDoc(AllDocs[0]);
    }

    /// <summary>全量文档（扁平、不分组）。文件树是它经过 Filter + Search 投影后的结果。</summary>
    public List<DocItem> AllDocs { get; } = new();
    /// <summary>文件树数据源，按分组组织。每次筛选/搜索都整体重建，而不是增删其中元素。</summary>
    public ObservableCollection<DocGroup> Groups { get; } = new();
    /// <summary>编辑区标签页集合。按打开顺序排列，选中态由 IsSelected 单独维护。</summary>
    public ObservableCollection<EditorTab> Tabs { get; } = new();
    /// <summary>编辑/分栏/预览三档模式项，绑定到顶部 Segment 控件。</summary>
    public ObservableCollection<SegmentItem> Modes { get; }

    /// <summary>筛选命令，参数为筛选标识字符串。</summary>
    public ICommand FilterCommand { get; }
    /// <summary>分组折叠/展开命令，参数为 DocGroup。</summary>
    public ICommand SelectGroupCommand { get; }
    /// <summary>打开文档命令，参数为 DocItem。</summary>
    public ICommand SelectDocCommand { get; }
    /// <summary>切换标签命令，参数为 EditorTab。</summary>
    public ICommand SelectTabCommand { get; }
    /// <summary>关闭标签命令，参数为 EditorTab。</summary>
    public ICommand CloseTabCommand { get; }
    /// <summary>切换显示模式命令，参数为 SegmentItem。</summary>
    public ICommand ModeCommand { get; }

    /// <summary>左下角的文档总数文案，假数据固定值，不随筛选变化。</summary>
    public string DocCountText => "128 篇文档";
    // 128 与任何一处的真实数字都对不上：AllDocs 只有 13 条，counts 数组四组合计 30。
    // 三个数字各说各话，纯装饰；若将来要让它说真话，应改成 AllDocs.Count 之类的计算属性，
    // 并在 RebuildTree 里补一次通知。

    /// <summary>
    /// 当前筛选态。getter 读私有字段，setter 私有且改动后自动 RebuildTree，
    /// 保证「属性变化」和「文件树刷新」永远成对发生，外部无法只改状态不刷新。
    /// </summary>
    // 使用现状（重要）：这个 private setter 目前没有任何调用点 —— ApplyFilter 是直接写 _filter 绕过去的。
    // 于是「Filter 一变就自动重建树」这条保证只写在语法上，尚未被真正执行过；
    // 保留它的意义在于把「属性变化」与「重建成对」绑成约束：
    // 将来接真实筛选按钮时，调用方只写 Filter = "star" 就不会漏掉刷新这一半。
    public string Filter
    {
        get => _filter;
        private set
        {
            if (Set(ref _filter, value)) RebuildTree();
        }
    }

    /// <summary>搜索关键字，双向绑定到搜索框；每次变更都会重建文件树做实时过滤。</summary>
    // 调用方是搜索框的 TwoWay 绑定（TextBox 每敲一个字符就回写一次，不是回车才提交），
    // 所以这里的 RebuildTree 会跟着按键频率跑：13 条数据下开销可忽略，
    // 但整棵树的模板容器会被销毁重建，文件树的滚动位置随之回到顶部（当前没有做防抖或位置保持）。
    // Set 返回 false（输入没让字符串发生变化，例如输入法组字过程中的中间态）时直接跳过重建。
    public string Search
    {
        get => _search;
        set
        {
            if (Set(ref _search, value)) RebuildTree();
        }
    }

    /// <summary>当前模式名（中文）。只读，改动统一走 SetMode，以免漏掉派生属性通知。</summary>
    public string Mode => _mode;

    /// <summary>编辑区是否可见：预览模式下整列宽度归零。</summary>
    // 消费者：MarkdownView.axaml 里编辑器外层 Grid 的 IsVisible（绑定表达式 ShowEditor）。
    // 判据写成「不等于预览」而不是「等于编辑或分栏」，所以 _mode 被写成任何陌生值时它都是 true。
    public bool ShowEditor => _mode != "预览";
    /// <summary>预览区是否可见：纯编辑模式下隐藏右侧栏。</summary>
    // 消费者：MarkdownView.axaml 里预览栏 Border 的 IsVisible（绑定表达式 ShowPreview）。
    // 与 ShowEditor 一样是「不等于」式判据，脏 _mode 会让两者同时为 true（界面上看不出异常，
    // 因为 code-behind 的 default 分支正好按分栏处理，三者又对上了）。
    public bool ShowPreview => _mode != "编辑";
    /// <summary>是否处于分栏模式。XAML 里暂未直接使用，保留给需要区分三态的场景。</summary>
    public bool IsSplit => _mode == "分栏";
    /// <summary>编辑区列宽（星号比例）。分栏时 3 份，其余 1 份占满。</summary>
    // 消费者：无。XAML 的列宽是写死的常量，运行时列宽由 code-behind 的 ApplyMode 逐列改写；
    // 而本属性算的是「3 份星号」，ApplyMode 用的是「编辑 3* + 预览 Auto(420)」，两套口径并不等价
    // （对方是 Auto 时星号会吃掉全部剩余空间，不会真的按 3:2 分）。
    public GridLength EditorWidth => _mode == "分栏" ? new GridLength(3, GridUnitType.Star) : new GridLength(1, GridUnitType.Star);
    /// <summary>预览区列宽（星号比例）。分栏时 2 份，其余 1 份。</summary>
    public GridLength PreviewWidth => _mode == "分栏" ? new GridLength(2, GridUnitType.Star) : new GridLength(1, GridUnitType.Star);

    /// <summary>
    /// 当前文档。setter 私有，改动时连带通知 CurrentName / CurrentGroup 两个派生属性。
    /// </summary>
    // 使用现状：XAML 没有直接绑 Current，只绑它的两个派生属性；
    // 因此它更像「当前选中项的容器」，界面可见的信息全部来自下面那两行算出来的字符串。
    public DocItem? Current
    {
        get => _current;
        private set
        {
            if (Set(ref _current, value))
            {
                OnPropertyChanged(nameof(CurrentName));
                OnPropertyChanged(nameof(CurrentGroup));
            }
        }
    }

    /// <summary>工具栏面包屑里的文件名，未选中时给占位文案。</summary>
    public string CurrentName => _current?.Name ?? "未打开文档";
    /// <summary>工具栏面包屑里的分组名，未选中时回落 Notes。</summary>
    public string CurrentGroup => _current?.GroupName ?? "Notes";

    /// <summary>
    /// 编辑器正文，与 TextBox 双向绑定。
    /// setter 带三件副作用：① 按换行符重算行号字符串；② 标记为脏；③ 状态文案改成「未保存」。
    /// 所以「切文档」这种非用户输入的场景必须绕开 setter，见 SelectDoc。
    /// </summary>
    public string Text
    {
        get => _text;
        set
        {
            // 调用方只有两类：① 编辑器的 TextBox 双向绑定（键入、粘贴、撤销、拖入文本）；
            //                ② SelectDoc 换文档（换完立刻把脏标记改回来，见那边的步骤 4）。
            // 后置状态：_text 更新 → LineNumbers 按换行重算 → IsDirty = true → SaveState = "未保存"；
            //           三个被改的属性各自通过 Set 发通知，行号栏与工具条由绑定自己刷新，
            //           这里不需要、也不该碰任何控件。
            // 关键短路：Set 返回 false 说明新旧字符串完全相同（编辑器偶尔会回写同值），
            //           此时直接 return，后面三重副作用一个都不执行 ——
            //           否则「点一下文本框」这种没改内容的动作也会把文档标脏。
            if (!Set(ref _text, value)) return;
            // 为什么行号是一整段字符串而不是一个集合：
            // 行号栏就是一个普通 TextBlock（MarkdownView.axaml 里 x:Name="Gutter"），它只认 Text 一个字符串。
            // 若改成 ObservableCollection<int> + ItemsControl，每行要多一个容器与一次布局，
            // 滚动时还得自己算每行高度；而单块 TextBlock 只要把 LineHeight 设成与编辑器同一个值，
            // 每行基线天然对齐。代价是每敲一个换行就重建整段字符串，
            // 在几十行的量级下完全可以忽略（本模块最长的示例不到 30 行）。
            // 行号栏是一整段用 \n 拼接的 "1\n2\n3…"，行数 = 换行符个数 + 1，最少 1 行
            // Enumerable.Range(1, n) 生成 1..n 再 Join("\n")，即把「行号序列」压成一个多行文本；
            // Math.Max(1, ...) 防的是空文本：n 若为 0，Range 产出空序列，Join 得到空串，
            // 行号栏会整体消失（连第 1 行都没有），而不是显示一个 "1"。
            // 副作用细节：正文以 \n 结尾时，末尾那个空行也会被算作一行 ——
            // 行号因此比肉眼可见的文字多一个，这与编辑器把光标停在新行的行为是一致的。
            LineNumbers = string.Join("\n", Enumerable.Range(1, Math.Max(1, value.Count(c => c == '\n') + 1)));
            // 走到这里就一定是用户改动了内容：置脏、并把工具条文案切成「未保存」。
            // 两条都是 Set 通知，界面无需额外刷新；顺序上先脏后文案只是阅读顺序，没有依赖。
            IsDirty = true;
            SaveState = "未保存";
        }
    }

    private string _lineNumbers = "1";

    /// <summary>行号栏文本，由 Text 的 setter 维护，外部只读。</summary>
    public string LineNumbers
    {
        get => _lineNumbers;
        private set => Set(ref _lineNumbers, value);
    }

    /// <summary>编辑器侧的脏标记，私有 setter：只有输入或 SelectDoc 内部能改写。</summary>
    // 使用现状：本视图没有绑定它。XAML 里那颗琥珀色圆点绑的是 DocItem.IsDirty（作用域在节点上），
    // 所以这里的置脏/清脏目前不产生任何可见反馈；用户真正看到的「已保存 / 未保存」是 SaveState 文案。
    public bool IsDirty
    {
        get => _isDirty;
        private set => Set(ref _isDirty, value);
    }

    /// <summary>保存状态文案，显示在工具条绿色圆点右侧。</summary>
    public string SaveState
    {
        get => _saveState;
        private set => Set(ref _saveState, value);
    }

    /// <summary>
    /// 构造内置假文档列表 AllDocs。
    /// 输入：无；输出：向 AllDocs 追加 4 组共 13 条记录。
    /// 副作用：每条记录都挂上 SelectDocCommand，因此本方法必须在命令注册之后调用。
    /// 各元组字段依次为：文件名、是否脏、是否收藏、是否最近访问。
    /// </summary>
    private void BuildDocs()
    {
        // 局部函数：批量把一个分组的文档塞进 AllDocs，避免 13 次重复的对象初始化
        void Add(string group, params (string Name, bool Dirty, bool Star, bool Recent)[] docs)
        {
            foreach (var d in docs)
            {
                AllDocs.Add(new DocItem
                {
                    Name = d.Name,
                    GroupName = group,
                    IsDirty = d.Dirty,
                    IsStarred = d.Star,
                    IsRecent = d.Recent,
                    Command = SelectDocCommand
                });
            }
        }

        Add("Notes",
            ("架构设计.md", false, true, true),
            ("产品需求.md", false, false, true),
            ("技术选型.md", true, false, false),
            ("踩坑记录.md", false, false, true));
        Add("Journal",
            ("2026-10-20.md", false, false, true),
            ("2026-10-19.md", false, false, false));
        Add("Drafts",
            ("产品需求 v2.md", false, false, true),
            ("竞品分析.md", false, false, false),
            ("发布清单.md", true, false, false),
            ("API 草案.md", false, false, false));
        Add("Archive",
            ("2025-Q4 复盘.md", false, false, false),
            ("旧版架构.md", false, true, false),
            ("迁移方案.md", false, false, false));
    }

    /// <summary>
    /// 按当前 Filter + Search 重建文件树。
    /// 输入：字段 _filter、_search 与 AllDocs；输出：Groups 被整体替换成新的分组快照。
    /// 副作用：先 Clear 再逐组 Add，任何已绑定到 ItemsControl 的旧节点实例都会被丢弃（含折叠状态）。
    /// 之所以每次整组重建而不是原地增删：ItemsControl 绑的是 Groups 这个集合本身，
    /// 换数据源不会重新触发模板生成，只能通过清空+重新填充让集合变更通知逐条送达界面。
    /// </summary>
    private void RebuildTree()
    {
        // 调用方共四处：构造函数步骤 5、Filter 的 private setter、Search 的 setter、ApplyFilter。
        // 前置状态：AllDocs 已建好；调用方刚刚改过 _filter 或 _search（也可能两者都没改，只是要求重刷）。
        // 后置状态：Groups 里换成一批全新的 DocGroup（折叠状态回到默认的展开），
        //           但 Items 里装的仍是 AllDocs 中的同一批 DocItem 引用 ——
        //           所以挂在 DocItem 上的 IsSelected 能跨重建保留，而 DocGroup.IsExpanded 会丢。
        // 通知机制：Clear 与 Add 会逐条发出 CollectionChanged，ItemsControl 据此销毁并重建模板容器；
        //           全程不需要任何 OnPropertyChanged，也绝对不能给 Groups 换新实例
        //           （ItemsControl 绑的是集合实例本身，换实例后旧实例的通知再没人听）。
        // ── 步骤 1：清空旧树 ──
        // 先清空。Groups 是 ObservableCollection，Clear 会逐个发出移除通知，界面同步清空
        Groups.Clear();
        // ── 步骤 2：三张等长数组描述四个分组的静态属性 ──
        // groups[i] / tones[i] / counts[i] 必须同长同序，下标 i 是三者唯一的关联键。
        // 为什么用写死数组而不是从 AllDocs 里 GroupBy：
        // ① GroupBy 按首次出现顺序输出，分组排序会随假数据的书写顺序漂移；
        // ② GroupBy 会把「一条都没命中」的分组彻底抹掉，而 all 态下我们希望空分组仍占位；
        // ③ 色调与展示计数本来就不是数据里带的字段，写死最直白。
        // 分组顺序与标题在这里写死，与 BuildDocs 里的分组名一一对应
        var groups = new[] { "Notes", "Journal", "Drafts", "Archive" };
        // 每个分组对应的图标色调，与 FileTree 模板里的 FolderBrush 匹配
        var tones = new[] { "accent", "violet", "muted", "muted" };
        // all 态展示用的「原始总数」假数据，故意大于真实条数，模拟「这里只是缓存了一部分」的效果
        var counts = new[] { 6, 3, 4, 17 };

        // 三张数组的取值对照（展示值 / AllDocs 里的真实条数）：
        //   Notes 6 / 4 · Journal 3 / 2 · Drafts 4 / 4 · Archive 17 / 3，合计 30 / 13。
        // 差值不是笔误：all 态下 Count 展示的是「服务端总量」的假象，列表里只列出本地这几条，
        // 于是会出现「分组写着 17、展开只有 3 条」；左下角 DocCountText 的 128 又是第三套数字。
        // ── 步骤 3：逐组「分桶 → 筛选 → 搜索 → 判空 → 造组 → 填项」──
        // 每组都新建 DocGroup，不复用上一轮的实例，所以整个循环是彻底的重建式写法。
        for (var i = 0; i < groups.Length; i++)
        {
            var name = groups[i];
            // 步骤 3.1：按分组名分桶。items 此刻是延迟求值的 IEnumerable，Where 还没真正跑，
            //           执行要等到步骤 3.4 的 ToList；这期间只是在拼一条管道。
            // 先按分组名分桶，再叠加筛选条件
            IEnumerable<DocItem> items = AllDocs.Where(d => d.GroupName == name);
            // 步骤 3.2：叠加筛选态。这里是「重新赋值 items」而不是「往 items 里追加」，
            //           所以每条分支都必须以当前 items 为起点（兜底分支 _ => items 也一样）；
            //           漏掉起点就等于丢掉分桶结果，后续搜索会作用于全部 13 条。
            //           另外这些 Where 都不修改源集合，AllDocs 永远保持原样。
            items = _filter switch
            {
                "recent" => items.Where(d => d.IsRecent),
                "star" => items.Where(d => d.IsStarred),
                "dirty" => items.Where(d => d.IsDirty),
                // 回收站没有真实数据源，直接给空集合（配合下面的 continue 让整组消失）
                "trash" => Enumerable.Empty<DocItem>(),
                _ => items
            };
            // 分支速查（条数按 BuildDocs 的假数据统计）：
            //   "recent" → 只留 IsRecent=true，5 条（架构设计 / 产品需求 / 踩坑记录 / 2026-10-20 / 产品需求 v2）
            //   "star"   → 只留 IsStarred=true，2 条（架构设计 / 旧版架构）
            //   "dirty"  → 只留 IsDirty=true，2 条（技术选型 / 发布清单）
            //   "trash"  → 恒为空：四组全空 → 四个 continue 全命中 → Groups 空 → 文件树整体空白，
            //              连分组标题都不剩，界面上没有任何「空态」占位提示。
            //   兜底分支 → 含 "all" 以及任何未知字符串，原样放过（所以传错筛选值等于不过滤）。
            // 步骤 3.3：叠加搜索。先 IsNullOrWhiteSpace 再套 Where，省掉一次无意义的管道拼接；
            //           这个判断也顺带让「只敲了空格」等价于没搜索（Trim 之后会是空串，空串 Contains 恒真）。
            // 搜索条件最后叠加：只匹配文件名，忽略大小写，先 Trim 掉首尾空格避免误判空串
            if (!string.IsNullOrWhiteSpace(_search))
                items = items.Where(d => d.Name.Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase));

            // StringComparison.OrdinalIgnoreCase 的含义：按 UTF-16 码元逐位比较（序数 / Ordinal），
            // 只额外折叠 ASCII 的 A 到 Z 大小写。它与当前区域设置、当前文化无关，
            // 所以同一份代码在任何系统语言下结果一致，也不会踩土耳其语 i/İ 之类的文化陷阱；
            // 中文文件名不受大小写折叠影响，效果等同普通包含匹配，成本比 CultureInfo 比较更低。
            // 代价是 _search.Trim() 在每次枚举（每组一次）时都会重算，属于可忽略的小浪费。
            // 步骤 3.4：物化。ToList 是整条管道唯一的执行点，
            //           之后的 Count 判断与 foreach 都要多次枚举，物化后避免重复计算。
            var list = items.ToList();
            // 非 all 态下没有命中的分组直接跳过：宁可整组不显示，也不留一个空壳分组占位
            if (_filter != "all" && list.Count == 0) continue;

            // continue 的判据只有 _filter，不含 _search，于是有两种容易被误当成渲染 bug 的表现：
            // ① 搜索把某组过滤空、但 _filter 仍是 "all" 时不会跳过 → 留下「只有标题 + 计数、
            //    没有任何文件」的空壳分组（计数仍显示 counts[i] 的总量）；
            // ② _filter 是 "trash" 时四组全空 → 四组全被跳过 → 整棵树一片空白。
            // 步骤 3.5：造组。ToggleCommand 用的是 VM 级别共享的同一个实例（不是每组 new 一个）；
            //           Items 由字段初始化器 new 好，这里只负责往里填。
            var group = new DocGroup
            {
                Name = name,
                // 计数口径：all 态用假数据 counts[i]（体现「总量」），筛选态用实际命中数（否则计数会撒谎）
                // 两种口径直接决定 UI 语义：
                //   all 态 → counts[i]，即「总篇数」，与下面实际列出几条无关（6/3/4/17 对 4/2/4/3）；
                //   其它态 → list.Count，即本次筛选真正命中的条数。
                // 判据只有 _filter，所以「all + 搜索」仍显示总量，不会随搜索结果收缩。
                Count = _filter == "all" ? counts[i] : list.Count,
                Tone = tones[i],
                ToggleCommand = SelectGroupCommand
            };
            // 步骤 3.6：填项。group 还没进 Groups，所以这些 Add 不产生任何界面开销；
            //           真正的渲染发生在步骤 3.7，由模板一次性生成整组节点。
            foreach (var item in list) group.Items.Add(item);
            // 步骤 3.7：整组挂上树。这一刻 ItemsControl 才收到 Add 通知并生成该组模板；
            //           组内已经填满，用户展开时不需要再补数据。
            Groups.Add(group);
        }
    }

    /// <summary>
    /// 应用一个筛选条件。
    /// 输入：筛选标识字符串（all/recent/star/dirty/trash）。
    /// 副作用：改字段 _filter → 手动通知 Filter 属性 → 重建文件树。
    /// 这里直接写字段而不是走 Filter 的 setter，是为了避免 setter 里的 RebuildTree 与本方法重复执行一次。
    /// 补充更正：本方法内部并没有再调 Filter 的 setter，所以上面这句话描述的「重复执行」并不成立；
    /// 真正的差别在别处 —— 走 setter 时 Set 会做相等判断，「点了一个已经选中的筛选项」不会重建树，
    /// 而本方法无条件重建（连点同一个筛选会白重建一遍，13 条数据下无感）。
    /// 第二个差别在通知：OnPropertyChanged 必须显式写 nameof(Filter)，
    /// 因为 [CallerMemberName] 取的是「所在成员名」，在普通方法里会得到 "ApplyFilter"，
    /// 那时通知就发给了根本不存在的属性，绑定了 Filter 的界面永远不会刷新。
    /// 当前状态：FilterCommand 与 Filter 在本工程的视图里都没有消费者（文件树顶部并没有渲染筛选按钮那一排），
    /// 整条链是「保留待恢复」的备用逻辑。
    /// </summary>
    private void ApplyFilter(string filter)
    {
        // 步骤 1：改字段。绕过 setter 的原因见上面的补充更正。
        _filter = filter;
        // 步骤 2：手动补通知。写法之所以是「手写属性名」，见上面的第二个差别。
        OnPropertyChanged(nameof(Filter));
        // 步骤 3：重建文件树。这是三步里唯一真正改变用户可见结果的一步，
        //         界面刷新完全由 Groups 的集合变更事件驱动，不依赖步骤 2 的那次通知。
        RebuildTree();
    }

    /// <summary>
    /// 切换显示模式。
    /// 输入：中文模式名（"编辑" / "分栏" / "预览"）。
    /// 副作用：改字段 _mode，并逐个通知 Mode 以及 5 个派生属性（ShowEditor / ShowPreview / IsSplit /
    /// EditorWidth / PreviewWidth）。手写 MVVM 没有 [ObservableProperty] 之类的联动，
    /// 少通知任何一个都会出现「列宽变了但可见性没变」这类半更新状态。
    /// 另外 code-behind 监听 Mode 变化后还会再改一次列宽，两者必须保持一致。
    /// </summary>
    private void SetMode(string mode)
    {
        // 调用链：SegmentItem 按钮点击 → ModeCommand（参数是条目自身）→ SetMode(s.Label)。
        // 前置：mode 应来自 Modes 里那三个 Label，即 "编辑" / "分栏" / "预览"。
        // 这里不做白名单校验，传进陌生字符串会静默落到「既不是预览也不是编辑」的一侧：
        // ShowEditor / ShowPreview 都变 true，code-behind 的 default 分支按分栏处理，
        // 表现为「布局像分栏、Mode 却是脏值」，排查时优先怀疑入参。
        // 也没有「值没变就跳过」的判断：重复点击当前模式会照原样再发一轮通知，无害但非必要。
        // 步骤 1：写字段。没走 Set，所以下面每一行通知都必须手动补 —— 少一行就少一块刷新。
        _mode = mode;
        // 步骤 2：先通知 Mode 本身。当前有两个消费者：
        //         ① code-behind 的 OnVmChanged 收到 "Mode" 后调 ApplyMode() 改列宽与预览栏宽度；
        //         ② 理论上是 XAML 绑定（本视图没有绑 Mode，绑的是下面那几个布尔与宽度）。
        OnPropertyChanged(nameof(Mode));
        // 步骤 3 到 7：逐个通知 5 个派生属性。它们全是「由 _mode 现算的表达式属性」，
        // 没有自己的存储字段，因此 _mode 变了也不会被自动通知，必须手写。
        // 为什么要一起通知：这五个属性描述的是同一个状态的不同侧面，
        // 只通知一半就会出现「列宽变了但可见性没变」这种半更新画面（一帧内可见的错位）。
        //   ShowEditor   → 编辑区整块的 IsVisible（MarkdownView.axaml 编辑器那一层 Grid）
        //   ShowPreview  → 预览栏 Border 的 IsVisible
        //   IsSplit      → 当前无消费者，留给需要区分三态的场景
        //   EditorWidth  → 当前无消费者：XAML 的列宽是写死的常量，运行时列宽由 ApplyMode 改写
        //   PreviewWidth → 同上
        // 后三个「通知了但没人听」不算错，只是每次都多发一次空通知；
        // 需要留意的是它们算出来的 3:2 与 ApplyMode 实际使用的「星号 + 420 固定」并不是一回事，
        // 将来若把列宽绑回 VM，会立刻变成两套互相打架的规则。
        OnPropertyChanged(nameof(ShowEditor));
        OnPropertyChanged(nameof(ShowPreview));
        OnPropertyChanged(nameof(IsSplit));
        OnPropertyChanged(nameof(EditorWidth));
        OnPropertyChanged(nameof(PreviewWidth));
    }

    /// <summary>
    /// 打开一篇文档：同步选中态、正文、脏状态、标签页。
    /// 输入：目标 DocItem。
    /// 副作用：① 重设 AllDocs 与 Tabs 的 IsSelected；② 替换 Text（进而重算行号）；
    /// ③ 重置脏标记与保存文案；④ 若标签不存在则追加到末尾。
    /// </summary>
    private void SelectDoc(DocItem doc)
    {
        // 调用方三处：构造函数步骤 6、SelectDocCommand（点文件树节点）、SelectTabCommand（点标签页）。
        // 前置：doc 必须是 AllDocs 里的实例（同一批引用）；Current / Tabs / Contents 均已就绪。
        // 后置：Current = doc；Text = 该文档正文（连带重算行号）；IsDirty = false；
        //       SaveState 按 doc.IsDirty 取「已保存 / 未保存」；
        //       AllDocs 里只有 doc 的 IsSelected 为 true；Tabs 里保证有 doc.Name 的标签且只有它选中。
        // 注意本方法不校验 doc 是否属于 AllDocs：传外部实例进来时，步骤 1 会把所有节点都清空，
        // 界面上就是「一个都没选中，但编辑区显示了这篇文档」。
        // 步骤 1：清掉全局选中态，只点亮目标节点。
        //         用 ReferenceEquals 而不是 Name 比较：重名实例会同时点亮两条。
        //         成本是 13 次属性赋值，且 Set 内有相等判断，已经是 false 的那些不会发通知。
        // 文件树里是单选语义，先全局清掉再点亮目标节点
        foreach (var d in AllDocs) d.IsSelected = ReferenceEquals(d, doc);
        // 步骤 2：换当前文档。setter 内 Set 会先发 Current 通知，
        //         再补发 CurrentName / CurrentGroup（工具条面包屑的两段文字）。
        Current = doc;
        // 步骤 3：写正文。Contents 只覆盖 3 篇（架构设计 / 技术选型 / 2026-10-20），
        //         其余 10 篇走三元表达式的后半段：文件名去扩展名当一级标题，再补一段占位提示。
        //         用 Path.GetFileNameWithoutExtension 而不是截掉末尾 3 个字符，
        //         是为了兼容没有点号的文件名（截断会吃掉真实字符）。
        //         这一行同时触发 Text setter 的全部副作用：行号重算 + IsDirty=true + SaveState="未保存"。
        // 正文优先取内置示例，没有则为该文档生成一个标题 + 空行的占位内容
        Text = Contents.TryGetValue(doc.Name, out var md) ? md : $"# {System.IO.Path.GetFileNameWithoutExtension(doc.Name)}\n\n开始书写…\n";
        // 步骤 4：把上一步被 setter 顺手改成 true 的脏标记改回来。
        //         这里直接写字段 + 手动通知，而不是写 IsDirty = false：
        //         两者结果几乎一样（差别只是值本来就已是 false 时，这种写法仍会多发一次通知），
        //         写成字段是为了让「这一行刻意绕开属性」的意图在读代码时一眼可见。
        //         OnPropertyChanged 必须显式给 nameof(IsDirty)：本方法不是属性访问器，
        //         [CallerMemberName] 会取到 "SelectDoc"，通知名发错等于没通知。
        // 关键点：上面给 Text 赋值会触发 setter，把 IsDirty 改成 true、SaveState 改成「未保存」。
        // 但「切换文档」不是编辑行为，所以这里直接给字段赋值绕开 setter，再手动通知一次，
        // 既不重复触发副作用，又能把状态改回真实值。
        _isDirty = false;
        OnPropertyChanged(nameof(IsDirty));
        // 步骤 5：状态文案取「文档对象自带的」IsDirty（BuildDocs 里写死的静态标记），
        //         而不是上一步刚清零的编辑器运行态 _isDirty —— 两套状态在这里第一次分岔。
        //         因此打开「技术选型.md」时工具条会显示「未保存」，尽管用户一行都没改；
        //         反之用户在新文档里打字后，_isDirty 变 true 却不会回写到 DocItem.IsDirty
        //         （那是 init 只读属性），文件树上的琥珀色圆点也就永远不会因为编辑而出现。
        // 保存文案以文档自身携带的脏标记为准（假数据里「技术选型.md」「发布清单.md」是脏的）
        SaveState = doc.IsDirty ? "未保存" : "已保存";

        // 步骤 6：标签页去重后追加。Tabs.Any 里的 == 是 string 的序数比较（seq 相等），
        //         不忽略大小写、不 Trim，所以 Name 必须完全一致；不一致的后果不是报错，
        //         而是每点一次就多开一个同名标签（同一个文档开出两页）。
        //         新加的 EditorTab 故意不设 IsSelected，交给步骤 7 统一处理，避免两条路径各写一半。
        // 标签页按名字去重：已打开过就不再新增，只把选中态移过去
        if (!Tabs.Any(t => t.Name == doc.Name))
        {
            Tabs.Add(new EditorTab
            {
                Name = doc.Name,
                Command = SelectTabCommand,
                CloseCommand = CloseTabCommand
            });
        }

        // 步骤 7：标签单选。不只点亮目标，还把其它标签全部清掉 ——
        //         IsSelected 通过 Classes.selected 直接驱动样式类，不手工维护就会出现两个标签同时高亮。
        //         这里用 Name 相等而不是引用比较，是为了兼容「重建后的同名标签」这种场景。
        // 标签是单选，同样先全清再点亮同名项
        foreach (var t in Tabs) t.IsSelected = t.Name == doc.Name;
    }

    // 内置示例正文。键与 DocItem.Name 对齐，未命中的文档会走 SelectDoc 里的占位内容分支。
    // 以下全部为 C# 原始字符串字面量（"""..."""），内部的 # 号、竖线表格、代码围栏都不能改，
    // 否则预览渲染结果会变；这些内容不参与任何逻辑判断，纯粹供演示。
    private static readonly Dictionary<string, string> Contents = new()
    {
        ["架构设计.md"] = """
# Aster 架构设计

> 基于 Avalonia 11 构建的跨平台桌面应用

## 一、模块划分

应用由三个核心模块组成，通过共享的 Shell 宿主容器统一挂载，模块之间不直接依赖。

- **Markdown 编辑器** — 负责文档的编写与实时预览
- **音乐播放器** — 本地曲库管理与沉浸式播放
- **管理系统** — 数据看板与业务操作

## 二、技术选型

| 模块 | 关键依赖 |
| --- | --- |
| 编辑 | `AvaloniaEdit` · `Markdig` |
| 音乐 | `LibVLCSharp` |
| 图表 | `LiveChartsCore` |

```csharp
public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();
}
```
""",
        ["技术选型.md"] = """
# 技术选型

## 编辑器

选用 **AvaloniaEdit**，与 Avalonia 11 同源，语法高亮与折叠均可复用。

- 行号栏内置
- 支持自定义语法定义

## 播放内核

优先 **LibVLCSharp**，其次是 NAudio。

```csharp
using var libVlc = new LibVLC();
using var player = new MediaPlayer(libVlc);
```
""",
        ["2026-10-20.md"] = """
# 2026-10-20 工作日志

> 周三 · 上海 · 晴

## 完成

- 重构 Shell 宿主容器
- 对齐设计稿配色与间距

## 待办

- 补齐设置面板
- 校验高 DPI 下的图标清晰度
"""
    };
}
