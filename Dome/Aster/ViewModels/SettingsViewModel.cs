// ============================================================================
// SettingsViewModel.cs —— 设置浮层的全部表单数据（纯视图模型，不含业务副作用）
// 核心设计：数据驱动的表单。分区 SettingSection → 分组 SettingGroup → 行 SettingRow，
// 行右侧的 Value 是 SettingValue 的具体子类，XAML 靠隐式 DataTemplate 按类型匹配控件，
// 所以增删或调整表单项只改本文件，完全不用动 SettingsOverlay.axaml。
// 与外部的关系：MainWindowViewModel 持有本类实例（Settings 属性），并用 IsSettingsOpen
// 控制浮层显隐；本类只决定"当前选中哪个分区"，关闭浮层交给外部命令。
// 易踩的坑：Value 声明为 object，模板靠运行时类型匹配——新增子类若忘了在 XAML 补模板，
// 该行会退化成 ToString() 纯文本；另外 Select/Chips 这类工厂方法名与类型名近似，别混淆。
//
// ============================ 数据流链路 ============================
// 一次点击从左栏传到右栏某个控件的完整回路，共 7 跳。每一跳都标出「谁通知谁」：
//
// 跳 1  Sections（左栏 8 项）
//       本类构造时一次性 Add 进去的 ObservableCollection<SettingSection>，运行期不再增删，
//       所以左栏的 ItemsControl 只会渲染一次，之后只靠元素的 IsSelected 变色。
//
// 跳 2  点按钮 → SelectCommand（本类构造函数里 new 的 RelayCommand）
//       Button.Command 绑的是「分区对象自己的 Command」（8 个分区共用同一个 SelectCommand），
//       CommandParameter="{Binding}" 把被点的那个 SettingSection 作为参数交出去。
//       通知方向：视图 → VM，由 Button 主动调用 ICommand.Execute。
//
// 跳 3  Select(s) 里的第一件事：遍历 Sections 重置 IsSelected
//       每个 IsSelected 都是 SelectableItem 上带 Set(...) 的属性，值一变就发 PropertyChanged；
//       谁在听：左栏按钮的 Classes.selected="{Binding IsSelected}"。
//       Avalonia 的样式系统收到通知后重新匹配 Button.setitem.selected 这条选择器，
//       把强调色底 / 描边 / 文字色挂上去或摘下来 —— 这就是「左侧高亮」的唯一来源。
//
// 跳 4  Select(s) 里的第二件事：Current = s
//       Current 的 setter 是私有，全类只有 Select 能写，保证左栏与右栏永远指向同一项。
//       谁在听：右栏 <ContentControl Content="{Binding Settings.Current}"/>。
//       注意绑定路径以 Settings. 开头，因为本控件的 x:DataType 是 MainWindowViewModel。
//
// 跳 5  ContentControl 拿到新的 SettingSection 后，按运行时类型去 DataTemplates 里找模板
//       命中 DataType="{x:Type vm:SettingSection}" 那一份，整页内容被重建（不是复用旧树）。
//       这一跳没有 PropertyChanged 参与，是 Avalonia 的「按类型挑模板」自动完成的。
//
// 跳 6  模板内的 ItemsControl ItemsSource="{Binding Groups}" 逐项展开
//       Groups 是 ObservableCollection，界面靠集合变更事件刷新（本类构造期就已填满，
//       运行期不再变动，所以实际只在首次绑定时读一遍）；每个元素再按 SettingGroup 匹配模板。
//       SettingGroup 模板里又是 ItemsControl → Rows → SettingRow 模板，同样逐层下钻。
//
// 跳 7  SettingRow 模板右侧的 ContentControl Content="{Binding Value}"
//       Value 声明成 object，所以这里发生「第二次类型匹配」：拿到 TextValue 就渲染 TextBox、
//       拿到 ToggleValue 就渲染挂了 SwitchToggle 主题的 ToggleButton，以此类推。
//       到了控件层再反向双向绑定回子类属性（如 Text、IsOn、Value），
//       用户在界面上改值 → 写回子类字段 → 子类发通知 → 控件刷新，回路闭合。
//
// 一句话总结：跳 2~4 是本类主动推的属性通知，跳 5~7 全靠 Avalonia 按类型找模板。
// 正因为后半段是自动的，本类完全不需要知道界面上有哪些控件；
// 反过来，新增一种 Value 子类时必须同时在 SettingsOverlay.axaml 补一份 DataTemplate，
// 否则跳 7 匹配不到，该行会退化成 ToString() 的纯文本。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Aster.Services;
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
// 影响哪几行：账号分区的「显示名称」「邮箱」两行（有初值），以及「手机号」一行（只有 Placeholder，
// Text 为空字符串，输入框里显示灰色水印「未绑定」）。改这个类只会同时影响这三行。
//
// Text 的 setter 走 Set(ref _text, value)：值真的变了才发 PropertyChanged(nameof(Text))，
// 模板里只有 TextBox 绑了 Text，没有派生属性要补通知。
// Placeholder 是 init-only：XAML 的水印在模板创建时读一次即可，运行期不会变，所以不必可写。
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
// 影响哪几行：所有需要「从固定集合里挑一个」的设置——所属部门、界面语言、日期与时间格式、
// 货币单位、数据刷新频率、默认时间范围、趋势图类型、表格每页行数、默认导出格式、
// 新成员默认角色、邀请链接有效期、会话超时、摘要发送时间、告警触发阈值、备份频率、
// 历史数据保留期。它们全是同一个类，差别只在构造时给的 Options 与 Width。
//
// Options 直接用只读的 ObservableCollection 暴露出去：模板里 ItemsSource 绑它，
// 本类构造完就不再改，所以不需要额外的「Options 变了」通知。
// Selected 存的是字符串本身（不是索引），ComboBox 靠引用/相等比较定位选中项，
// 因此选项文案必须唯一，否则会出现选中跳回第一项的现象。
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
// 影响哪几行：所有「是/否」型设置——两步验证、启动时恢复上次页面、关闭窗口时最小化到托盘、
// 后台持续刷新、数值缩写、显示环比、允许成员邀请他人、强制双因素认证、异地登录提醒、
// 记录操作日志、邮件/桌面/声音通知、每日数据摘要、指标异常告警、自动备份。共 16 行，全部同一类型。
//
// 关键点：本类完全不描述外观。开/关的轨道颜色、滑块位置都写在 Theme.axaml 的
// SwitchToggle 主题里，靠 ToggleButton 自身的 :checked 伪类切换；
// 本类只提供一个 bool，连「开」是什么颜色都不知道 —— 这样换配色时不用碰 VM。
// IsOn 的 setter 用 Set(...)，值变即通知，双向绑定下这是用户点开关后控件回写的落点。
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
// 影响哪几行：只有两行——「通用 → 外观 → 界面缩放」（80~130，Unit 为 "%"）与
// 「存储与备份 → 备份 → 保留份数」（3~30，Unit 为 " 份"，带前导空格）。
// 两条滑块共用本类，区间和单位都靠构造时的 init 值区分。
//
// Min/Max/Unit/IsPercent 全是 init-only：滑块的量程在模板绑定时读一次，
// 运行期不该变，否则轨道刻度与已拖动的值会打架。
// IsPercent 目前没有任何代码读取（模板靠 Unit 里的 "%" 决定读数后缀），
// 保留它是给「需要按百分比换算的业务逻辑」留的语义标记。
//
// 下面 Value 的写法为什么绕，分三步看：
// 步骤 1  Math.Round(value) 先取整再交给 Set。TrackBar 拖动时给的是连续 double
//         （比如 99.62），不取整的话读数会显示奇怪小数，而且几乎每帧都「值变了」。
// 步骤 2  Set 内部先用 EqualityComparer 比较。取整后若仍是同一个整数，说明用户只是
//         在同一像素格内抖动，Set 返回 false，直接 return —— 既省掉一次无意义重绘，
//         也避免把抖动写进历史记录（若以后加撤销栈，这里就是天然的去重闸门）。
// 步骤 3  DisplayText 是表达式体计算属性，不是字段。Set 只会发出
//         PropertyChanged(nameof(Value))（靠 [CallerMemberName] 推出来的），
//         绑定引擎不会自动推导「Value 变了所以 DisplayText 也变了」，
//         所以必须手动补一次 OnPropertyChanged(nameof(DisplayText))。
//         漏了这一行的典型症状：滑块拖动了，右侧读数还停在旧数字。
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
// 本类不是 SettingValue（不直接当某一行的 Value），而是 ChipsValue.Items 的元素类型，
// 因此在 SettingRow 那一层匹配不到它，必须再套一层 ItemTemplate 才会被渲染
// （见 SettingsOverlay.axaml 里 ChipsValue 模板内层的那份 DataTemplate）。
// IsSelected 继承自 SelectableItem，是标签「是否点亮」的唯一数据源；
// 双向绑定后点击标签就是直接翻转这个 bool，没有任何命令参与 —— 与色板的互斥单选不同。
public sealed class ChipItem : SelectableItem
{
    public string Label { get; init; } = string.Empty;
}

