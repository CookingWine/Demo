// ============================================================================
// Items.cs —— 纯展示模型 + 设计稿色板（只做数据载体，不含业务流程）
// 1) Palette：把设计稿的十六进制色值 / Color 转成 Avalonia 画刷（Solid/Alpha/Gradient）；
// 2) 导航模型：RailItem（74px 一级 rail）与 SubNavEntry 家族（二级导航的分组与链接）；
// 3) 内容模型：KpiItem、RecentDocItem、QuickAction、PlainRow、SegmentItem。
// 位置：MainWindowViewModel 负责构造这些集合，XAML 通过 {Binding} 直接消费。
// 坑：颜色统一走 Palette，不要在 XAML 里再硬编码；带 Kind/Tone 的模型改配色时只改映射表。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media;

namespace Aster.ViewModels;

// ---------- 色板 ----------
/// <summary>设计稿色板，供视图模型直接构造画刷。</summary>
// 为什么是 static class：Palette 没有实例状态，只是一组「色值 → 画刷」的转换函数，
// 做成静态类既避免误 new，也让调用点写成 Palette.Solid(...) 一眼看出与实例无关。
// 为什么放 ViewModels 命名空间：这些画刷只被 VM 构造的数据模型使用（KpiItem / RecentDocItem），
// 视图侧不直接碰它；逻辑上属于「为界面准备的数据」，放在 VM 层比放 Controls 更贴切。
public static class Palette
{
    // 语义色常量：品牌蓝 / 紫 / 青 / 绿（正向）/ 琥珀（预警）/ 红（负向）/ 深色底。
    // 为什么是 static readonly：Color 是值类型，readonly 防止运行期被改写；
    // static 表示全进程只存一份。字段初始化器只在类型第一次被访问时执行一次
    // （由编译器生成的静态构造函数保证），之后每次读 Palette.AccentColor 只是读一个
    // 已经解析好的结构体，不会重复执行 Color.Parse 的字符串解析。
    // 命名按「用途/情绪」而不是色相（绿=正向、琥珀=预警、红=负向），换肤时只改这里的值，
    // 所有派生画刷（含 Alpha / Gradient）会自动跟着变。
    // Color.Parse 接受 #RRGGBB 时会把 A 通道补成 255（完全不透明）。
    public static readonly Color AccentColor = Color.Parse("#6C8BFF");
    public static readonly Color VioletColor = Color.Parse("#A78BFA");
    public static readonly Color CyanColor = Color.Parse("#3DD6E8");
    public static readonly Color GreenColor = Color.Parse("#3DDC97");
    public static readonly Color AmberColor = Color.Parse("#F5B942");
    public static readonly Color RedColor = Color.Parse("#F8716E");
    public static readonly Color Bg2Color = Color.Parse("#13161D");

    // 同名两个重载的分工：写死色值时用 string 版（可读性最好、与设计稿一一对应），
    // 复用上面的语义色常量时用 Color 版 —— 这样 #6C8BFF 这类字面量只出现在本文件的常量区，
    // 不会散落到 MainWindowViewModel 里。
    // 每次调用都会 new 一个新的 SolidColorBrush：这些函数被当作「计算属性的返回值」使用，
    // 绑定每读一次属性就重新求值一次，因此会有少量分配；换来的是画刷不会被别处的绑定改坏。
    // 返回类型写 IBrush 而不是 SolidColorBrush：调用方（模型属性）只需要 IBrush，
    // 将来若把某个色块换成渐变或位图画刷，模型签名不用跟着改。
    // 另外一个代价要清楚：每次绑定重读都会拿到一个新实例，Avalonia 会认为「画刷变了」而重绘，
    // 所以这些属性不适合放在高频刷新的路径上（本工程的卡片数据都是构造时定型的，不受影响）。
    /// <summary>十六进制字符串 → 纯色画刷，写 XAML 风格的色值最省事。</summary>
    public static IBrush Solid(string hex) => new SolidColorBrush(Color.Parse(hex));

    /// <summary>Color → 纯色画刷，用于复用上面的语义色常量。</summary>
    public static IBrush Solid(Color c) => new SolidColorBrush(c);

