using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Avalonia.Media;

namespace Aster.ViewModels;

// ===================== 设置行取值类型 =====================

public abstract class SettingValue : ObservableObject
{
}

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

public sealed class ToggleValue : SettingValue
{
    private bool _isOn;
    public bool IsOn
    {
        get => _isOn;
        set => Set(ref _isOn, value);
    }
}

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
            if (!Set(ref _value, Math.Round(value))) return;
            OnPropertyChanged(nameof(DisplayText));
        }
    }

    public string DisplayText => $"{Value:0}{Unit}";
}

public sealed class ChipItem : SelectableItem
{
    public string Label { get; init; } = string.Empty;
}

public sealed class ChipsValue : SettingValue
{
    public ObservableCollection<ChipItem> Items { get; } = new();
}

public sealed class ButtonValue : SettingValue
{
    public string Text { get; init; } = string.Empty;
    public bool IsPrimary { get; init; }
    public bool IsDanger { get; init; }
    public ICommand? Command { get; init; }
}

public sealed class InfoValue : SettingValue
{
    public string Text { get; init; } = string.Empty;
}

public sealed class PathValue : SettingValue
{
    public string Text { get; init; } = string.Empty;
}

public sealed class SwatchItem : SelectableItem
{
    public IBrush Brush { get; init; } = Palette.Solid("#6C8BFF");
    public ICommand Command { get; init; } = null!;
}

public sealed class PaletteValue : SettingValue
{
    public ObservableCollection<SwatchItem> Swatches { get; } = new();
}

// ===================== 行 / 分组 / 卡片 =====================

public sealed class SettingRow
{
    public string Title { get; init; } = string.Empty;
    public string? Desc { get; init; }
    public bool HasDesc => !string.IsNullOrEmpty(Desc);
    public object? Value { get; init; }
    public bool HasValue => Value is not null;
}

public sealed class SettingGroup
{
    public string Title { get; init; } = string.Empty;
    public ObservableCollection<SettingRow> Rows { get; } = new();
}

public sealed class LinkItem
{
    public string Icon { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Sub { get; init; } = string.Empty;
    public ICommand? Command { get; init; }
}

public sealed class AccountCard
{
    public string Initial { get; init; } = "陈";
    public string Name { get; init; } = "陈墨";
    public string Role { get; init; } = "管理员";
    public string Email { get; init; } = "chenmo@aster.io";
    public string Joined { get; init; } = "2024 年 3 月加入 · 最近登录 2 分钟前";
}

public sealed class AboutCard
{
    public string Name { get; } = "Aster";
    public string Version { get; } = "v1.4.2";
    public string Meta { get; } = "已是最新 · 2026-10-12 发布";
}

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

public sealed class SettingsViewModel : ObservableObject
{
    private SettingSection? _current;

    public SettingsViewModel()
    {
        SelectCommand = new RelayCommand(o =>
        {
            if (o is SettingSection s) Select(s);
        });

        SwatchCommand = new RelayCommand(o =>
        {
            if (o is not SwatchItem picked) return;
            foreach (var s in _allSwatches) s.IsSelected = ReferenceEquals(s, picked);
        });

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
        Select(Sections[0]);
    }

    public ObservableCollection<SettingSection> Sections { get; } = new();
    public ICommand SelectCommand { get; }
    public ICommand SwatchCommand { get; private set; } = null!;

    public SettingSection? Current
    {
        get => _current;
        private set => Set(ref _current, value);
    }

    public string Footer => $"Aster v1.4.2 · 设置已同步至本地";

    private readonly List<SwatchItem> _allSwatches = new();

    private void Select(SettingSection section)
    {
        foreach (var s in Sections) s.IsSelected = ReferenceEquals(s, section);
        Current = section;
    }

    private static SettingGroup Group(string title, params SettingRow[] rows)
    {
        var g = new SettingGroup { Title = title };
        foreach (var r in rows) g.Rows.Add(r);
        return g;
    }

    private static SettingRow Row(string title, string? desc, object? value)
        => new() { Title = title, Desc = desc, Value = value };

    private static SelectValue Select(params string[] options)
    {
        var v = new SelectValue();
        foreach (var o in options) v.Options.Add(o);
        v.Selected = options.FirstOrDefault() ?? string.Empty;
        return v;
    }

    private static ChipsValue Chips(int onCount, params string[] labels)
    {
        var v = new ChipsValue();
        for (var i = 0; i < labels.Length; i++)
            v.Items.Add(new ChipItem { Label = labels[i], IsSelected = i < onCount });
        return v;
    }

