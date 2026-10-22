// ============================================================================
// SettingsViewModel.cs —— 设置浮层的全部表单数据（纯视图模型，不含业务副作用）
// 核心设计：数据驱动的表单。分区 SettingSection → 分组 SettingGroup → 行 SettingRow，
// 行右侧的 Value 是 SettingValue 的具体子类，XAML 靠隐式 DataTemplate 按类型匹配控件，
// 所以增删或调整表单项只改本文件，完全不用动 SettingsOverlay.axaml。
// 与外部的关系：MainWindowViewModel 持有本类实例（Settings 属性），并用 IsSettingsOpen
// 控制浮层显隐；本类只决定"当前选中哪个分区"，关闭浮层交给外部命令。
// 易踩的坑：Value 声明为 object，模板靠运行时类型匹配——新增子类若忘了在 XAML 补模板，
// 该行会退化成 ToString() 纯文本；另外 Select/Chips 这类工厂方法名与类型名近似，别混淆。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Avalonia.Media;

namespace Aster.ViewModels;

// ===================== 设置行取值类型 =====================

/// <summary>
/// 行右侧取值的抽象基类。每个子类在 SettingsOverlay.axaml 里都有且只有一个隐式 DataTemplate，
/// 靠 DataType 精确匹配到具体控件；继承 ObservableObject 是为了双向绑定能推送属性变更。
/// </summary>
public abstract class SettingValue : ObservableObject
{
}

/// <summary>单行文本输入：渲染成 <c>TextBox</c>（宽 196，水印取 Placeholder），Text 双向绑定。</summary>
public sealed class TextValue : SettingValue
{
    private string _text = string.Empty;
    public string Text
    {
        get => _text;
        set => Set(ref _text, value);
    }

    public string? Placeholder { get; init; }
}

/// <summary>
/// 下拉单选：渲染成 <c>ComboBox</c>。Options 是候选项，Selected 双向绑定当前值，
/// Width 让每个下拉可以按内容长度单独定宽（默认 158），避免右列宽度来回跳。
/// </summary>
public sealed class SelectValue : SettingValue
{
    private string _selected = string.Empty;
    public ObservableCollection<string> Options { get; } = new();

    public string Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    public double Width { get; init; } = 158;
}

/// <summary>
/// 开关：渲染成挂了 SwitchToggle 主题（ControlTheme）的 <c>ToggleButton</c>，
/// IsOn 双向绑定 IsChecked；不需要自己写开/关两个状态，切换态都由主题内的伪类样式处理。
/// </summary>
public sealed class ToggleValue : SettingValue
{
    private bool _isOn;
    public bool IsOn
    {
        get => _isOn;
        set => Set(ref _isOn, value);
    }
}

/// <summary>
/// 滑块：渲染成 <c>ctrl:TrackBar</c> 加右侧数值文本。Min/Max/Unit 是区间与单位，
/// Value 双向绑定并在赋值时四舍五入取整，这样滑块拖动不会产生 99.999 这类脏数据。
/// </summary>
public sealed class RangeValue : SettingValue
{
    private double _value;

    public double Min { get; init; }
    public double Max { get; init; } = 100;
    public string Unit { get; init; } = string.Empty;
    public bool IsPercent { get; init; }

    public double Value
    {
        get => _value;
        set
        {
            // Set 返回 false 说明值没变，直接返回避免重复触发通知
            if (!Set(ref _value, Math.Round(value))) return;
            // DisplayText 是计算属性，Set 只会通知 Value，这里要手动补一次通知
            OnPropertyChanged(nameof(DisplayText));
        }
    }

    /// <summary>滑块右侧展示的读数，例如 100%（Unit 自带空格时如 " 份" 也一样拼接）。</summary>
    public string DisplayText => $"{Value:0}{Unit}";
}

/// <summary>单个标签块：渲染成 ChipToggle 主题的 ToggleButton，IsSelected 来自 SelectableItem。</summary>
public sealed class ChipItem : SelectableItem
{
    public string Label { get; init; } = string.Empty;
}

/// <summary>多选标签组：渲染成 WrapPanel 布局的 ItemsControl，内部按 ChipItem 再匹配一层模板。</summary>
public sealed class ChipsValue : SettingValue
{
    public ObservableCollection<ChipItem> Items { get; } = new();
}