/// <summary>多选标签组：渲染成 WrapPanel 布局的 ItemsControl，内部按 ChipItem 再匹配一层模板。</summary>
// 影响哪几行：只有两行——「通用 → 外观 → 主题」（深色/浅色/跟随系统，由 Chips(1, ...) 造）
// 与「数据看板 → 指标卡 → 显示的指标」（四张指标卡，由 Chips(4, ...) 造）。
// 注意两行的语义其实不同：主题是「三选一」，但本类没有互斥逻辑，
// 用户点「浅色」之后「深色」仍然亮着 —— 这是代码行为与直觉不符的地方之一（见文件末尾说明）。
// Items 只读暴露：集合在构造时填满，运行期增删才会刷新界面，这里不存在增删。
public sealed class ChipsValue : SettingValue
{
    public ObservableCollection<ChipItem> Items { get; } = new();
}

/// <summary>
/// 按钮：渲染成普通 <c>Button</c>，只通过 Text 决定文案。三种语气（默认/主按钮/危险）
/// 由 IsPrimary、IsDanger 两个开关组合，IsNormal 供需要反向判断的场景使用。
/// </summary>
// 影响哪几行：全部 6 个按钮行——「修改密码」「管理设备」（常态）、「退出登录」「注销账号」（danger）、
// 「清理」（常态）、「立即导出」（primary）。语气完全由 IsPrimary / IsDanger 两个 init 开关决定。
//
// 设计取舍：外观不写在 XAML 里，而是让 VM 直接暴露三个 IBrush。原因是语气是「两个 bool 的组合」，
// 若用 Classes 表达就得为「危险 / 主 / 普通」各写一份 Button.xxx 样式，还要在 XAML 里写
// Classes.danger="{Binding IsDanger}" 与 Classes.primary="{Binding IsPrimary}" 两条条件类，
// 且两个都为 true 时样式优先级由声明顺序决定、结果不可预期；放在 VM 里用三元表达式算，
// 优先级一眼可见，也省掉一个 bool 到画刷的转换器。
// 代价：换主题配色时这几个色值不在 Theme.axaml 里，改配色要同时翻到这个文件。
public sealed class ButtonValue : SettingValue
{
    public string Text { get; init; } = string.Empty;
    public bool IsPrimary { get; init; }
    public bool IsDanger { get; init; }

    // 空命令兜底：没接线的按钮点了也不报错，避免 Command 为 null 时按钮自动变成禁用态
    // 补充说明（与上一行的说法不完全一致，列入待确认项）：Avalonia 11 的 Button 在 Command 为 null 时
    // 一般仍保持可用（内部把「没有命令」当作恒可执行），并没有「自动变禁用」这一步，
    // 所以 Noop 真正的收益是下面三条：
    // 1) 语义占位：读代码的人能一眼看出「这个按钮是有意不接行为的设计稿占位」，
    //    而不是怀疑「是不是忘了接线」——6 个按钮里只有「退出登录」接了真实命令。
    // 2) 统一入口：将来若要给所有按钮加埋点或防抖，只改 Noop 这一处就能全量生效，
    //    不必在每个点击处理里判空。
    // 3) 单例复用：static readonly 只初始化一次，所有 ButtonValue 实例共享同一个命令对象，
    //    而不是在属性初始化器里每实例 new 一个（属性初始化器对每个新对象都会执行一遍）。
    private static readonly ICommand Noop = new RelayCommand(() => { });

    public ICommand? Command { get; init; } = Noop;

    // IsNormal 是把「语气」翻译成「是否既非主、也非危险」的派生布尔。
    // 目前 XAML 三处画刷都直接用 Background / BorderBrush / TextBrush，没有绑定它；
    // 保留它的意义是：以后若要在样式层面区分三种语气（例如给普通按钮加 hover 效果），
    // 可以直接 Classes.ghost="{Binding IsNormal}"，不必再改 VM 加属性。
    // 它是计算属性且不参与任何通知：两个来源都是 init-only，构造后不会变。
    public bool IsNormal => !IsDanger && !IsPrimary;

    // 直接用画刷驱动外观，避免类名绑定在不同控件间行为不一致
    // 三个只读画刷由状态一次性算好，XAML 侧只做 {Binding}，不用写触发器或 Classes 组合
    // 语气是多维组合（普通/主/危险），靠 Classes 表达就要为每种组合各写一份样式，
    // 放在 VM 里用三元表达式算反而更短，也省掉一个 bool 到画刷的值转换器
    //
    // Background 三个分支（值都来自设计稿的按钮语气表）：
    //   分支 1 danger：把 #F8716E（Palette.RedColor 同色值）压到 17% 透明度当底。
    //          危险操作不用实心红，避免在深色面板上过于刺眼；语义主要靠红字 + 红边表达，
    //          底色只做一层「这里有点不对」的提示。
    //   分支 2 primary：从 #7A96FF 到 #5C7BF7 的渐变，与底部「完成」按钮的主按钮语言一致。
    //          Palette.Gradient 默认 horizontal=false，即自上而下；
    //          这里用嵌套三元而不是 switch，是为了让「首选分支」写在外层、阅读顺序与优先级一致。
    //   分支 3 normal：纯白 4% 作底，几乎看不出填充，靠 1px 浅描边界定边界，最不抢注意力。
    // 优先级：danger 高于 primary。两个开关同时为 true 时按危险处理——这是有意的，
    //        「注销账号」这类动作宁可显得危险，也不要显得像推荐的主操作。
    // 三个画刷属性都是只读计算属性，没有 setter：IsDanger / IsPrimary 是 init-only，
    // 构造完就不再变，所以画刷只需在首次绑定取值时算一次，运行期不会变、也无须发通知。
    public Avalonia.Media.IBrush Background => IsDanger
        ? Palette.Alpha("#F8716E", 0.17)
        : IsPrimary
            ? Palette.Gradient("#7A96FF", "#5C7BF7")
            : Palette.Alpha("#FFFFFF", 0.04);

    // BorderBrush 跟着同一套分支走，只是透明度更高：描边要比底色实，按钮轮廓才立得住。
    //   danger 45% 红边（与 17% 红底配对，形成「浅红块 + 明显红边」）；primary 14% 白边
    //   （渐变底上需要一点高光边，否则和深色面板的交界糊在一起）；normal 12% 白边（最弱）。
    //   注意 primary 的 14% 比 normal 的 12% 只高 2 个百分点：主按钮的辨识度交给渐变底，
    //   描边只做极轻微的提亮，避免出现「双层边框」的厚重感。
    public Avalonia.Media.IBrush BorderBrush => IsDanger
        ? Palette.Alpha("#F8716E", 0.45)
        : IsPrimary
            ? Palette.Alpha("#FFFFFF", 0.14)
            : Palette.Alpha("#FFFFFF", 0.12);

    // TextBrush 是三个分支里唯一「用文字颜色承担语义」的一处：
    //   danger 用 #FF8B88（比底色 #F8716E 更亮一档），因为文字压在 17% 的红底上，
    //   若直接用原色会发闷、对比度不够；调亮后小字号也读得清。
    //   primary 直接用 Avalonia.Media.Brushes.White，与渐变底形成最强对比，
    //   故意不走 Palette——纯白是唯一一个不需要跟设计稿色板联动的值。
    //   normal 用 #9BA3B7（次级文字色），保持「普通操作不该抢眼」。
    // 三者在 XAML 里都绑到 Button.Foreground，所以按钮上的文字颜色也完全由 VM 决定。
    public Avalonia.Media.IBrush TextBrush => IsDanger
        ? Palette.Solid("#FF8B88")
        : IsPrimary
            ? Avalonia.Media.Brushes.White
            : Palette.Solid("#9BA3B7");
}