    /// <summary>
    /// 保留原色、只改透明度的画刷：alpha 传 0~1 的倍数（如 0.10），内部换算成 0~255。
    /// 主要用于「浅色底 + 同色文字」的标签，比如 KpiItem.DeltaBackground。
    /// </summary>
    // ── alpha * 255 这一步到底怎么算 ───────────────────────────────────────────
    // Color.FromArgb 要的是 0~255 的 byte，而设计稿与 XAML 习惯写 0~1 的小数，
    // 所以先乘 255 再取整。Math.Round 的默认模式是「银行家舍入」
    // （MidpointRounding.ToEven：恰好落在 .5 时往偶数方向取），不是四舍五入的 .5 进位。
    // 本工程实际用到的几个 alpha 换算结果（括号里是等价的十六进制 A 通道）：
    //   0.05 → 12.75 → 13（#0D）   用于中性标签底
    //   0.10 → 25.5  → 26（#1A）   恰好 .5，银行家舍入取偶数 26 而不是 25
    //   0.12 → 30.6  → 31（#1F）   用于图标底
    // 强转 (byte) 是截断而非饱和：若调用方传进来的 alpha 超出 0~1（例如误传 255），
    // 乘积会越过 byte 上限并按 256 取模回绕，得到完全不同的透明度，且不报错。
    // 这里不做 Math.Clamp，约定由调用方保证 0 ≤ alpha ≤ 1。
    // 语义上只替换 A 通道，R/G/B 原样保留，所以「淡底」与「实色字」永远同源，
    // 不会出现「绿字配红底」这类不合色板的组合。
    public static IBrush Alpha(Color c, double alpha)
        // 从左到右：透明度 0~1 → 0~255；色相通道 c.R / c.G / c.B 原样带入。
        => new SolidColorBrush(Color.FromArgb((byte)Math.Round(alpha * 255), c.R, c.G, c.B));

    /// <summary>Alpha 的字符串重载，便于直接写设计稿色值。</summary>
    // 纯转调：字符串先 Parse 成 Color，再走上面的重载，两处换算逻辑只有一份。
    public static IBrush Alpha(string hex, double alpha) => Alpha(Color.Parse(hex), alpha);

    /// <summary>
    /// 两色线性渐变。horizontal=true 时从左到右，否则自上而下；
    /// RelativePoint 用相对单位，保证画刷跟随控件尺寸拉伸。
    /// </summary>
    // ── RelativePoint 与 RelativeUnit.Relative 的含义 ───────────────────────────
    // Avalonia 的渐变端点用 RelativePoint 表达，两个分量各自可以选参照系：
    //   RelativeUnit.Relative：分量是「画刷作用区域包围盒的百分比」，0 = 左/上边，1 = 右/下边，
    //   所以 (0.5, 0.5) 是区域正中，(1, 0) 是右上角；
    //   RelativeUnit.Absolute：分量是设备无关像素的绝对坐标，与控件尺寸无关，
    //   换个大按钮渐变就会停在中间、后半截变成纯色。
    // 这里三个点全用 Relative，好处是同一个画刷铺在不同尺寸的卡片/条纹上都自动铺满，
    // 不需要为每个尺寸写一份坐标。
    // 方向只用 EndPoint 区分，StartPoint 恒为左上角：
    //   horizontal = true  → EndPoint (1,0) 右上，颜色沿 +X 铺开（左→右）；
    //   horizontal = false → EndPoint (0,1) 左下，颜色沿 +Y 铺开（上→下，默认）。
    // GradientStops = { ... } 是集合初始化器语法（注意没有 new）：
    // LinearGradientBrush 构造时已经把 GradientStops 建成空集合，这里只是往里 Add 两个停点，
    // 不会替换集合实例 —— 这一点从「没有 new 也能编译」就能反推出来（否则会报 CS1918）。
    public static IBrush Gradient(string from, string to, bool horizontal = false)
        => new LinearGradientBrush
        {
            // 起点固定在左上顶点：百分比以包围盒为参照，0,0 永远落在区域左上角。
            StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
            // 方向开关：三元只换端点的 x / y，其余参数与起点完全一致，避免写两个几乎相同的分支。
            EndPoint = horizontal
                ? new Avalonia.RelativePoint(1, 0, Avalonia.RelativeUnit.Relative)
                : new Avalonia.RelativePoint(0, 1, Avalonia.RelativeUnit.Relative),
            // 两个停点：Offset 0 取 from、Offset 1 取 to，中间由渲染器线性插值；
            // 只给两端就是标准两色过渡，想加三段色只需在 0~1 之间再插一个停点。
            GradientStops =
            {
                new GradientStop(Color.Parse(from), 0),
                new GradientStop(Color.Parse(to), 1)
            }
        };
}