/// <summary>
/// 按钮：渲染成普通 <c>Button</c>，只通过 Text 决定文案。三种语气（默认/主按钮/危险）
/// 由 IsPrimary、IsDanger 两个开关组合，IsNormal 供需要反向判断的场景使用。
/// </summary>
public sealed class ButtonValue : SettingValue
{
    public string Text { get; init; } = string.Empty;
    public bool IsPrimary { get; init; }
    public bool IsDanger { get; init; }

    // 空命令兜底：没接线的按钮点了也不报错，避免 Command 为 null 时按钮自动变成禁用态
    private static readonly ICommand Noop = new RelayCommand(() => { });

    public ICommand? Command { get; init; } = Noop;

    public bool IsNormal => !IsDanger && !IsPrimary;

    // 直接用画刷驱动外观，避免类名绑定在不同控件间行为不一致
    // 三个只读画刷由状态一次性算好，XAML 侧只做 {Binding}，不用写触发器或 Classes 组合
    // 语气是多维组合（普通/主/危险），靠 Classes 表达就要为每种组合各写一份样式，
    // 放在 VM 里用三元表达式算反而更短，也省掉一个 bool 到画刷的值转换器
    public Avalonia.Media.IBrush Background => IsDanger
        ? Palette.Alpha("#F8716E", 0.17)
        : IsPrimary
            ? Palette.Gradient("#7A96FF", "#5C7BF7")
            : Palette.Alpha("#FFFFFF", 0.04);

    public Avalonia.Media.IBrush BorderBrush => IsDanger
        ? Palette.Alpha("#F8716E", 0.45)
        : IsPrimary
            ? Palette.Alpha("#FFFFFF", 0.14)
            : Palette.Alpha("#FFFFFF", 0.12);

    public Avalonia.Media.IBrush TextBrush => IsDanger
        ? Palette.Solid("#FF8B88")
        : IsPrimary
            ? Avalonia.Media.Brushes.White
            : Palette.Solid("#9BA3B7");
}

/// <summary>只读信息：渲染成等宽字体的 <c>TextBlock</c>，用于版本号、运行时这类不可编辑的值。</summary>
public sealed class InfoValue : SettingValue
{
    public string Text { get; init; } = string.Empty;
}

/// <summary>路径展示：渲染成只读 <c>TextBox</c>（方便复制）加一个「打开」按钮。</summary>
public sealed class PathValue : SettingValue
{
    public string Text { get; init; } = string.Empty;
}

/// <summary>单个色块：渲染成 Classes="swatch" 的圆形色按钮，Command 负责互斥选中。</summary>
public sealed class SwatchItem : SelectableItem
{
    public IBrush Brush { get; init; } = Palette.Solid("#6C8BFF");
    public ICommand Command { get; init; } = null!;
}

/// <summary>色板：渲染成横向排列的一排色块，用于强调色选择。</summary>
public sealed class PaletteValue : SettingValue
{
    public ObservableCollection<SwatchItem> Swatches { get; } = new();
}

// ===================== 行 / 分组 / 卡片 =====================

/// <summary>
/// 一个设置行：左侧标题 + 可选描述，右侧 Value 承载任意 SettingValue 子类。
/// Value 之所以声明成 object，是为了让同一个集合能混装文本、开关、按钮等不同控件类型，
/// 由 XAML 在运行时按真实类型挑模板；HasDesc / HasValue 用来直接驱动 IsVisible，
/// 避免在 XAML 里写 null 转换器。
/// </summary>
public sealed class SettingRow
{
    public string Title { get; init; } = string.Empty;
    public string? Desc { get; init; }
    public bool HasDesc => !string.IsNullOrEmpty(Desc);
    public object? Value { get; init; }
    public bool HasValue => Value is not null;
}

/// <summary>一组设置行：渲染成「小标题 + 圆角卡片」，卡片内的行之间用 1px 分隔线连接。</summary>
public sealed class SettingGroup
{
    public string Title { get; init; } = string.Empty;
    public ObservableCollection<SettingRow> Rows { get; } = new();
}