/// <summary>只读信息：渲染成等宽字体的 <c>TextBlock</c>，用于版本号、运行时这类不可编辑的值。</summary>
// 影响哪几行：只有「关于 → 运行环境」四行——界面框架、运行时、渲染后端、数据存储。
// 之所以用等宽字体：这些值里混着数字和小数点（如 11.3.22、8.0.0），比例字体下很难逐字符对齐。
// Text 是 init-only 且类型上没有通知：值在构造时算好写死，界面只读一次。
public sealed class InfoValue : SettingValue
{
    public string Text { get; init; } = string.Empty;
}

/// <summary>路径展示：渲染成只读 <c>TextBox</c>（方便复制）加一个「打开」按钮。</summary>
// 影响哪几行：只有「存储与备份 → 位置 → 数据目录」这一行（Text 由 BuildStorage 现拼）。
// 用只读 TextBox 而不是 TextBlock：TextBox 里可以拖选、Ctrl+C 复制长路径，
// TextBlock 做不到这一点；代价是它自带内边距与最小高度，行高会比别的行略高。
// 右侧「打开」按钮在 XAML 里没有绑 Command，属于纯占位。
public sealed class PathValue : SettingValue
{
    public string Text { get; init; } = string.Empty;
}

/// <summary>单个色块：渲染成 Classes="swatch" 的圆形色按钮，Command 负责互斥选中。</summary>
// 影响哪个位置：通用分区「强调色」那一行的 6 个色块之一。
// Brush 是 init-only 的实色画刷，XAML 直接把按钮背景绑到它，所以颜色只在这里定义一次。
// Command 声明为 null! 而不是可空：这只是对编译器说「我保证调用处会传」，运行期并不真的兜底。
// 若忘传，Button.Command 就是 null，点了毫无反应也不报错 —— 所以 BuildGeneral 里
// Command = SwatchCommand 那一行是不能省的接线，改色板逻辑时要留意它还在。
// IsSelected 来自 SelectableItem，由 SwatchCommand 统一重算，XAML 用 Classes.selected 消费它。
public sealed class SwatchItem : SelectableItem
{
    public IBrush Brush { get; init; } = Palette.Solid("#6C8BFF");
    public ICommand Command { get; init; } = null!;
}

/// <summary>色板：渲染成横向排列的一排色块，用于强调色选择。</summary>
// 影响哪一行：只有「通用 → 外观 → 强调色」。Swatches 里的 6 个 SwatchItem 同时被登记到
// SettingsViewModel._allSwatches，互斥选中逻辑在构造函数那个 SwatchCommand 里统一处理，
// 而不是写在每个色块自己的命令上 —— 因为「取消其他 5 个」这件事必须由持有全量列表的一方来做。
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
// 本类不继承 ObservableObject：所有属性都是 init-only，构造完就是只读数据，
// 运行期没有任何一行会变，所以不需要 INotifyPropertyChanged —— 也就没有「刷新」的负担。
// HasDesc 用的是 !string.IsNullOrEmpty 而不是 Desc is not null：
// 因为描述传空字符串（""）时同样不该占位，Null 判断会漏掉这种情况。
// HasValue 用 `Value is not null` 模式匹配：Value 是 object?，这样写既判空又不必装箱或强转。
public sealed class SettingRow
{
    public string Title { get; init; } = string.Empty;
    public string? Desc { get; init; }
    public bool HasDesc => !string.IsNullOrEmpty(Desc);
    public object? Value { get; init; }
    public bool HasValue => Value is not null;
}

/// <summary>一组设置行：渲染成「小标题 + 圆角卡片」，卡片内的行之间用 1px 分隔线连接。</summary>
// 层级定位：SettingSection.Groups 的元素类型，对应 XAML 里「小标题 + Border.groupCard」那份模板。
// Rows 是 ObservableCollection：构造期逐个 Add，界面靠集合变更事件逐行展开；
// 因为是 Add 而不是整体替换实例，ItemsControl 的绑定始终指着同一个集合，不会掉绑定。
// 同样不需要通知属性：Title 是 init-only，组的生命周期内不变。
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
// 全部是演示占位：本应用没有账号体系，也没有「管理员 / 加入时间」这些真实资料。
// 以前写死「陈墨 / chenmo@aster.io / 2024 年 3 月加入」，会被当成真实的用户档案。
public sealed class AccountCard
{
    public string Initial { get; init; } = "演";
    public string Name { get; init; } = "演示账号";
    public string Role { get; init; } = "Demo";
    public string Email { get; init; } = "demo@example.com";
    public string Joined { get; init; } = "本地演示环境 · 未接后端";
}

/// <summary>关于卡片：应用名、版本与「已是最新」提示，位于「关于」分区顶部。</summary>
public sealed class AboutCard
{
    public string Name { get; } = "Aster";
    // 与 csproj 的 1.0.0.0 对齐并显式标注 Demo：以前写「v1.4.2 / 已是最新 · 2026-10-12 发布」
    // 会让人以为这是一个有正式发版节奏的产品。
    public string Version { get; } = "v1.0.0 · Demo";
    public string Meta { get; } = "本地演示构建 · 无后端 · 数据仅存本机";
}

/// <summary>
/// 一个设置分区：左栏一项 + 右栏一整页内容。继承 SelectableItem 拿到 IsSelected，
/// 左栏按钮用 Classes.selected 绑定它来实现高亮；Command 里带 CommandParameter=自身，
/// 因此各分区的 Command 可以共用同一个 SelectCommand。
/// TopCard / HasTopCard 用于可选的顶部卡片（账号卡、关于卡），Links / HasLinks 用于可选资源区。
/// </summary>
// 注意同一个对象会被渲染两次，用两份不同的模板：
//   左栏：Sections 的元素 → 显式 ItemTemplate（一行按钮，只用 Icon / Title / Command / IsSelected）；
//   右栏：Current 的值     → 隐式模板（整页内容，用 Title / Subtitle / TopCard / Groups / Links）。
// 显式 ItemTemplate 的优先级高于隐式模板，所以左栏不会意外渲染成整页 —— 这是链路上最容易误判的一点。
// Command 由各 BuildXxx 设为同一个 SelectCommand 实例；CommandParameter 由 XAML 传 `{Binding}`（自身），
// 于是「哪个分区被点」这个信息完全靠参数携带，不必给每个分区各建一个命令。
// 继承来的 IsSelected 只服务左栏按钮的高亮；右栏不读它，右栏只看 Current 指向谁。
// HasLinks 用 Count > 0 而不是 is not null：Links 集合永远非空实例，只有空/非空之分。
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
/// 设置浮层的视图模型：构造 8 个分区、维护当前选中项、提供左栏底部文案。
/// 构造函数入参是外部传入的「退出登录」命令，见字段 _logoutCommand 的注释。
/// </summary>
// 生命周期：MainWindowViewModel 在构造时 new 一次（Settings 属性是只读的），此后整个进程复用同一个实例。
// 也就是说浮层关掉再打开时，本类里的值（输入框文字、开关状态、选中的分区）都还在——
// 这是「所有更改即时生效并自动保存」这句文案的实际含义：状态活在内存里，不随浮层销毁。
// 代价：没有持久化层，进程重启后所有改动回到构造时的初值。
//
// 对外暴露的三样东西，各自的使用者：
//   Sections（左栏数据源）→ SettingsOverlay.axaml 左栏的 ItemsControl；
//   Current（右栏数据源） → 同上右栏的 ContentControl；
//   Footer（左栏底部小字）→ 同上，绑定路径 Settings.Footer。
// 本类不读 IsSettingsOpen、不知道浮层是否可见，显隐完全由 MainWindowViewModel 控制。
public sealed class SettingsViewModel : ObservableObject
{
    // 当前分区。字段初值只能是 null，但构造函数末尾就 Select(Sections[0]) 赋成了第一项，
    // 而界面（DataContext）是在构造返回之后才绑上来的，所以右栏的 ContentControl 从来不会真的读到 null；
    // 这个 null 只存在于「字段已声明、构造函数还没跑完」这一瞬间，属于语言层面的必然，不是运行时状态。
    // 私有 setter 是刻意的：全类只有 Select 能改它，保证「左栏高亮」与「右栏内容」不会各改各的。
    private SettingSection? _current;