// ---------- 导航模型 ----------
/// <summary>可选中列表项的通用外壳。</summary>
// 继承 ObservableObject 的收益只有一个：IsSelected 变化时能发 PropertyChanged，
// 模板里的 Classes.selected="{Binding IsSelected}" 才会在点选后立刻让样式类跟随切换。
// 这一层抽象不含任何业务字段，纯做「可选」这一个语义，所以 RailItem / SegmentItem 都能直接复用。
// 与下面 SubNavEntry 的关系：两者是平行实现而不是继承关系 —— SubNavEntry 那一族需要和
// 「分组标题」共用一个集合类型（模板按具体类型分发），若让分组标题也继承本类，
// 就会把「可选」这个语义安到一组根本不可点的标题上，读代码时容易被误导。
public abstract class SelectableItem : ObservableObject
{
    private bool _isSelected;

    /// <summary>
    /// 选中态。放在基类而不是各自再写一遍，是为了让 DataTemplate 统一用 IsSelected 驱动高亮样式，
    /// 由一个 VM 集中维护「同一组里只有一项为 true」（见 MainWindowViewModel.SelectModule）。
    /// </summary>
    // setter 用 Set 一行搞定：值没变时不发通知（同一项被重复点选不会引发无意义刷新），
    // 值变了才通知，绑定着的 Classes.selected 随即切换样式类。
    // 谁改它：本类的读者是 RailItem（MainWindowViewModel.SelectModule 遍历 RailItems 整组重算）；
    // 注意二级导航那一组走的是 SubNavEntry 自己那份 IsSelected，与本类无关。
    // 两处都是「整组重算」，所以互斥性由遍历本身保证，不依赖任何标志位。
    public bool IsSelected
    {
        // 纯读，不发通知：绑定每帧读多少次 getter 都无所谓。
        get => _isSelected;
        // 写入走 Set：相等则静默返回（界面不动），不等则写字段 + 发一次通知。
        set => Set(ref _isSelected, value);
    }
}

/// <summary>一级导航（74px rail）。</summary>
// 74 是 ShellView 里三栏布局第一列的固定宽度：图标 19px + 竖排文字，刚好放得下且不再多占。
// 继承 SelectableItem 的理由：五个导航项里只有「当前项」会变，其余字段构造后即定型。
public sealed class RailItem : SelectableItem
{
    /// <summary>
    /// 构造一级导航项。Key 是后续 switch 分支的判别依据；
    /// IsSettings 在构造时按 key 算好，避免在 XAML 里做字符串比较。
    /// </summary>
    // 调用方只有 MainWindowViewModel 的构造函数（RailItems 集合初始化器），一次建五个实例。
    // 因为属性全是 get-only，这里不做任何校验：传空 key 也只会在 switch 里落到 default 分支。
    public RailItem(string key, string title, string icon, ICommand command)
    {
        // 步骤 1：四个展示 / 行为字段原样透传。属性都没有 setter，构造完即定型，运行期不再变。
        Key = key;
        Title = title;
        Icon = icon;
        Command = command;
        // 步骤 2：派生标记在这里算一次，XAML 侧直接绑 bool。
        //         用 key 字符串比较而不是枚举，是因为 key 还要给
        //         SyncRailSelection / AdminViewModel.Select 的 switch 复用，统一用字符串更省事。
        IsSettings = key == "settings";
    }

    // 全部只读：导航项在运行期不变，只有 IsSelected 会变（所以继承 SelectableItem）。
    // 逐条说明谁在读：
    //   Key     —— 模块标识。SelectModule 的 switch、SyncRailSelection 的映射表、
    //              AdminViewModel.Select 三处共用同一套字符串，"settings" 是唯一的特殊值。
    //   Title   —— rail 里那行 10.5px 小字，同时给 ToolTip.Tip 用（模板里两处绑定都读它）。
    //   Icon    —— 图标路径数据（Controls.Icons 里的常量字符串），喂给 ctrl:Icon 的 Data。
    //   Command —— 点这一项执行什么：前四项都是 SelectModuleCommand（模板靠 CommandParameter="{Binding}"
    //              把自身传回去），第五项「设置」是 OpenSettingsCommand。
    // 这些属性都没有 setter，因此不需要 Set、也不会通知 —— 绑定读一次就定型。
    public string Key { get; }
    public string Title { get; }
    public string Icon { get; }
    public ICommand Command { get; }
    /// <summary>「设置」不是切换模块，而是打开右侧浮层，模板据此把它单独摆到 rail 底部。</summary>
    public bool IsSettings { get; }
}