/// <summary>资源链接行：图标 + 主标题 + 副标题 + 右侧箭头，目前仅「关于」分区使用。</summary>
public sealed class LinkItem
{
    public string Icon { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Sub { get; init; } = string.Empty;
    public ICommand? Command { get; init; }
}

/// <summary>账号资料卡：位于「账号」分区顶部，展示头像首字、昵称、角色、邮箱与加入时间。</summary>
public sealed class AccountCard
{
    public string Initial { get; init; } = "陈";
    public string Name { get; init; } = "陈墨";
    public string Role { get; init; } = "管理员";
    public string Email { get; init; } = "chenmo@aster.io";
    public string Joined { get; init; } = "2024 年 3 月加入 · 最近登录 2 分钟前";
}

/// <summary>关于卡片：应用名、版本与「已是最新」提示，位于「关于」分区顶部。</summary>
public sealed class AboutCard
{
    public string Name { get; } = "Aster";
    public string Version { get; } = "v1.4.2";
    public string Meta { get; } = "已是最新 · 2026-10-12 发布";
}

/// <summary>
/// 一个设置分区：左栏一项 + 右栏一整页内容。继承 SelectableItem 拿到 IsSelected，
/// 左栏按钮用 Classes.selected 绑定它来实现高亮；Command 里带 CommandParameter=自身，
/// 因此各分区的 Command 可以共用同一个 SelectCommand。
/// TopCard / HasTopCard 用于可选的顶部卡片（账号卡、关于卡），Links / HasLinks 用于可选资源区。
/// </summary>
public sealed class SettingSection : SelectableItem
{
    public string Key { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public ICommand Command { get; init; } = null!;
    public ObservableCollection<SettingGroup> Groups { get; } = new();
    public ObservableCollection<LinkItem> Links { get; } = new();
    public bool HasLinks => Links.Count > 0;
    public object? TopCard { get; init; }
    public bool HasTopCard => TopCard is not null;
}

/// <summary>
/// 设置浮层的视图模型：构造 7 个分区、维护当前选中项、提供左栏底部文案。
/// 构造函数入参是外部传入的「退出登录」命令，见字段 _logoutCommand 的注释。
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private SettingSection? _current;

    // 退出登录属于应用级流程（要切回登录页、清空会话），本类不该自己实现，
    // 因此由 MainWindowViewModel 在构造时注入 LogoutCommand，这里只负责挂到按钮上。
    private readonly ICommand? _logoutCommand;

    public SettingsViewModel(ICommand? logoutCommand = null)
    {
        _logoutCommand = logoutCommand;

        // 分区切换的唯一入口：参数是左栏按钮传来的 SettingSection（CommandParameter）
        SelectCommand = new RelayCommand(o =>
        {
            if (o is SettingSection s) Select(s);
        });

        // 色板是单选：把所有色块的 IsSelected 重算一遍，只保留被点中的那个。
        // 即使点到已选中的色块也不会取消选中，避免出现「一个都不选」的无效状态。
        SwatchCommand = new RelayCommand(o =>
        {
            if (o is not SwatchItem picked) return;
            foreach (var s in _allSwatches) s.IsSelected = ReferenceEquals(s, picked);
        });

        // 分区顺序即左栏顺序；每个 BuildXxx 独立构造成一组行，彼此不共享状态
        var sections = new[]
        {
            BuildAccount(),
            BuildGeneral(),
            BuildBoard(),
            BuildMembers(),
            BuildNotify(),
            BuildStorage(),
            BuildAbout()
        };

        foreach (var s in sections) Sections.Add(s);
        // 默认选中第一个分区，保证浮层一打开右栏就有内容，不会出现空白页
        Select(Sections[0]);
    }

    public ObservableCollection<SettingSection> Sections { get; } = new();
    public ICommand SelectCommand { get; }
    public ICommand SwatchCommand { get; private set; } = null!;

    // 当前分区：setter 私有，只能经由 Select 修改，确保左栏高亮与右栏内容永远同步
    public SettingSection? Current
    {
        get => _current;
        private set => Set(ref _current, value);
    }

    /// <summary>左栏底部那行小字，XAML 里绑定 Settings.Footer。</summary>
    public string Footer => $"Aster v1.4.2 · 设置已同步至本地";

    // 全量色块索引：色板只在「通用」分区创建，但单选逻辑写在构造函数里，
    // 用一个列表把它们收集起来，避免命令里再去遍历界面树。
    private readonly List<SwatchItem> _allSwatches = new();

    /// <summary>
    /// 切换到指定分区：先把所有分区的 IsSelected 复位成「是否等于目标」，再更新 Current。
    /// 先改选中态再改 Current，可让右栏内容与左栏高亮在同一帧内一起变化。
    /// </summary>
    private void Select(SettingSection section)
    {
        foreach (var s in Sections) s.IsSelected = ReferenceEquals(s, section);
        Current = section;
    }