    private SettingSection BuildAccount()
    {
        var s = new SettingSection
        {
            Key = "account", Title = "账号", Icon = Controls.Icons.User,
            Subtitle = "个人信息、安全设置与登录会话",
            Command = SelectCommand,
            TopCard = new AccountCard()
        };
        s.Groups.Add(Group("基本信息",
            Row("显示名称", "在其他成员可见的位置展示的名字", new TextValue { Text = "陈墨" }),
            Row("邮箱", "用于登录与接收通知 · 已验证", new TextValue { Text = "chenmo@aster.io" }),
            Row("手机号", "用于二次验证与安全提醒 · 未绑定", new TextValue { Placeholder = "未绑定" }),
            Row("所属部门", "决定可查看的数据范围", Select("产品部", "技术部", "运营部"))));
        s.Groups.Add(Group("安全",
            Row("登录密码", "上次修改：2026-08-14", new ButtonValue { Text = "修改密码" }),
            Row("两步验证", "登录时需要输入额外的动态验证码", new ToggleValue { IsOn = true }),
            Row("登录设备", "当前有 3 台设备保持登录状态", new ButtonValue { Text = "管理设备" })));
        s.Groups.Add(Group("会话",
            Row("退出当前账号", "返回登录界面，本地缓存不会被清除", new ButtonValue { Text = "退出登录" }),
            Row("注销账号", "永久删除账号及全部关联数据，无法恢复",
                new ButtonValue { Text = "注销账号", IsDanger = true })));
        return s;
    }

    private SettingSection BuildGeneral()
    {
        var theme = Chips(1, "深色", "浅色", "跟随系统");
        var palette = new PaletteValue();
        var colors = new[] { "#6C8BFF", "#A78BFA", "#3DD6E8", "#3DDC97", "#F5B942", "#F8716E" };
        foreach (var c in colors)
            palette.Swatches.Add(new SwatchItem { Brush = Palette.Solid(c), Command = SwatchCommand });
        foreach (var sw in palette.Swatches) _allSwatches.Add(sw);
        palette.Swatches[0].IsSelected = true;

        var s = new SettingSection
        {
            Key = "general", Title = "通用", Icon = Controls.Icons.Sliders,
            Subtitle = "外观、区域语言与启动行为的偏好设置",
            Command = SelectCommand
        };

        var appearance = Group("外观");
        appearance.Rows.Add(Row("主题", "切换明暗配色，选择「跟随系统」会随操作系统外观自动切换", theme));
        appearance.Rows.Add(Row("强调色", "用于按钮、选中态与图表高亮", palette));
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

    private SettingSection BuildStorage()
    {
        var dataDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) +
                      System.IO.Path.DirectorySeparatorChar + "Aster";

        var s = new SettingSection
        {
            Key = "storage", Title = "存储与备份", Icon = Controls.Icons.Database,
            Subtitle = "数据目录、缓存清理与备份策略",
            Command = SelectCommand
        };

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
                new RangeValue { Min = 3, Max = 30, Value = 10, Unit = " 份" })));

        s.Groups.Add(Group("数据管理",
            Row("导出全部业务数据", "导出成员、订单与统计数据，用于迁移或归档",
                new ButtonValue { Text = "立即导出", IsPrimary = true }),
            Row("历史数据保留期", "超过保留期的明细数据会被聚合后删除",
                Select("永久保留", "1 年", "2 年", "3 年"))));
        return s;
    }

    private SettingSection BuildAbout()
    {
        var avalonia = typeof(Avalonia.Controls.Window).Assembly.GetName().Version?.ToString(3) ?? "11.3.22";
        var runtime = Environment.Version.ToString(3);

        var s = new SettingSection
        {
            Key = "about", Title = "关于", Icon = Controls.Icons.Info,
            Subtitle = "版本信息与相关资源",
            Command = SelectCommand,
            TopCard = new AboutCard()
        };

        s.Groups.Add(Group("运行环境",
            Row("界面框架", null, new InfoValue { Text = $"Avalonia {avalonia}" }),
            Row("运行时", null, new InfoValue { Text = $".NET {runtime} · {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}" }),
            Row("渲染后端", null, new InfoValue { Text = "Skia" }),
            Row("数据存储", null, new InfoValue { Text = "本地文件 · JSON" })));

        s.Links.Add(new LinkItem { Icon = Controls.Icons.Globe, Title = "官方网站", Sub = "aster.studio" });
        s.Links.Add(new LinkItem { Icon = Controls.Icons.Book, Title = "更新日志", Sub = "v1.4.2" });
        s.Links.Add(new LinkItem { Icon = Controls.Icons.Message, Title = "问题反馈", Sub = "GitHub Issues" });
        s.Links.Add(new LinkItem { Icon = Controls.Icons.Doc, Title = "开源许可", Sub = "MIT / 第三方组件" });
        return s;
    }
}