/// <summary>二级导航条目基类。</summary>
// 存在的意义是「让分组标题和链接共用一种集合类型」：
// MainWindowViewModel.SubNavEntries 声明为 ObservableCollection<SubNavEntry>，
// 里面混装两种派生类，ShellView 用两个隐式 DataTemplate 按运行时类型分别渲染
// （SubNavGroupEntry → 一行不可点的粗体标题；SubNavLinkEntry → 可点的 Button）。
// 这也是它不继承 SelectableItem 的原因：分组标题不需要「可选」，但必须能放进同一个集合。
public abstract class SubNavEntry : ObservableObject
{
    private bool _isSelected;

    // 这里的 IsSelected 与 SelectableItem.IsSelected 是两份独立实现（不是同一份继承来的）。
    // 实际上只有 SubNavLinkEntry 会真正被选中：SelectSubNav 用 OfType<SubNavLinkEntry>() 过滤，
    // 分组标题虽然白拿了这属性，但永远不会被写。
    // 谁改它：MainWindowViewModel.SelectSubNav，遍历整个集合把「key 匹配的那一项」置 true。
    public bool IsSelected
    {
        // 纯读。
        get => _isSelected;
        // 写入走 Set：同值静默、异值通知，模板的 Classes.selected 随之切换。
        set => Set(ref _isSelected, value);
    }
}

/// <summary>二级导航里的分组标题（如「项目」「最近」），只负责视觉分节，不可点击。</summary>
public sealed class SubNavGroupEntry : SubNavEntry
{
    // 表达式体构造函数：只有一个只读属性，连方法体都不需要。
    // 它没有 Key —— 这正是 SelectSubNav 能用 OfType<SubNavLinkEntry>() 把分组排除在选中逻辑外的原因。
    public SubNavGroupEntry(string title) => Title = title;
    // 渲染成 14px Bold + Text3（最弱一级灰）的一行文字，Template 里不自带任何交互。
    public string Title { get; }
}

/// <summary>
/// 二级导航里的可点击链接。参数较多，是因为同一个模板要兼顾三种形态：
/// 常规入口（带图标 + 右侧数字徽标）、强调入口（Emphasized，字号/字重更大）、
/// 「最近」列表（ShowIcon=false、Height 更矮）。
/// </summary>
// 参数默认值就是三种形态的「预设」，调用方只覆盖需要改的那几个：
//   常规入口   → 全默认（showIcon: true, emphasized: false, height: 38），可传 badge 显示右侧徽标
//   强调入口   → emphasized: true，TitleFontSize / TitleWeight 随之变大
//   「最近」行 → showIcon: false, height: 34，视觉上比常规入口更轻
// 实际调用点见 MainWindowViewModel 的 AddSubNav / AddRecent。
// 注意 Height 是「由 VM 给出、模板直接绑」的做法：模板里 Height="{Binding Height}"，
// 视图因此不需要知道「哪个形态该多高」，条件判断全留在 C# 里。
public sealed class SubNavLinkEntry : SubNavEntry
{
    public SubNavLinkEntry(string key, string title, string icon, ICommand command, string? badge = null,
        bool showIcon = true, bool emphasized = false, double height = 38)
    {
        // 步骤 1：身份与行为。key 会被 SelectSubNav 用来判断「是不是我被点了」，
        //         command 是 SubNavCommand（一个共享实例，靠 CommandParameter 区分条目）。
        Key = key;
        Title = title;
        Icon = icon;
        Command = command;
        // 步骤 2：可选徽标。null 表示没有，HasBadge 会据此算成 false。
        Badge = badge;
        // 步骤 3：三种形态的开关，全部是构造时一次定型的只读值。
        ShowIcon = showIcon;
        Emphasized = emphasized;
        Height = height;
    }