    // 退出登录属于应用级流程（要切回登录页、清空会话），本类不该自己实现，
    // 因此由 MainWindowViewModel 在构造时注入 LogoutCommand，这里只负责挂到按钮上。
    private readonly ICommand? _logoutCommand;

    // 谁是调用者：MainWindowViewModel 的构造函数（Settings = new SettingsViewModel(LogoutCommand)），
    //   全进程只执行一次。
    // 前置状态：无。本类不依赖任何外部状态，唯一入参可以是 null。
    // 执行完的状态：Sections 里已有 8 个分区（顺序即左栏从上到下的顺序）、
    //   Current 指向第 1 个分区、Sections[0].IsSelected 为 true 其余为 false。
    // 谁会被通知：构造期还没绑任何界面，所有 PropertyChanged 都没有订阅者，等于空发；
    //   界面是在之后才通过 DataContext 拿到这个实例的，第一次读到的就是已经配好的最终状态。
    //
    // 步骤 1：保存注入的退出登录命令（可为 null，见字段注释）。
    // 步骤 2：建 SelectCommand。用带参重载而不是无参：分区对象要作为参数传进来。
    //         `o is SettingSection s` 这层模式匹配同时做了判空与转型，
    //         参数不是 SettingSection（理论上不会发生）时静默忽略，不抛异常。
    // 步骤 3：建 SwatchCommand，做色块互斥。
    // 步骤 4：按左栏顺序依次 Build 八个分区。顺序即业务优先级：账号 → 通用 → 看板 → 成员 → 通知 → AI 模型 → 存储 → 关于，
    //         常用项在前、信息项在最后，与设计稿一致。每次 Build 都新建独立对象，分区之间不共享行或组。
    // 步骤 5：逐个 Add 进 Sections。用 Add 而不是构造新集合：Sections 是只读属性且已初始化，
    //         Add 会触发集合变更事件；此时即使已有绑定也能逐项刷新。
    // 步骤 6：默认选中第一项，避免浮层打开时右栏空白。这一步同时把 Sections[0].IsSelected 置 true。
    public SettingsViewModel(ICommand? logoutCommand = null)
    {
        // 步骤 1：存下注入的命令。存成字段而不是直接用参数，是因为它要到步骤 4 的
        // BuildAccount 里才会被用到，那时构造函数的参数还在作用域内，但字段语义更清楚。
        _logoutCommand = logoutCommand;

        // 分区切换的唯一入口：参数是左栏按钮传来的 SettingSection（CommandParameter）
        SelectCommand = new RelayCommand(o =>
        {
            if (o is SettingSection s) Select(s);
        });

        // 色板是单选：把所有色块的 IsSelected 重算一遍，只保留被点中的那个。
        // 即使点到已选中的色块也不会取消选中，避免出现「一个都不选」的无效状态。
        // 步骤 3：色板单选命令。注意它构造时 _allSwatches 还是空的——
        // 色块是在步骤 4 的 BuildGeneral 里才被塞进去的，而命令体只在实际点击时才遍历列表，
        // 那时列表早已填满，所以这个「先建命令、后填数据」的顺序是安全的（延迟求值）。
        SwatchCommand = new RelayCommand(o =>
        {
            // 参数必须是被点中的那个色块；`is not SwatchItem` 把判空和类型不符一并挡掉，
            // 提前 return 而不是抛异常：命令参数出错不该让整个应用崩掉。
            if (o is not SwatchItem picked) return;
            // 遍历全量色块，用引用相等决定谁亮：命中自己的置 true，其余一律 false。
            // 这样「取消其他所有」与「点亮自己」是同一句话，不会出现两个都亮的中间态。
            // IsSelected 是带 Set 的属性，值没变的那些不发通知，只有一升一降两次真正刷新。
            foreach (var s in _allSwatches) s.IsSelected = ReferenceEquals(s, picked);
        });

        // 分区顺序即左栏顺序；每个 BuildXxx 独立构造成一组行，彼此不共享状态
        // 步骤 4：八个分区在这里一次性造好。数组的先后就是左栏从上到下的顺序；
        // 每个 BuildXxx 内部自己决定卡片数与行数，本构造函数不关心细节。
        var sections = new[]
        {
            BuildAccount(),
            BuildGeneral(),
            BuildBoard(),
            BuildMembers(),
            BuildNotify(),
            BuildAi(),
            BuildStorage(),
            BuildAbout()
        };

        // 步骤 5：逐项加入 ObservableCollection。集合变更事件会让任何已绑定的 ItemsControl
        // 自动补上这一项；此处发生在界面挂载之前，所以实际上等价于「一次填好」。
        foreach (var s in sections) Sections.Add(s);
        // 默认选中第一个分区，保证浮层一打开右栏就有内容，不会出现空白页
        // 步骤 6：直接取 Sections[0]（此时刚 Add 完，Count 必为 7，不会越界）。
        // Select 内部会顺手把它的 IsSelected 置 true，所以左栏第一项也是预先高亮的。
        Select(Sections[0]);
    }

    public ObservableCollection<SettingSection> Sections { get; } = new();
    public ICommand SelectCommand { get; }
    // Sections / SelectCommand / SwatchCommand 三者的可写性各不相同，原因也不同：
    //   Sections 只有 getter（集合本身可变）：界面绑的是这个集合实例，换实例等于断绑定，
    //     所以只允许在里面 Add，不允许整体替换。
    //   SelectCommand 只有 getter：构造函数里直接赋值一次，之后永远指向同一个 RelayCommand；
    //     它没有状态，不需要重建。
    //   SwatchCommand 留了 private set：它同样只在构造函数里赋值一次。其实写成纯 get 也能编译
    //     （自动属性允许在声明处初始化、再在构造函数里赋值），这个 set 是多余的，
    //     与 SelectCommand 的写法不统一，属于可清理的小瑕疵，不影响运行期行为。
    public ICommand SwatchCommand { get; private set; } = null!;

    // 当前分区：setter 私有，只能经由 Select 修改，确保左栏高亮与右栏内容永远同步
    // 谁在用它：右栏的 <ContentControl Content="{Binding Settings.Current}"/>（隐式匹配 SettingSection 模板）。
    // 这里的 Set 返回值被忽略是有意的，而且这个「忽略」恰好带来了一个好处：
    //   重复点同一个分区时新值与旧值引用相等，Set 返回 false、不发通知，右栏不会重建，
    //   用户正看到一半的滚动位置因此得以保留；若每次都强发通知，ContentControl 会重建整棵子树，
    //   滚动条会跳回顶部，看起来像「点了没反应却莫名其妙跳了一下」。
    // 通知方向：本类 → 视图，单向。setter 私有保证不存在「右栏显示 A、左栏高亮 B」的中间态。
    public SettingSection? Current
    {
        get => _current;
        private set => Set(ref _current, value);
    }

    /// <summary>左栏底部那行小字，XAML 里绑定 Settings.Footer。</summary>
    // 纯计算属性：每次读都重新拼一次字符串，不缓存、不发通知（没有 Set，也就没有 PropertyChanged）。
    // 不做成字段的理由：内容是常量，拼一行的开销可以忽略，少一个字段少一处可能过期的地方。
    // 注意这里的 v1.4.2 与 BuildAbout 里 AboutCard.Version 的 "v1.4.2"、以及
    // Avalonia 版本兜底值 "11.3.22" 是三处各自独立的字符串，改版本号时必须同步（见文末说明）。
    // 文案里的「设置已同步至本地」是承诺不是事实：本类没有任何持久化代码，见类注释里的生命周期说明。
    public string Footer => "Aster Demo · 设置仅本次运行有效";

    // 全量色块索引：色板只在「通用」分区创建，但单选逻辑写在构造函数里，
    // 用一个列表把它们收集起来，避免命令里再去遍历界面树。
    // 为什么是实例字段而不是 static：色块属于当前这个 VM 实例，若做成 static，
    // 第二个实例会看到上一份实例留下的色块，点击时就会去改已经不在界面上的对象，
    // 表现为「点了没反应」——这类 bug 极难排查，所以宁可多存一个实例字段。
    // 初始化时机：字段初始化器先于构造函数体执行，所以构造函数里 new SwatchCommand 时
    // 这个列表已经是可用（空）状态，命令体真正遍历它则要等到用户点击时，那时早已填满。
    private readonly List<SwatchItem> _allSwatches = new();

