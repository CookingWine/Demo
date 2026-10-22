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
        CloseTabCommand = new RelayCommand(o =>
        {
            if (o is EditorTab t) Tabs.Remove(t);
        });
        // 顶部三段控件：SegmentItem.Label 就是模式名，直接作为 SetMode 的入参
        ModeCommand = new RelayCommand(o =>
        {
            if (o is SegmentItem s) SetMode(s.Label);
        });

        // 造 13 篇假文档（分组、脏标记、收藏、最近访问等字段都在这一步固定）
        BuildDocs();
        // 预开三个标签页模拟「上次退出时留下的打开记录」，第一个默认选中
        Tabs.Add(new EditorTab { Name = "架构设计.md", Command = SelectTabCommand, CloseCommand = CloseTabCommand, IsSelected = true });
        Tabs.Add(new EditorTab { Name = "技术选型.md", Command = SelectTabCommand, CloseCommand = CloseTabCommand });
        Tabs.Add(new EditorTab { Name = "2026-10-20.md", Command = SelectTabCommand, CloseCommand = CloseTabCommand });
        // 模式分段控件：三个选项共用同一个 ModeCommand，靠 CommandParameter 即自身来区分
        Modes = new ObservableCollection<SegmentItem>
        {
            new("编辑", ModeCommand),
            new("分栏", ModeCommand),
            new("预览", ModeCommand)
        };
        // 默认「分栏」，与字段 _mode 的初值保持一致，否则首次渲染会高亮错位
        Modes[1].IsSelected = true;

        // 先建树再选文档：选中文档时要在树节点上打 IsSelected，树必须已存在
        RebuildTree();
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

    /// <summary>
    /// 当前筛选态。getter 读私有字段，setter 私有且改动后自动 RebuildTree，
    /// 保证「属性变化」和「文件树刷新」永远成对发生，外部无法只改状态不刷新。
    /// </summary>
    public string Filter
    {
        get => _filter;
        private set
        {
            if (Set(ref _filter, value)) RebuildTree();
        }
    }

    /// <summary>搜索关键字，双向绑定到搜索框；每次变更都会重建文件树做实时过滤。</summary>
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
    public bool ShowEditor => _mode != "预览";
    /// <summary>预览区是否可见：纯编辑模式下隐藏右侧栏。</summary>
    public bool ShowPreview => _mode != "编辑";
    /// <summary>是否处于分栏模式。XAML 里暂未直接使用，保留给需要区分三态的场景。</summary>
    public bool IsSplit => _mode == "分栏";
    /// <summary>编辑区列宽（星号比例）。分栏时 3 份，其余 1 份占满。</summary>
    public GridLength EditorWidth => _mode == "分栏" ? new GridLength(3, GridUnitType.Star) : new GridLength(1, GridUnitType.Star);
    /// <summary>预览区列宽（星号比例）。分栏时 2 份，其余 1 份。</summary>
    public GridLength PreviewWidth => _mode == "分栏" ? new GridLength(2, GridUnitType.Star) : new GridLength(1, GridUnitType.Star);

    /// <summary>
    /// 当前文档。setter 私有，改动时连带通知 CurrentName / CurrentGroup 两个派生属性。
    /// </summary>
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
            if (!Set(ref _text, value)) return;
            // 行号栏是一整段用 \n 拼接的 "1\n2\n3…"，行数 = 换行符个数 + 1，最少 1 行
            LineNumbers = string.Join("\n", Enumerable.Range(1, Math.Max(1, value.Count(c => c == '\n') + 1)));
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
        // 先清空。Groups 是 ObservableCollection，Clear 会逐个发出移除通知，界面同步清空
        Groups.Clear();
        // 分组顺序与标题在这里写死，与 BuildDocs 里的分组名一一对应
        var groups = new[] { "Notes", "Journal", "Drafts", "Archive" };
        // 每个分组对应的图标色调，与 FileTree 模板里的 FolderBrush 匹配
        var tones = new[] { "accent", "violet", "muted", "muted" };
        // all 态展示用的「原始总数」假数据，故意大于真实条数，模拟「这里只是缓存了一部分」的效果
        var counts = new[] { 6, 3, 4, 17 };

        for (var i = 0; i < groups.Length; i++)
        {
            var name = groups[i];
            // 先按分组名分桶，再叠加筛选条件
            IEnumerable<DocItem> items = AllDocs.Where(d => d.GroupName == name);
            items = _filter switch
            {
                "recent" => items.Where(d => d.IsRecent),
                "star" => items.Where(d => d.IsStarred),
                "dirty" => items.Where(d => d.IsDirty),
                // 回收站没有真实数据源，直接给空集合（配合下面的 continue 让整组消失）
                "trash" => Enumerable.Empty<DocItem>(),
                _ => items
            };
            // 搜索条件最后叠加：只匹配文件名，忽略大小写，先 Trim 掉首尾空格避免误判空串
            if (!string.IsNullOrWhiteSpace(_search))
                items = items.Where(d => d.Name.Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase));

            var list = items.ToList();
            // 非 all 态下没有命中的分组直接跳过：宁可整组不显示，也不留一个空壳分组占位
            if (_filter != "all" && list.Count == 0) continue;

            var group = new DocGroup
            {
                Name = name,
                // 计数口径：all 态用假数据 counts[i]（体现「总量」），筛选态用实际命中数（否则计数会撒谎）
                Count = _filter == "all" ? counts[i] : list.Count,
                Tone = tones[i],
                ToggleCommand = SelectGroupCommand
            };
            foreach (var item in list) group.Items.Add(item);
            Groups.Add(group);
        }
    }

    /// <summary>
    /// 应用一个筛选条件。
    /// 输入：筛选标识字符串（all/recent/star/dirty/trash）。
    /// 副作用：改字段 _filter → 手动通知 Filter 属性 → 重建文件树。
    /// 这里直接写字段而不是走 Filter 的 setter，是为了避免 setter 里的 RebuildTree 与本方法重复执行一次。
    /// </summary>
    private void ApplyFilter(string filter)
    {
        _filter = filter;
        OnPropertyChanged(nameof(Filter));
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
        _mode = mode;
        OnPropertyChanged(nameof(Mode));
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
        // 文件树里是单选语义，先全局清掉再点亮目标节点
        foreach (var d in AllDocs) d.IsSelected = ReferenceEquals(d, doc);
        Current = doc;
        // 正文优先取内置示例，没有则为该文档生成一个标题 + 空行的占位内容
        Text = Contents.TryGetValue(doc.Name, out var md) ? md : $"# {System.IO.Path.GetFileNameWithoutExtension(doc.Name)}\n\n开始书写…\n";
        // 关键点：上面给 Text 赋值会触发 setter，把 IsDirty 改成 true、SaveState 改成「未保存」。
        // 但「切换文档」不是编辑行为，所以这里直接给字段赋值绕开 setter，再手动通知一次，
        // 既不重复触发副作用，又能把状态改回真实值。
        _isDirty = false;
        OnPropertyChanged(nameof(IsDirty));
        // 保存文案以文档自身携带的脏标记为准（假数据里「技术选型.md」「发布清单.md」是脏的）
        SaveState = doc.IsDirty ? "未保存" : "已保存";

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