    public string Key { get; }
    public string Title { get; }
    public string Icon { get; }
    public ICommand Command { get; }
    public string? Badge { get; }
    /// <summary>给徽标标签的 IsVisible 用：Badge 为空时不占位，避免空白留白。</summary>
    public bool HasBadge => !string.IsNullOrEmpty(Badge);
    public bool ShowIcon { get; }
    public bool Emphasized { get; }
    // 38 / 34 这两个高度是设计稿给定的行高：常规入口 38，弱化后的「最近」行 34，
    // 差值刚好让「最近会话」区块看起来更紧凑，但又不至于小到点不中（34 仍是可点面积）。
    public double Height { get; }
    // 字号与字重由 Emphasized 派生，保证模板里不用再写值转换器。
    // 15/14 是「强调入口比常规入口大半档」的约定；Medium 与 Normal 同理，只差一档字重。
    // 这两个属性都是纯计算、无后备字段：Emphasized 构造后不再变，所以它们也不会变，不需要通知。
    public double TitleFontSize => Emphasized ? 15 : 14;
    public Avalonia.Media.FontWeight TitleWeight => Emphasized ? Avalonia.Media.FontWeight.Medium : Avalonia.Media.FontWeight.Normal;
}

// ---------- 内容模型 ----------
/// <summary>指标卡。</summary>
// 与 RailItem 的对比最能说明「要不要继承 ObservableObject」的判断标准：
//   RailItem 有会变的选中态 → 继承 SelectableItem（间接继承 ObservableObject）；
//   KpiItem 的所有字段都是 init-only，构造完就是一张只读快照 → 不继承，
//   既不发通知也不需要界面回写，绑定全是单向只读的。
// 什么时候该反过来改用 set + Set(...)：当卡片数据会随实时行情刷新时。
// 现在一屏四张卡都是 MainWindowViewModel 初始化时铺好的静态演示数据。
public sealed class KpiItem
{
    // 全部用 init：这些卡片在构造时一次性铺好数据，之后不再变，因此不需要 INotifyPropertyChanged。
    // init 与 set 的差别：init 访问器只允许在对象初始化器（new KpiItem { ... }）或构造函数里赋值，
    // 之后编译期就禁止再改 —— 正好表达「数据是快照，不是可变状态」，写错地方会直接编译失败。
    // 默认值 = string.Empty 是为了让可空引用类型不留 null，XAML 里少一层判空。
    public string Title { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string? Unit { get; init; }
    public string? Tail { get; init; }
    public string Delta { get; init; } = string.Empty;
    /// <summary>涨跌语义，只接受 up / dn / warn / 其它（中性），决定下面两个画刷的颜色。</summary>
    public string DeltaKind { get; init; } = "up";
    // Icon / Spark 都是可空：为 null 表示「这张卡没有这个部件」，模板靠下面的 Has* 决定显不显示，
    // 而不是靠「绑一个空字符串让它渲染成透明」——后者仍会占位。
    public string? Icon { get; init; }
    // 迷你折线的原始点集。SparkBrush 是画这条线用的画刷，两者配套传；
    // 点数与语义无关，纯粹是「采样点」，由 KpiItem 的生产者（AdminViewModel）决定。
    public IReadOnlyList<double>? Spark { get; init; }
    public IBrush? SparkBrush { get; init; }

    // 空字符串统一视为「没有」，方便 XAML 直接绑 IsVisible，无需再写转换器。
    public bool HasIcon => !string.IsNullOrEmpty(Icon);
    /// <summary>折线至少要两个点才成立，少于两点直接不显示迷你图。</summary>
    // Spark is { Count: > 1 } 是属性模式：先判非 null，再要求 Count > 1，
    // 一个表达式同时完成「空引用」与「点数不足」两道检查，比 Spark != null && Spark.Count > 1 更紧凑。
    public bool HasSpark => Spark is { Count: > 1 };
    // 允许个别卡片覆盖数值颜色（例如告警卡的数值也用琥珀色），不传则走下面的默认浅白。
    public IBrush? ValueBrushOverride { get; init; }

    /// <summary>数值默认浅白；个别卡片（如告警）可传 ValueBrushOverride 改色。</summary>
    // "#E9ECF4" 与 Theme.axaml 的 Text0Color 同值：数值是卡片里最亮的一层，与全局主文字保持一致。
    // 用 ?? 而不是三元：null 与「没传」语义相同，一行就能表达「有就用、没有就用默认」。
    public IBrush ValueBrush => ValueBrushOverride ?? Palette.Solid("#E9ECF4");

    /// <summary>
    /// DeltaKind → 涨跌文字色：up 绿、dn 红、warn 琥珀，其余走中性灰。
    /// 之所以用字符串常量而不是枚举，是为了在构造 KpiItem 时可以直接写字面量。
    /// </summary>
    // ── DeltaKind 取值 → 画刷的完整映射（换配色只改这一张表和下面的表）────────────
    // ┌──────────────┬──────────────────────────┬──────────────────────────────────────────┐
    // │ DeltaKind    │ DeltaBrush（文字 / 箭头）│ DeltaBackground（标签底，同色 + 透明度）  │
    // ├──────────────┼──────────────────────────┼──────────────────────────────────────────┤
    // │ "up"         │ GreenColor   #3DDC97     │ GreenColor  @0.10 → #1A3DDC97            │
    // │ "dn"         │ RedColor     #F8716E     │ RedColor    @0.10 → #1AF8716E            │
    // │ "warn"       │ AmberColor   #F5B942     │ AmberColor  @0.10 → #1AF5B942            │
    // │ 其它 / 未传  │ #9BA3B7 中性灰           │ #FFFFFF     @0.05 → #0DFFFFFF            │
    // └──────────────┴──────────────────────────┴──────────────────────────────────────────┘
    // 未传时有默认值：DeltaKind = "up"，所以「不写这个参数」等于「正向」，而不是中性。
    // 语义约定：up/dn 是业务涨跌，warn 是「需要注意但不一定坏」（如审批超时），
    // 中性灰用于「无变化 / 无可比数据」。
    public IBrush DeltaBrush => DeltaKind switch
    {
        "up" => Palette.Solid(Palette.GreenColor),
        "dn" => Palette.Solid(Palette.RedColor),
        "warn" => Palette.Solid(Palette.AmberColor),
        // 默认臂：任何拼错的字符串都会静默落到这里，不会抛异常 —— 好处是界面不崩，
        // 代价是写错 "up " / "UP" 这种值时只会「颜色不对」，不会报错提示。
        _ => Palette.Solid("#9BA3B7")
    };

    /// <summary>与 DeltaBrush 同源的标签底色，统一取 10% 透明度，保证「浅底 + 同色字」的观感。</summary>
    // 与上表一一对应，只是把 Solid 换成 Alpha(..., 0.10)；中性分支用 5% 白而不是 10%，
    // 因为灰底的观感本来就更重，10% 会在深色背景上显出一块明显的方块。
    // 两个 switch 用同一套字符串字面量，改取值时两处都要动（这是字符串代替枚举的代价）。
    public IBrush DeltaBackground => DeltaKind switch
    {
        "up" => Palette.Alpha(Palette.GreenColor, 0.10),
        "dn" => Palette.Alpha(Palette.RedColor, 0.10),
        "warn" => Palette.Alpha(Palette.AmberColor, 0.10),
        // 默认臂：5% 白，几乎不改变底色，只让标签有个极淡的边界。
        _ => Palette.Alpha("#FFFFFF", 0.05)
    };

    // Has* 三兄弟都是「字符串/集合是否有值」的派生 bool，专供 XAML 的 IsVisible 使用。
    // 放在模型里而不是视图里，是为了避免每条绑定都挂一个转换器（转换器无法在编译期检查）。
    public bool HasTail => !string.IsNullOrEmpty(Tail);
    public bool HasUnit => !string.IsNullOrEmpty(Unit);
}

/// <summary>最近文档行。Tone 决定图标底色与图标前景：violet / cyan / 默认 accent。</summary>
// 同样是 init-only 的只读快照，故不继承 ObservableObject。
// 模板是一行三列：左边 30px 左右的圆角图标块（用 Tone 上色）、中间名称 + 元信息、右侧时间。
public sealed class RecentDocItem
{
    // Name 是文档名（单行省略），Meta 是「类型 · 大小」这类次要信息，Time 是相对时间（"2 小时前"）。
    // 三者都由 AdminViewModel 在构造集合时写好，运行期不再改。
    public string Name { get; init; } = string.Empty;
    public string Meta { get; init; } = string.Empty;
    public string Time { get; init; } = string.Empty;
    // 默认 "accent"：不传 Tone 就落到下面两个 switch 的默认臂，得到品牌蓝。
    public string Tone { get; init; } = "accent";