    /// <summary>
    /// 切换到指定分区：先把所有分区的 IsSelected 复位成「是否等于目标」，再更新 Current。
    /// 先改选中态再改 Current，可让右栏内容与左栏高亮在同一帧内一起变化。
    /// </summary>
    // 调用者只有两处：构造函数末尾（默认选中第 1 项）和 SelectCommand 的回调（用户点左栏）。
    // 前提：section 必须就是 Sections 里的某个元素。XAML 用 CommandParameter="{Binding}" 传的
    //   正是被点的那个元素，所以引用必然相等；若传入一个「字段相同但不在集合里」的新对象，
    //   结果是左栏全部熄灭、右栏却显示那个陌生分区 —— 目前没有校验，靠调用方自觉。
    // 做完之后：恰好一个分区的 IsSelected 为 true，Current 指向它。
    // 谁被通知：每个 IsSelected 的变化推给左栏按钮的 Classes.selected（样式重匹配）；
    //   Current 的变化推给右栏 ContentControl（换内容、重新挑模板）。
    //   两者都在同一次布局周期里完成，所以视觉上是「高亮和内容一起跳」，不会先亮后换。
    private void Select(SettingSection section)
    {
        // 步骤 1：把 8 个分区的选中态全部重算一遍，只有等于 section 的那个留下 true。
        // 用 ReferenceEquals 而不是比较 Key 字符串：分区对象构造后从不复制，
        // 引用相等就是「是不是同一个」的准确语义，也免了 Key 拼错导致的静默失效。
        // 刻意不用 ==，是为了避免将来给 SettingSection 重载相等运算符时语义被悄悄改掉。
        // 开销：只有一升一降两次赋值真正发通知，其余 5 个 Set 返回 false，等于没动。
        foreach (var s in Sections) s.IsSelected = ReferenceEquals(s, section);
        // 步骤 2：换右栏内容。setter 私有，全类只有这里能写，
        // 所以「左栏高亮」与「右栏内容」不可能分别从两条路径被改乱。
        Current = section;
    }

    /// <summary>
    /// 构造一个分组。title 是分组小标题，rows 用 params 展开，调用处可以一行写完一个分组；
    /// 返回的 SettingGroup 由调用方决定挂到哪个分区（有的分区需要先建组再逐行 Add）。
    /// </summary>
    // 调用者：本文件的 7 个 BuildXxx（BuildGeneral / BuildStorage 里还会「先建空组再逐行 Add」）。
    // 参数约定：title 是卡片上方那行 10.5 号小标题，允许空字符串（会渲染成一段空白标题）；
    //   rows 用 params 展开，允许 0 个——0 个就是一张只有边框没有行的空卡片，目前没人这么用。
    // 返回值：Rows 已按传入顺序填好的 SettingGroup。
    // 为什么是 static：不读任何实例字段，纯数据装配，static 能避免误用 this。
    // 为什么保留 params 而不是只收 IEnumerable：调用处可以把一个普通分组写成一行
    //   Group("安全", Row(...), Row(...))；需要复用局部变量时又能退回 Add 逐个加，两种风格都顺手。
    // 集合刷新：Set 的是 Title（init），Rows 用 Add 填充，界面靠集合变更事件刷新，不需要属性通知。
    private static SettingGroup Group(string title, params SettingRow[] rows)
    {
        // 步骤 1：先建空组，Title 走对象初始化器（init-only，只能在这里赋）。
        var g = new SettingGroup { Title = title };
        // 步骤 2：按参数顺序逐行加入。顺序即界面上从上到下的顺序。
        foreach (var r in rows) g.Rows.Add(r);
        // 步骤 3：返回给调用方，由它决定挂到哪个分区（本方法不知道分区的存在）。
        return g;
    }

    /// <summary>
    /// 构造一行：title 必填，desc 传 null 表示这一行不显示描述，value 传 null 表示右侧留空。
    /// 三个参数都走对象初始化器，所以调用处能写成一目了然的单行声明。
    /// </summary>
    // 调用者：几乎所有 Group(...) 的实参位置，本文件里出现几十次。
    // 参数约定（三条都是「照填即可」，没有隐含换算）：
    //   title：必填，界面左列的主文案；
    //   desc ：可为 null，null 表示这行不显示描述。关于页的「运行环境」四行就传 null，
    //          行高会比有描述的行矮一截（少了一行 11.5 号文字 + 2px 上边距）；
    //   value：可为 null，null 表示右侧留空。此时 XAML 里 ContentControl 的
    //          IsVisible 绑 HasValue 得到 false，那个格子完全退出布局。
    // 返回值：只做数据搬运的 SettingRow，没有副作用、不做缓存、也不校验。
    // 表达式体写法（=> new {...}）等价于「建对象 → 返回」，选择它是为了少一层大括号，
    // 让调用处一个分组能在一屏内读完。
    private static SettingRow Row(string title, string? desc, object? value)
        => new() { Title = title, Desc = desc, Value = value };

    /// <summary>
    /// 构造一个下拉框：options 为候选项，默认选中第一项（没有任何选项时退化为空字符串）。
    /// 方法名 Select 与类型名 SelectValue 能共存，是因为它们是两个不同标识符；
    /// 静态调用 Select(...) 也不会和 LINQ 的 Enumerable.Select 扩展方法冲突——
    /// 扩展方法只出现在实例的链式调用上，静态按名字解析时优先命中本类方法。
    /// </summary>
    // 调用者：需要下拉框的行，共 17 处（例如 Select("访客", "编辑", "管理员")）。
    // 参数约定：变长字符串，顺序即下拉展开后的顺序，第一个即默认值。
    // 返回值：Options 已填满、Selected 已指向第一项的 SelectValue，Width 取类型上的默认 158。
    // 为什么默认选第一项：
    //   1) 空 Selected 在 ComboBox 里表现为一个空框，视觉上像「加载失败」或「还没选」，
    //      而这些设置业务上都有合理默认值（界面语言默认简体中文、角色默认访客）；
    //   2) 让每个调用处不必再写一遍 Selected，减少遗漏。
    //   注意这一行副作用：Selected 在此时就发了一次 PropertyChanged，但没有任何订阅者（还没绑定），
    //   所以 ComboBox 首次绑定时读到的就是已经定好的值。
    private static SelectValue Select(params string[] options)
    {
        // 步骤 1：建空的 SelectValue（Options 是只读集合属性，只能在实例上 Add）。
        var v = new SelectValue();
        // 步骤 2：按参数顺序加进 Options。ComboBox 下拉列表的顺序就是这个顺序。
        foreach (var o in options) v.Options.Add(o);
        // 步骤 3：取第一项作为默认值。options 为空时 FirstOrDefault() 返回 null，
        //         用 ?? string.Empty 兜住，避免 Selected 变成 null（ComboBox 会显示成未选择）。
        v.Selected = options.FirstOrDefault() ?? string.Empty;
        return v;
    }

    /// <summary>
    /// 构造一组标签块：labels 为文案，前 onCount 个默认选中，其余默认关闭。
    /// 用「前 N 个选中」而不是逐个传 bool，是因为这里的多选只是演示性的默认值，写法越短越好；
    /// 方法名 Chips 与类型名 ChipsValue 同理不会冲突，二者是不同的标识符。
    /// </summary>
    // 调用者只有两处：BuildGeneral 的 Chips(1, "深色", "浅色", "跟随系统") 与
    //   BuildBoard 的 Chips(4, "活跃用户", "订单总量", "总营收", "转化率")。
    // 参数约定：onCount = 前几个默认点亮；labels = 标签文案，顺序即显示顺序。
    //   两个参数顺序刻意把数字放前面：调用处读起来像「点亮 1 个：深色/浅色/跟随系统」。
    // 返回值：Items 已填好的 ChipsValue。
    // 边界：onCount 大于标签数不会越界（i < onCount 只会让所有项都点亮）；
    //   onCount 为 0 或负数则全部熄灭；负数和 0 没有区别，不需要额外校验。
    // 隐患：本方法没有任何互斥逻辑，Chips(1, ...) 造出来的「主题」其实可以同时点亮多个，
    //   与「三选一」的语义不符（见文末说明）。
    private static ChipsValue Chips(int onCount, params string[] labels)
    {
        // 步骤 1：建空的 ChipsValue（Items 是只读集合属性）。
        var v = new ChipsValue();
        // 步骤 2：逐项造 ChipItem。i < onCount 这个布尔直接写进 IsSelected，
        //         即「位置决定默认选中态」，避免为每个标签多传一个 bool 参数。
        //         这里没有大括号：循环体只有 Add 一句，写成单行是为了让「一段标签」看起来是一行数据。
        for (var i = 0; i < labels.Length; i++)
            v.Items.Add(new ChipItem { Label = labels[i], IsSelected = i < onCount });
        return v;
    }