    /// <summary>
    /// 构造一个分组。title 是分组小标题，rows 用 params 展开，调用处可以一行写完一个分组；
    /// 返回的 SettingGroup 由调用方决定挂到哪个分区（有的分区需要先建组再逐行 Add）。
    /// </summary>
    private static SettingGroup Group(string title, params SettingRow[] rows)
    {
        var g = new SettingGroup { Title = title };
        foreach (var r in rows) g.Rows.Add(r);
        return g;
    }

    /// <summary>
    /// 构造一行：title 必填，desc 传 null 表示这一行不显示描述，value 传 null 表示右侧留空。
    /// 三个参数都走对象初始化器，所以调用处能写成一目了然的单行声明。
    /// </summary>
    private static SettingRow Row(string title, string? desc, object? value)
        => new() { Title = title, Desc = desc, Value = value };

    /// <summary>
    /// 构造一个下拉框：options 为候选项，默认选中第一项（没有任何选项时退化为空字符串）。
    /// 方法名 Select 与类型名 SelectValue 能共存，是因为它们是两个不同标识符；
    /// 静态调用 Select(...) 也不会和 LINQ 的 Enumerable.Select 扩展方法冲突——
    /// 扩展方法只出现在实例的链式调用上，静态按名字解析时优先命中本类方法。
    /// </summary>
    private static SelectValue Select(params string[] options)
    {
        var v = new SelectValue();
        foreach (var o in options) v.Options.Add(o);
        v.Selected = options.FirstOrDefault() ?? string.Empty;
        return v;
    }

    /// <summary>
    /// 构造一组标签块：labels 为文案，前 onCount 个默认选中，其余默认关闭。
    /// 用「前 N 个选中」而不是逐个传 bool，是因为这里的多选只是演示性的默认值，写法越短越好；
    /// 方法名 Chips 与类型名 ChipsValue 同理不会冲突，二者是不同的标识符。
    /// </summary>
    private static ChipsValue Chips(int onCount, params string[] labels)
    {
        var v = new ChipsValue();
        for (var i = 0; i < labels.Length; i++)
            v.Items.Add(new ChipItem { Label = labels[i], IsSelected = i < onCount });
        return v;
    }

    /// <summary>
    /// 账号分区：顶部 AccountCard 资料卡，下面分「基本信息（昵称/邮箱/手机/部门）、
    /// 安全（改密码/两步验证/登录设备）、会话（退出登录/注销账号）」三组。
    /// 其中「退出登录」按钮的命令来自构造函数注入，其余按钮只挂默认空命令。
    /// </summary>
    private SettingSection BuildAccount()
    {
        var s = new SettingSection
        {
            Key = "account", Title = "账号", Icon = Controls.Icons.User,
            Subtitle = "个人信息、安全设置与登录会话",
            Command = SelectCommand,
            // 顶部资料卡：右栏会把 TopCard 交给一个 ContentControl，靠类型匹配到「账号卡」模板
            TopCard = new AccountCard()
        };
        s.Groups.Add(Group("基本信息",
            Row("显示名称", "在其他成员可见的位置展示的名字", new TextValue { Text = "陈墨" }),
            Row("邮箱", "用于登录与接收通知 · 已验证", new TextValue { Text = "chenmo@aster.io" }),
            // 手机号未绑定：Text 留空、只给 Placeholder，输入框的水印就充当了「未绑定」提示
            Row("手机号", "用于二次验证与安全提醒 · 未绑定", new TextValue { Placeholder = "未绑定" }),
            Row("所属部门", "决定可查看的数据范围", Select("产品部", "技术部", "运营部"))));
        s.Groups.Add(Group("安全",
            Row("登录密码", "上次修改：2026-08-14", new ButtonValue { Text = "修改密码" }),
            Row("两步验证", "登录时需要输入额外的动态验证码", new ToggleValue { IsOn = true }),
            Row("登录设备", "当前有 3 台设备保持登录状态", new ButtonValue { Text = "管理设备" })));
        s.Groups.Add(Group("会话",
            // 唯一接了真实命令的按钮：_logoutCommand 由 MainWindowViewModel 注入，点击后回到登录页
            Row("退出当前账号", "返回登录界面，本地缓存不会被清除",
                new ButtonValue { Text = "退出登录", IsDanger = true, Command = _logoutCommand }),
            // 危险语气但没有接命令，属于设计稿占位；Command 落到 Noop，按钮仍可点击且不报错
            Row("注销账号", "永久删除账号及全部关联数据，无法恢复",
                new ButtonValue { Text = "注销账号", IsDanger = true })));
        return s;
    }