    // ── Tone 取值 → 图标画刷的映射 ─────────────────────────────────────────────
    // ┌──────────────┬────────────────────────────────────┬────────────────────────────┐
    // │ Tone         │ IconBackground（同色 12% 淡底）    │ IconBrush（实色前景）      │
    // ├──────────────┼────────────────────────────────────┼────────────────────────────┤
    // │ "violet"     │ VioletColor @0.12 → #1FA78BFA      │ VioletColor   #A78BFA      │
    // │ "cyan"       │ CyanColor   @0.12 → #1F3DD6E8      │ CyanColor     #3DD6E8      │
    // │ 其它 / 未传  │ AccentColor @0.12 → #1F6C8BFF      │ AccentColor   #6C8BFF      │
    // └──────────────┴────────────────────────────────────┴────────────────────────────┘
    // 12% 是经验值：再淡就看不出「这是一块彩色图标底」，再浓就会盖过右侧的文档名。
    /// <summary>图标底色是同色 12% 透明，避免整行过亮。</summary>
    public IBrush IconBackground => Tone switch
    {
        "violet" => Palette.Alpha(Palette.VioletColor, 0.12),
        "cyan" => Palette.Alpha(Palette.CyanColor, 0.12),
        // 默认臂承担 "accent" 与任何未知取值，保证永远有颜色可用。
        _ => Palette.Alpha(Palette.AccentColor, 0.12)
    };