    /// <summary>
    /// 账号分区：顶部 AccountCard 资料卡，下面分「基本信息（昵称/邮箱/手机/部门）、
    /// 安全（改密码/两步验证/登录设备）、会话（退出登录/注销账号）」三组。
    /// 其中「退出登录」按钮的命令来自构造函数注入，其余按钮只挂默认空命令。
    /// </summary>
    // 调用者：构造函数步骤 4，进程内只执行一次。
    // 界面映射：左栏第 1 项「账号」；右栏 = 顶部一张账号资料卡 + 3 张分组卡片，共 9 行：
    //   卡片 1「基本信息」4 行（显示名称 / 邮箱 / 手机号 / 所属部门）
    //   卡片 2「安全」    3 行（登录密码 / 两步验证 / 登录设备）
    //   卡片 3「会话」    2 行（退出当前账号 / 注销账号）
    // 前置：无。本方法不读任何实例状态，唯一的外部依赖是字段 _logoutCommand（可能为 null）。
    // 后置：返回一个分区对象；此时它还没挂进 Sections，IsSelected 仍是默认的 false。
    // 通知：本方法里没有任何属性通知会对外生效（对象尚未被界面看到）。
    private SettingSection BuildAccount()
    {
        // 步骤 1：建分区壳。Key 目前只起标识作用（Select 用的是引用比较，不读 Key），
        //         Icon 取自 Controls.Icons 的图标路径常量，Command 指向共享的 SelectCommand。
        var s = new SettingSection
        {
            Key = "account", Title = "账号", Icon = Controls.Icons.User,
            Subtitle = "个人信息、安全设置与登录会话",
            Command = SelectCommand,
            // 顶部资料卡：右栏会把 TopCard 交给一个 ContentControl，靠类型匹配到「账号卡」模板
            TopCard = new AccountCard()
        };
        // 步骤 2：「基本信息」卡片。4 行里有 3 个 TextValue 和 1 个 SelectValue，
        //         这正是数据驱动表单的好处：同一张卡片里可以混装不同类型，行模板不必区分。
        s.Groups.Add(Group("基本信息",
            Row("显示名称", "演示占位资料，不会上传到任何服务器", new TextValue { Text = "演示账号" }),
            Row("邮箱", "仅作展示，本应用不会发送邮件", new TextValue { Text = "demo@example.com" }),
            // 手机号未绑定：Text 留空、只给 Placeholder，输入框的水印就充当了「未绑定」提示
            Row("手机号", "演示环境未接入短信服务", new TextValue { Placeholder = "未绑定" }),
            Row("所属部门", "演示用选项，不影响任何权限", Select("演示部门", "产品部", "运营部"))));
        // 步骤 3：「安全」卡片。注意「登录密码」这一行的右侧是一个按钮而不是输入框——
        //         密码不回显、也不能就地改，只能跳到另一个流程，所以这里只放一个动作入口。
        s.Groups.Add(Group("安全",
            Row("登录密码", "演示登录：任意非空账号密码即可进入，没有后端校验", new ButtonValue { Text = "修改密码" }),
            Row("两步验证", "演示开关，不会真的发送验证码", new ToggleValue { IsOn = true }),
            Row("登录设备", "演示环境 · 未接后端", new ButtonValue { Text = "管理设备" })));
        // 步骤 4：「会话」卡片，两个危险操作放在最后一张卡片里，与上方的常规设置隔开。
        //         两行都是 danger 语气，但只有第一行真的接了命令，第二行是设计稿占位——
        //         这种「同样长相、一个能点一个不能」的差异在界面上看不出来，属于已知的演示取舍。
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
    // 调用者：构造函数步骤 4，进程内只执行一次。
    // 界面映射：左栏第 2 项「通用」；无顶部卡；3 张分组卡片，共 8 行：
    //   卡片 1「外观」       3 行（主题 chips / 强调色色板 / 界面缩放滑块）
    //   卡片 2「区域与语言」 3 行（界面语言 / 日期与时间格式 / 货币单位，全是下拉）
    //   卡片 3「启动与窗口」 2 行（恢复上次页面 / 最小化到托盘，全是开关）
    // 这一页是控件类型的「全家福」：ChipsValue、PaletteValue、RangeValue、SelectValue、ToggleValue
    // 各出现至少一次，调样式时拿它对照最方便。
    // 前置：无。副作用只有一处——把 6 个色块登记进 _allSwatches（供 SwatchCommand 做互斥）。
    private SettingSection BuildGeneral()
    {
        // 步骤 1：主题标签，只点亮第 1 个（深色）。注意它没有互斥逻辑，
        //         用户可以把三个都点亮，见 Chips 方法注释与文末说明。
        // 主题是三选一的 chips：只在「深色」上给选中态，其余默认关闭
        var theme = Chips(1, "深色", "浅色", "跟随系统");

        // 步骤 2：强调色色板。6 个色值与 Items.cs 里 Palette 的语义色一一对应
        //         （品牌蓝 / 紫 / 青 / 绿 / 琥珀 / 红），顺序即色块从左到右的顺序；
        //         这里直接用字面量而不是引用 Palette.AccentColor，是为了让色板顺序一眼可读，
        //         代价是色值在这两处各写了一遍。
        // 强调色用色板而非下拉，6 个色值来自 Palette 里的设计稿色
        var palette = new PaletteValue();
        var colors = new[] { "#6C8BFF", "#A78BFA", "#3DD6E8", "#3DDC97", "#F5B942", "#F8716E" };
        foreach (var c in colors)
            palette.Swatches.Add(new SwatchItem { Brush = Palette.Solid(c), Command = SwatchCommand });
        // 注册到全量索引，供 SwatchCommand 做互斥选择；默认点亮第一个
        // 这一步是色块互斥的关键：单个色块并不知道「同组其他色块」是谁，
        // 只有这个列表知道；命令体遍历它，才能做到「点亮一个、熄灭其余」。
        // 每个色块的 Command 都是同一个 SwatchCommand 实例，靠 XAML 里的
        // CommandParameter="{Binding}" 把「我是哪一个」传回命令。
        foreach (var sw in palette.Swatches) _allSwatches.Add(sw);
        // 默认点亮第一个（品牌蓝）。直接写 [0] 而不是查找，因为上面刚按顺序填完，位置确定。
        // 这里有意不走 SwatchCommand：直接赋值更直白，也省掉命令里的类型判断分支。
        palette.Swatches[0].IsSelected = true;

        // 步骤 3：建分区壳。TopCard 不赋值（保持 null），于是右栏 HasTopCard 为 false，
        //         顶部卡片那个 ContentControl 完全不占位，页面直接从「外观」卡片开始。
        //         这与账号页/关于页形成对照：那两个分区靠 TopCard 多出一块资料卡。
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
    // 调用者：构造函数步骤 4，进程内只执行一次。
    // 界面映射：左栏第 3 项「数据看板」；无顶部卡；3 张分组卡片，共 9 行：
    //   卡片 1「刷新与范围」   3 行（数据刷新频率 / 默认时间范围 / 后台持续刷新）
    //   卡片 2「指标卡」       3 行（显示的指标 chips / 数值缩写 / 显示环比）
    //   卡片 3「图表与表格」   3 行（趋势图类型 / 每页行数 / 导出格式）
    // 内容取向：这一页改的是「管理页怎么呈现数据」，不是数据本身，所以全是展示层偏好。
    // 前置：无。后置：返回未挂载的分区对象，无任何外部副作用。
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
    // 调用者：构造函数步骤 4，进程内只执行一次。
    // 界面映射：左栏第 4 项「成员与权限」；无顶部卡；3 张分组卡片，共 8 行：
    //   卡片 1「权限」       3 行（新成员默认角色 / 允许成员邀请他人 / 邀请链接有效期）
    //   卡片 2「登录与安全」 3 行（会话超时 / 强制双因素 / 异地登录提醒）
    //   卡片 3「审计」       2 行（记录操作日志 / 日志保留时长）
    // 内容取向：与「账号」页的区别在于——账号页改的是「我自己」，这里改的是「整个团队的规则」。
    // 前置：无。后置：返回未挂载的分区对象，无任何外部副作用。
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
    // 调用者：构造函数步骤 4，进程内只执行一次。
    // 界面映射：左栏第 5 项「通知」；无顶部卡；2 张分组卡片，共 7 行：
    //   卡片 1「推送方式」 3 行（邮件通知 / 桌面通知 / 声音提醒，全是开关）
    //   卡片 2「内容订阅」 4 行（每日数据摘要 / 摘要发送时间 / 指标异常告警 / 告警触发阈值）
    // 这一页只有开关和下拉两种控件（5 个 ToggleValue + 2 个 SelectValue），结构最简单，
    // 适合当作新增分区的模板来复制。
    // 前置：无。后置：返回未挂载的分区对象，无任何外部副作用。
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
    // 调用者：构造函数步骤 4，进程内只执行一次。
    // 界面映射：左栏第 6 项「存储与备份」；无顶部卡；3 张分组卡片，共 7 行：
    //   卡片 1「位置」     2 行（数据目录 PathValue / 缓存占用 按钮「清理」）
    //   卡片 2「备份」     3 行（自动备份 / 备份频率 / 保留份数滑块）
    //   卡片 3「数据管理」 2 行（导出全部业务数据 主按钮 / 历史数据保留期）
    // 这一页同时出现 PathValue、ButtonValue、RangeValue、SelectValue、ToggleValue，
    // 且路径是运行时算出来的，所以它也常被当作「动态数据怎么进表单」的参考。
    // 前置：无。后置：返回未挂载的分区对象；另有一个局部变量 dataDir 被算出来后立刻用掉。
    private SettingSection BuildStorage()
    {
        // 步骤 1：取本地缓存真实所在目录。
        // 由 Services.LocalCache 统一决定（开发态是仓库内的 AppData/，发布态是用户漫游目录），
        // 这里不重复一遍路径规则 —— 界面显示的位置必须和程序真正写入的位置是同一个，
        // 否则用户按这个路径去翻文件会找不到。
        // 末尾的分隔符由 LocalCache.DirectoryPath 给（各平台都用 DirectorySeparatorChar 拼），
        // 显示的路径可直接粘贴进资源管理器。
        var dataDir = LocalCache.DirectoryPath;

        var s = new SettingSection
        {
            Key = "storage", Title = "存储与备份", Icon = Controls.Icons.Database,
            Subtitle = "数据目录、缓存清理与备份策略",
            Command = SelectCommand
        };

        // 同样先建组再逐行 Add：数据目录这一行要复用上面算出的 dataDir
        var location = Group("位置");
        location.Rows.Add(Row("数据目录", "本地缓存文件 local-cache.json 的存放位置",
            new PathValue { Text = dataDir }));
        // 「缓存占用」这一行现在是真的：描述文案是运行时按文件实际大小拼的，
        // 右侧「清理」按钮绑 LocalCache.Reset（删文件 → 立刻按默认结构重建）。
        // 已知瑕疵：清理后文案不会刷新（SettingRow.Desc 是 init-only、没有通知），
        // 要看到变化得重开设置浮层；动作本身是即时生效的。
        location.Rows.Add(Row("缓存占用", LocalCache.Describe(),
            new ButtonValue { Text = "清理", Command = new RelayCommand(LocalCache.Reset) }));
        // Agent 审计是独立文件（audit.ndjson），清理它不会动业务缓存。
        // 与「缓存占用」同样的已知瑕疵：清理后 desc 不会即时刷新，重开浮层才能看到新大小。
        var auditSize = AgentAudit.SizeBytes;
        location.Rows.Add(Row("Agent 审计日志",
            AgentAudit.Exists
                ? $"{auditSize / 1024.0:0.0} KB · 记录模型请求、工具调用与确认结果，仅存本机"
                : "尚未生成 · 首次使用 Agent 后自动创建",
            new ButtonValue { Text = "清理", Command = new RelayCommand(AgentAudit.Clear) }));
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
    /// AI 模型分区：接入 OpenAI 兼容的大模型服务（DeepSeek / Kimi / 通义兼容模式 / Ollama 等）。
    /// 三个输入行（API Key / Base URL / 模型名）+「保存配置」按钮，写入 LocalCache.Data.Ai；
    /// 这是本类少数真正持久化的地方 —— 与工作台对话功能共用 Services.LocalCache 这一份配置。
    /// </summary>
    // 调用者：构造函数步骤 4，进程内只执行一次；位置在「通知」与「存储与备份」之间。
    // 界面映射：左栏「AI 模型」；右栏 = 1 张分组卡片「接入配置」4 行（Key / URL / 模型 / 保存）
    //   + 1 张说明卡片「使用说明」2 行（纯文案，无右侧控件）。
    // 与其它分区的差别：其它分区的输入框只是摆设（内存态、不落盘），本分区「保存配置」
    // 是真写盘（LocalCache.Save 立即落盘），且 AdminViewModel.Send 每次发消息都现读
    // LocalCache.Data.Ai —— 所以保存即生效，不需要重启应用。
    // 前置：无。后置：返回未挂载的分区对象；三个 TextValue 的实例被闭包捕获给保存命令。
    private SettingSection BuildAi()
    {
        var s = new SettingSection
        {
            Key = "ai", Title = "AI 模型", Icon = Controls.Icons.Lightning,
            Subtitle = "接入 OpenAI 兼容的大模型服务（DeepSeek、Kimi、通义、Ollama）",
            Command = SelectCommand
        };

        // 步骤 1：读当前配置灌进输入框。cfg 在 Normalize 后必非 null（见 LocalCache.Normalize）。
        var cfg = LocalCache.Data.Ai;
        var keyBox = new TextValue { Text = cfg.ApiKey, Placeholder = "sk-…" };
        var urlBox = new TextValue { Text = cfg.BaseUrl };
        var modelBox = new TextValue { Text = cfg.Model };

        // 步骤 2：「接入配置」组。三个输入行 + 一个保存按钮。
        // TextValue 实例必须先存局部变量再进 Row：保存命令的闭包要读它们,
        // 写在 Row(...) 实参里就拿不到引用了。
        var connect = Group("接入配置");
        connect.Rows.Add(Row("API Key", "只保存在本机的 local-cache.json，不随代码入库、不上传", keyBox));
        connect.Rows.Add(Row("Base URL", "OpenAI 兼容端点根地址，如 https://api.deepseek.com", urlBox));
        connect.Rows.Add(Row("模型名", "如 deepseek-chat / deepseek-reasoner / qwen-plus", modelBox));
        connect.Rows.Add(Row("应用", "修改后点一次「保存配置」即生效，无需重启",
            new ButtonValue
            {
                Text = "保存配置",
                IsPrimary = true,
                Command = new RelayCommand(() => SaveAiConfig(keyBox, urlBox, modelBox))
            }));
        s.Groups.Add(connect);

        // 步骤 3：「使用说明」组，纯文案行（value 传 null，右侧留空）。
        s.Groups.Add(Group("使用说明",
            Row("对话入口", "工作台底部的指令输入框，Agent 可对话并直接操作工作区数据（新建客户 / 待办、审批等）", null),
            Row("未配置时", "对话会提示先在本分区填写 API Key，不会发出网络请求", null)));
        return s;
    }

    // 调用者：仅 BuildAi 里「保存配置」按钮的命令闭包。
    // 做完之后：三个输入框的值修剪后写回 LocalCache.Data.Ai 并立即落盘（Save 而非 SaveSoon：
    // 用户点「保存」就是要看到「已保存」的确定性，防抖的 700ms 在这里反而制造不确定）。
    // BaseUrl / Model 留空时回落默认值，避免存进一个肯定发不出请求的空串。
    private static void SaveAiConfig(TextValue keyBox, TextValue urlBox, TextValue modelBox)
    {
        var cfg = LocalCache.Data.Ai;
        cfg.ApiKey = keyBox.Text.Trim();
        cfg.BaseUrl = urlBox.Text.Trim().Length == 0 ? "https://api.deepseek.com" : urlBox.Text.Trim();
        cfg.Model = modelBox.Text.Trim().Length == 0 ? "deepseek-chat" : modelBox.Text.Trim();
        LocalCache.Save();
    }

    /// <summary>
    /// 关于分区：顶部 AboutCard，下面「运行环境」组展示框架/运行时/渲染后端/存储方式，
    /// 末尾 Links 追加 4 条资源链接（官网、更新日志、反馈、许可），由右栏单独渲染。
    /// </summary>
    // 调用者：构造函数步骤 4，进程内只执行一次。它是左栏最后一项，也是唯一带 Links 的分区。
    // 界面映射：左栏第 8 项「关于」；右栏 = 顶部 AboutCard（Logo + 应用名 + 版本 + 「已是最新」）
    //   + 1 张分组卡片「运行环境」4 行（界面框架 / 运行时 / 渲染后端 / 数据存储）
    //   + 1 个资源区（4 条 LinkItem：官网 / 更新日志 / 反馈 / 许可），共 4 行 + 4 条链接。
    // 前置：无。后置：返回未挂载的分区对象；两次读程序集/运行时元数据，没有别的副作用。
    // 注意 4 行的 desc 全传 null，所以这一页的行高比别的页紧凑（少了描述那一行文字）。
    private SettingSection BuildAbout()
    {
        // 版本号不写死：取 Avalonia.Controls 程序集的元数据版本，升级 NuGet 后自动跟着变；
        // ToString(3) 只保留 主版本.次版本.修订号，末端的 ?? 兜底防止某些构建拿不到 Version
        // 这一行的三段来源：
        //   1) typeof(Avalonia.Controls.Window)——借 Window 类型触发 Avalonia.Controls.dll 的加载，
        //      拿到的是「这个程序集」而不是当前应用的程序集；
        //   2) Assembly.GetName().Version——dll 里的 AssemblyVersion（NuGet 包版本 11.3.22.0 对应 "11.3.22"），
        //      类型是 Version?，所以要先 ?. 再 ToString(3)，只保留三段、丢掉第 4 段修订号；
        //   3) ?? "11.3.22"——AssemblyVersion 理论上总有值，这里兜的是单文件/裁剪等极端构建下
        //      拿不到元数据的情况。兜底值是硬编码的，升级包版本时它不会自动跟着变，需要手工同步。
        var avalonia = typeof(Avalonia.Controls.Window).Assembly.GetName().Version?.ToString(3) ?? "11.3.22";
        // .NET 运行时版本也只取主.次.修订三段，和界面里其它版本号的写法保持一致
        // runtime 的来源与上一行不同：Environment.Version 是「当前正在运行的 CLR 版本」，
        //   不是项目文件里的 TargetFramework（net8.0）也不是 SDK 版本号；
        //   它由运行时自己填，非空，所以不需要 ?? 兜底。
        //   在 .NET 8 上通常形如 "8.0.0"，ToString(3) 同样是取三段。
        var runtime = Environment.Version.ToString(3);

        var s = new SettingSection
        {
            Key = "about", Title = "关于", Icon = Controls.Icons.Info,
            Subtitle = "版本信息与相关资源",
            Command = SelectCommand,
            // 顶部关于卡：右栏的 ContentControl 靠类型匹配到 AboutCard 模板
            TopCard = new AboutCard()
        };

        // 项目定位单独一行放在最前：演示时打开设置就能确认「这不是生产系统」。
        s.Groups.Add(Group("项目定位",
            Row("应用性质", "单机 Demo 演示应用，不含真实业务数据与后端服务",
                new InfoValue { Text = "Demo · 仅本机" })));

        // 这几行 desc 传 null：运行环境本身没有解释性文案，行高会更紧凑
        s.Groups.Add(Group("运行环境",
            Row("界面框架", null, new InfoValue { Text = $"Avalonia {avalonia}" }),
            // 「运行时」这一行的三段来源：
            //   ".NET " 是字面量前缀；runtime 是上面算出的 CLR 三段版本；
            //   最后一段 RuntimeInformation.RuntimeIdentifier 是运行时标识（RID），
            //   形如 win-x64 / linux-x64 / osx-arm64，由运行时启动时按当前平台填写，
            //   发布时若指定了 -r 参数就沿用那个值。它没有兜底：极端环境下若为空字符串，
            //   这一行会显示成 ".NET 8.0.0 · "（尾部多一个分隔符），属可接受的小瑕疵。
            Row("运行时", null, new InfoValue { Text = $".NET {runtime} · {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}" }),
            // 「Skia」是 Avalonia 11 在各平台的默认渲染后端，这里写死而不是查询运行时实际后端；
            // 若将来启用其它后端（如某些平台的 GPU 后端），这一行会说谎。
            Row("渲染后端", null, new InfoValue { Text = "Skia" }),
            // 「本地文件 · JSON」现在名副其实：登录记忆、工作台对话、审批结果与窗口尺寸
            // 都写在 local-cache.json 里（见 Services.LocalCache）。
            // 该文件不随代码入库 —— 仓库 .gitignore 已忽略 Dome/Aster/AppData/。
            Row("数据存储", null, new InfoValue { Text = "本地文件 · JSON" })));

        // Links 塞在分区而不是分组里：右栏会把它单独渲染成「资源」小节，
        // 只有 HasLinks 为 true 的分区才出现这一块
        // 4 条链接都没传 Command（LinkItem.Command 可为 null），所以 XAML 里那一行是纯展示、
        // 整行不可点击；Sub 是标题右侧的小号灰字，用来放域名/版本号这类次要信息。
        s.Links.Add(new LinkItem { Icon = Controls.Icons.Globe, Title = "官方网站", Sub = "aster.studio" });
        s.Links.Add(new LinkItem { Icon = Controls.Icons.Book, Title = "更新日志", Sub = "v1.0.0" });
        s.Links.Add(new LinkItem { Icon = Controls.Icons.Message, Title = "问题反馈", Sub = "GitHub Issues" });
        s.Links.Add(new LinkItem { Icon = Controls.Icons.Doc, Title = "开源许可", Sub = "MIT / 第三方组件" });
        return s;
    }

    // ==================== 阅读时发现的问题（只记录，未改代码） ====================
    // 1) 「主题」实为多选：Chips(1, "深色", "浅色", "跟随系统") 看起来是三选一，
    //    但 ChipItem.IsSelected 是双向绑定直接翻转，没有任何互斥逻辑，
    //    用户可以同时点亮「深色」和「浅色」。若要单选，需要在 ChipItem 上加命令做互斥
    //    （可参照 SwatchCommand 的做法）。
    // 2) 「显示的指标」写着「最多四项」，但四项全部可点亮，也可全部熄灭，没有上下限校验。
    // 3) Noop 的注释说它避免「Command 为 null 时按钮自动变成禁用态」；按 Avalonia 11 的行为，
    //    null 命令通常被视作恒可执行、按钮不会变灰，这个理由存疑。更可靠的解释是语义占位与统一入口。
    // 4) 版本号散落在四处且各自独立：Footer 的 "Aster v1.4.2"、AboutCard.Version 的 "v1.4.2"、
    //    更新日志链接的 Sub "v1.4.2"、以及 avalonia 的兜底值 "11.3.22"（这个其实是框架版本，
    //    和产品版本不是一回事，容易被误读）。改版本号时必须同时改前三个。
    // 5) 界面上的「设置已同步至本地」「本地文件 · JSON」没有任何持久化代码支撑：
    //    本类不写文件、不读配置，进程重启后所有改动回到构造初值。
    // 6) RangeValue.Value 不做区间夹取：外部直接赋 200 时，DisplayText 会显示 "200%"，
    //    而 TrackBar 的可视滑块仍停在 Max 处，读数与滑块会不一致。目前只有界面拖动这一个入口，
    //    所以没暴露出来。
    // 7) SelectValue.Width（默认 158）在全部 17 个下拉里都没有被覆盖过，属于预留但未使用的配置项；
    //    同理 RangeValue.IsPercent 也没有任何读者。
    // 8) Footer 用了插值字符串 $"..."，但里面没有任何占位符，$ 是多余的（不影响行为）。
    // 9) SettingSection.Key 目前没有任何代码读取（Select 用引用比较），只起文档作用；
    //    若以后想按 Key 恢复上次分区，需要额外实现。
}