    /// <summary>
    /// 通用分区：覆盖「外观（主题标签、强调色色板、界面缩放滑块）、
    /// 区域与语言（语言、日期格式、货币单位）、启动与窗口（恢复上次页面、最小化到托盘）」。
    /// </summary>
    private SettingSection BuildGeneral()
    {
        // 主题是三选一的 chips：只在「深色」上给选中态，其余默认关闭
        var theme = Chips(1, "深色", "浅色", "跟随系统");

        // 强调色用色板而非下拉，6 个色值来自 Palette 里的设计稿色
        var palette = new PaletteValue();
        var colors = new[] { "#6C8BFF", "#A78BFA", "#3DD6E8", "#3DDC97", "#F5B942", "#F8716E" };
        foreach (var c in colors)
            palette.Swatches.Add(new SwatchItem { Brush = Palette.Solid(c), Command = SwatchCommand });
        // 注册到全量索引，供 SwatchCommand 做互斥选择；默认点亮第一个
        foreach (var sw in palette.Swatches) _allSwatches.Add(sw);
        palette.Swatches[0].IsSelected = true;

        var s = new SettingSection
        {
            Key = "general", Title = "通用", Icon = Controls.Icons.Sliders,
            Subtitle = "外观、区域语言与启动行为的偏好设置",
            Command = SelectCommand
        };

        // 「外观」组先建组再逐行 Add：因为 Row 的参数里要复用到上面创建的 theme / palette 局部变量，
        // 用 Group(...) 一次性写会太长，分行添加可读性更好
        var appearance = Group("外观");
        appearance.Rows.Add(Row("主题", "切换明暗配色，选择「跟随系统」会随操作系统外观自动切换", theme));
        appearance.Rows.Add(Row("强调色", "用于按钮、选中态与图表高亮", palette));
        // 滑块区间 80%~130%，Unit 填了 % 让 DisplayText 直接拼出「100%」
        appearance.Rows.Add(Row("界面缩放", "调整整体 UI 尺寸，建议高分屏适当放大",
            new RangeValue { Min = 80, Max = 130, Value = 100, Unit = "%" }));
        s.Groups.Add(appearance);

        s.Groups.Add(Group("区域与语言",
            Row("界面语言", "切换后需要重启应用生效", Select("简体中文", "English", "日本語")),
            Row("日期与时间格式", "用于报表、日志与成员活跃时间",
                Select("2026-10-20 14:30", "2026/10/20 14:30", "10月20日 14:30")),
            Row("货币单位", "影响营收类指标与导出报表的计价符号",
                Select("人民币 CNY (¥)", "美元 USD ($)", "欧元 EUR (€)"))));

        s.Groups.Add(Group("启动与窗口",
            Row("启动时恢复上次页面", "打开应用后回到上次停留的模块与筛选条件", new ToggleValue { IsOn = true }),
            Row("关闭窗口时最小化到托盘", "关闭主窗口后保持在系统托盘运行，不退出进程", new ToggleValue { IsOn = true })));
        return s;
    }

    /// <summary>
    /// 数据看板分区：覆盖「刷新与范围（刷新频率、默认时间范围、后台持续刷新）、
    /// 指标卡（显示哪几张卡、数值缩写、环比）、图表与表格（趋势图类型、分页行数、导出格式）」。
    /// </summary>
    private SettingSection BuildBoard()
    {
        var s = new SettingSection
        {
            Key = "board", Title = "数据看板", Icon = Controls.Icons.Bars,
            Subtitle = "控制管理页的刷新策略、指标卡与图表呈现",
            Command = SelectCommand
        };
        s.Groups.Add(Group("刷新与范围",
            Row("数据刷新频率", "自动重新拉取看板数据的间隔",
                Select("每 5 分钟", "实时推送", "每 30 秒", "仅手动刷新")),
            Row("默认时间范围", "进入管理页时预先选中的统计区间",
                Select("近 7 天", "今日", "本月", "近 30 天")),
            Row("后台持续刷新", "窗口失焦时仍保持数据更新", new ToggleValue())));
        s.Groups.Add(Group("指标卡",
            Row("显示的指标", "选择在管理页顶部展示的统计卡片，最多四项",
                Chips(4, "活跃用户", "订单总量", "总营收", "转化率")),
            Row("数值缩写", "大数字以 12.4k / 1.2M 形式显示", new ToggleValue { IsOn = true }),
            Row("显示环比", "在指标卡上展示相较上一周期的涨跌", new ToggleValue { IsOn = true })));
        s.Groups.Add(Group("图表与表格",
            Row("趋势图类型", "访问与转化趋势的默认呈现方式", Select("面积图", "折线图", "柱状图")),
            Row("表格每页行数", "成员与订单列表的分页大小", Select("5 行", "10 行", "20 行", "50 行")),
            Row("默认导出格式", "点击导出按钮时使用的文件类型", Select("Excel (.xlsx)", "CSV (.csv)", "PDF (.pdf)"))));
        return s;
    }