    /// <summary>图标前景用实色，与上面的淡底形成对比。</summary>
    // 与上表严格同源：同一个 Tone 在两张表里落到同一个语义色，只是 Solid 与 Alpha 的区别，
    // 所以「淡底 + 亮图标」的组合永远成立，不会出现青底紫图标。
    public IBrush IconBrush => Tone switch
    {
        "violet" => Palette.Solid(Palette.VioletColor),
        "cyan" => Palette.Solid(Palette.CyanColor),
        _ => Palette.Solid(Palette.AccentColor)
    };
}

/// <summary>快捷入口（图标 + 文案）。点击行为由外层模板决定，当前 HomeView 只渲染按钮、未绑命令，所以自身不带 Command。</summary>
// 两个字段都是纯展示：Icon 是路径数据，Title 是按钮下的一行小字。
// 不继承 ObservableObject 也不需要 IsSelected —— 它是一次性的入口方块，没有「当前选中」的概念。
// 代价是：如果哪天要让某个入口变灰/变文案，就得把它升级成带通知的模型，而不是改一个绑定。
public sealed class QuickAction
{
    public string Title { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
}

/// <summary>简单展示行（队列、导出记录等）。</summary>
// 三列结构：Index（序号/时间）、Text（主文案）、Value（右侧数值，可空）。
// Value 为空时模板靠「绑空字符串」自然留白，没有额外的 Has* 派生属性，属于该模型的简化取舍。
// 常见用途：审批队列、导出记录列表 —— 都是只需要展示、不需要交互的行。
public sealed class PlainRow
{
    public string Index { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public string? Value { get; init; }
}

/// <summary>分段控件项。</summary>
// 与 RailItem 的差别：分段控件（如「待处理 / 全部」）通常只有两三项、横向排列、无图标，
// 但选中互斥的规则与 rail 完全一致，所以直接复用 SelectableItem。
public sealed class SegmentItem : SelectableItem
{
    // 继承 SelectableItem：靠 IsSelected 让模板把当前段渲染成高亮样式。
    // 构造只有两个参数：Label 是段名，Command 是点击后要执行的命令。
    // 选中态由谁维护：本意是「命令体里遍历同组条目逐个赋 IsSelected」，
    // 但当前唯一使用方（MarkdownViewModel 的 Modes）只在构造时把 Modes[1].IsSelected 置 true，
    // SetMode 并不重算选中态 —— 也就是说分段高亮不会随点击移动，属于现状缺口（不在本文件范围内）。
    public SegmentItem(string label, ICommand command)
    {
        // 两个字段都是 get-only，构造后定型，没有校验。
        Label = label;
        Command = command;
    }

    // Label 直接作为按钮文字；Command 一般是一个 SelectCommand，靠 CommandParameter 区分段。
    public string Label { get; }
    public ICommand Command { get; }
}