    /// <summary>
    /// 成员与权限分区：覆盖「权限（新成员默认角色、是否允许互相邀请、邀请链接有效期）、
    /// 登录与安全（会话超时、强制双因素、异地登录提醒）、审计（操作日志、日志保留时长）」。
    /// </summary>
    private SettingSection BuildMembers()
    {
        var s = new SettingSection
        {
            Key = "member", Title = "成员与权限", Icon = Controls.Icons.Users,
            Subtitle = "新成员默认权限、邀请策略与登录安全",
            Command = SelectCommand
        };
        s.Groups.Add(Group("权限",
            Row("新成员默认角色", "受邀加入但尚未分配权限时的初始角色", Select("访客", "编辑", "管理员")),
            Row("允许成员邀请他人", "关闭后仅管理员可以发出邀请", new ToggleValue()),
            Row("邀请链接有效期", "超时未使用的邀请链接会自动失效", Select("7 天", "24 小时", "30 天"))));
        s.Groups.Add(Group("登录与安全",
            Row("会话超时", "无操作超过该时长后需要重新登录", Select("2 小时", "30 分钟", "8 小时", "永不超时")),
            Row("强制双因素认证", "管理员角色登录时必须完成二次验证", new ToggleValue { IsOn = true }),
            Row("异地登录提醒", "检测到非常用地区登录时发送通知", new ToggleValue { IsOn = true })));
        s.Groups.Add(Group("审计",
            Row("记录操作日志", "保存成员的增删改与登录记录，用于追溯", new ToggleValue { IsOn = true }),
            Row("日志保留时长", "超出时长的日志会被自动归档删除", Select("90 天", "30 天", "1 年", "永久保留"))));
        return s;
    }

    /// <summary>
    /// 通知分区：覆盖「推送方式（邮件、桌面、声音）与内容订阅（每日摘要、发送时间、
    /// 指标异常告警、告警阈值）」。全部是开关与下拉，没有自定义控件。
    /// </summary>
    private SettingSection BuildNotify()
    {
        var s = new SettingSection
        {
            Key = "notify", Title = "通知", Icon = Controls.Icons.Bell,
            Subtitle = "数据提醒、异常告警与摘要的推送方式",
            Command = SelectCommand
        };
        s.Groups.Add(Group("推送方式",
            Row("邮件通知", "发送到账号绑定的邮箱", new ToggleValue { IsOn = true }),
            Row("桌面通知", "通过系统通知中心弹出提醒", new ToggleValue { IsOn = true }),
            Row("声音提醒", "收到重要告警时播放提示音", new ToggleValue())));
        s.Groups.Add(Group("内容订阅",
            Row("每日数据摘要", "汇总当日活跃、订单与营收表现", new ToggleValue { IsOn = true }),
            Row("摘要发送时间", "按账号所在时区计算", Select("每天 09:00", "每天 12:00", "每天 18:00")),
            Row("指标异常告警", "关键指标跌破阈值时立即通知", new ToggleValue { IsOn = true }),
            Row("告警触发阈值", "相较上一周期跌幅达到该值时触发", Select("跌幅 20%", "跌幅 10%", "跌幅 30%"))));
        return s;
    }

    /// <summary>
    /// 存储与备份分区：覆盖「位置（数据目录路径、缓存清理）、备份（自动备份、频率、
    /// 保留份数）、数据管理（全量导出、历史明细保留期）」。
    /// </summary>
    private SettingSection BuildStorage()
    {
        // 数据目录按当前用户的 Roaming 目录拼出来，跨机器展示时不会写死某台机器的路径；
        // 末尾用 DirectorySeparatorChar 而不是 "/"，保证 Windows 与类 Unix 下都正确
        var dataDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) +
                      System.IO.Path.DirectorySeparatorChar + "Aster";

        var s = new SettingSection
        {
            Key = "storage", Title = "存储与备份", Icon = Controls.Icons.Database,
            Subtitle = "数据目录、缓存清理与备份策略",
            Command = SelectCommand
        };

        // 同样先建组再逐行 Add：数据目录这一行要复用上面算出的 dataDir
        var location = Group("位置");
        location.Rows.Add(Row("数据目录", "业务数据库、导出文件与配置的存放位置",
            new PathValue { Text = dataDir }));
        location.Rows.Add(Row("缓存占用", "报表缩略图、查询结果与临时导出文件",
            new ButtonValue { Text = "清理" }));
        s.Groups.Add(location);

        s.Groups.Add(Group("备份",
            Row("自动备份", "定期将数据库与配置打包到备份目录", new ToggleValue { IsOn = true }),
            Row("备份频率", "上次备份：今天 09:00", Select("每天", "每周", "每月")),
            Row("保留份数", "超出数量的旧备份会被自动清理",
                // 3~30 份，Unit 带前导空格，读数显示成「10 份」而不是「10份」
                new RangeValue { Min = 3, Max = 30, Value = 10, Unit = " 份" })));

        s.Groups.Add(Group("数据管理",
            Row("导出全部业务数据", "导出成员、订单与统计数据，用于迁移或归档",
                new ButtonValue { Text = "立即导出", IsPrimary = true }),
            Row("历史数据保留期", "超过保留期的明细数据会被聚合后删除",
                Select("永久保留", "1 年", "2 年", "3 年"))));
        return s;
    }

    /// <summary>
    /// 关于分区：顶部 AboutCard，下面「运行环境」组展示框架/运行时/渲染后端/存储方式，
    /// 末尾 Links 追加 4 条资源链接（官网、更新日志、反馈、许可），由右栏单独渲染。
    /// </summary>
    private SettingSection BuildAbout()
    {
        // 版本号不写死：取 Avalonia.Controls 程序集的元数据版本，升级 NuGet 后自动跟着变；
        // ToString(3) 只保留 主版本.次版本.修订号，末端的 ?? 兜底防止某些构建拿不到 Version
        var avalonia = typeof(Avalonia.Controls.Window).Assembly.GetName().Version?.ToString(3) ?? "11.3.22";
        // .NET 运行时版本也只取主.次.修订三段，和界面里其它版本号的写法保持一致
        var runtime = Environment.Version.ToString(3);

        var s = new SettingSection
        {
            Key = "about", Title = "关于", Icon = Controls.Icons.Info,
            Subtitle = "版本信息与相关资源",
            Command = SelectCommand,
            // 顶部关于卡：右栏的 ContentControl 靠类型匹配到 AboutCard 模板
            TopCard = new AboutCard()
        };

        // 这几行 desc 传 null：运行环境本身没有解释性文案，行高会更紧凑
        s.Groups.Add(Group("运行环境",
            Row("界面框架", null, new InfoValue { Text = $"Avalonia {avalonia}" }),
            Row("运行时", null, new InfoValue { Text = $".NET {runtime} · {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}" }),
            Row("渲染后端", null, new InfoValue { Text = "Skia" }),
            Row("数据存储", null, new InfoValue { Text = "本地文件 · JSON" })));

        // Links 塞在分区而不是分组里：右栏会把它单独渲染成「资源」小节，
        // 只有 HasLinks 为 true 的分区才出现这一块
        s.Links.Add(new LinkItem { Icon = Controls.Icons.Globe, Title = "官方网站", Sub = "aster.studio" });
        s.Links.Add(new LinkItem { Icon = Controls.Icons.Book, Title = "更新日志", Sub = "v1.4.2" });
        s.Links.Add(new LinkItem { Icon = Controls.Icons.Message, Title = "问题反馈", Sub = "GitHub Issues" });
        s.Links.Add(new LinkItem { Icon = Controls.Icons.Doc, Title = "开源许可", Sub = "MIT / 第三方组件" });
        return s;
    }
}
