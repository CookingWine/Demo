using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Avalonia.Media;

namespace Aster.ViewModels;

public sealed class MemberRow
{
    public string Name { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Initial => Name.Length > 0 ? Name[..1] : "?";
    public string AvatarFrom { get; init; } = "#7A96FF";
    public string AvatarTo { get; init; } = "#5C7BF7";
    public string Role { get; init; } = "编辑";
    public string Status { get; init; } = "正常";
    public string StatusKind { get; init; } = "ok";
    public string LastActive { get; init; } = string.Empty;
    public string Department { get; init; } = string.Empty;

    public IBrush Avatar => Palette.Gradient(AvatarFrom, AvatarTo);

    public IBrush StatusBrush => StatusKind switch
    {
        "ok" => Palette.Solid(Palette.GreenColor),
        "wait" => Palette.Solid(Palette.AmberColor),
        _ => Palette.Solid("#626A7D")
    };

    public IBrush StatusBackground => StatusKind switch
    {
        "ok" => Palette.Alpha(Palette.GreenColor, 0.10),
        "wait" => Palette.Alpha(Palette.AmberColor, 0.10),
        _ => Palette.Alpha("#FFFFFF", 0.06)
    };
}

public sealed class OrderRow
{
    public string Id { get; init; } = string.Empty;
    public string Customer { get; init; } = string.Empty;
    public string Amount { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string StatusKind { get; init; } = "ok";
    public string CreatedAt { get; init; } = string.Empty;

    public IBrush StatusBrush => StatusKind switch
    {
        "ok" => Palette.Solid(Palette.GreenColor),
        "wait" => Palette.Solid(Palette.AmberColor),
        _ => Palette.Solid("#626A7D")
    };

    public IBrush StatusBackground => StatusKind switch
    {
        "ok" => Palette.Alpha(Palette.GreenColor, 0.10),
        "wait" => Palette.Alpha(Palette.AmberColor, 0.10),
        _ => Palette.Alpha("#FFFFFF", 0.06)
    };
}

public sealed class AuditRow
{
    public string Time { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public string Tone { get; init; } = "accent";

    public IBrush DotBrush => Tone switch
    {
        "green" => Palette.Solid(Palette.GreenColor),
        "cyan" => Palette.Solid(Palette.CyanColor),
        "red" => Palette.Solid(Palette.RedColor),
        "amber" => Palette.Solid(Palette.AmberColor),
        _ => Palette.Solid(Palette.AccentColor)
    };

    public IBrush DotGlow => Tone switch
    {
        "green" => Palette.Solid("#3DDC97"),
        "cyan" => Palette.Solid("#3DD6E8"),
        "red" => Palette.Solid("#F8716E"),
        "amber" => Palette.Solid("#F5B942"),
        _ => Palette.Solid("#6C8BFF")
    };
}

public sealed class ReportCard
{
    public string Title { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Delta { get; init; } = string.Empty;
    public bool Up { get; init; } = true;

    public IBrush DeltaBrush => Up ? Palette.Solid(Palette.GreenColor) : Palette.Solid(Palette.RedColor);
}

public sealed class ExportRow
{
    public string File { get; init; } = string.Empty;
    public string Owner { get; init; } = string.Empty;
    public string Time { get; init; } = string.Empty;
    public string Size { get; init; } = string.Empty;
}

/// <summary>管理页各面板的基类。</summary>
public abstract class AdminPanel : ObservableObject
{
    public abstract string Title { get; }
    public abstract string Subtitle { get; }
    public ObservableCollection<SegmentItem> Segments { get; } = new();
    public bool HasSegments => Segments.Count > 0;
    public string PrimaryAction { get; protected set; } = string.Empty;
}

public enum OverviewPeriod
{
    Today,
    Week,
    Month,
    Custom
}

public sealed class OverviewPanel : AdminPanel
{
    public OverviewPanel(ICommand segmentCommand)
    {
        Segments.Add(new SegmentItem("今日", segmentCommand));
        Segments.Add(new SegmentItem("近 7 天", segmentCommand));
        Segments.Add(new SegmentItem("本月", segmentCommand));
        Segments.Add(new SegmentItem("自定义", segmentCommand));
        Segments[1].IsSelected = true;
        PrimaryAction = "新增成员";
    }

    public override string Title => "运营管理";
    public override string Subtitle => "数据更新时间：2026-10-20 14:18 · 每 5 分钟自动刷新";
    public string ChartSubtitle => "2026-10-14 至 2026-10-20";

    public ObservableCollection<KpiItem> Kpis { get; } = new()
    {
        new KpiItem
        {
            Title = "活跃用户", Value = "12,486", Delta = "↑ 12.4%", DeltaKind = "up",
            Icon = Controls.Icons.Users, SparkBrush = Palette.Solid(Palette.GreenColor),
            Spark = new double[] { 17, 14, 16, 9, 11, 6, 3 }
        },
        new KpiItem
        {
            Title = "订单总量", Value = "3,214", Delta = "↑ 8.1%", DeltaKind = "up",
            Icon = Controls.Icons.Cart, SparkBrush = Palette.Solid(Palette.GreenColor),
            Spark = new double[] { 14, 16, 11, 13, 8, 9, 5 }
        },
        new KpiItem
        {
            Title = "总营收", Value = "¥482,900", Delta = "↑ 23.6%", DeltaKind = "up",
            Icon = Controls.Icons.Currency, SparkBrush = Palette.Solid(Palette.GreenColor),
            Spark = new double[] { 18, 15, 17, 10, 12, 5, 2 }
        },
        new KpiItem
        {
            Title = "转化率", Value = "4.82%", Delta = "↓ 0.8%", DeltaKind = "dn",
            Icon = Controls.Icons.Trend, SparkBrush = Palette.Solid(Palette.RedColor),
            Spark = new double[] { 5, 8, 6, 12, 10, 15, 17 }
        }
    };

    public IReadOnlyList<double> Visits { get; } = new double[] { 108, 92, 100, 66, 74, 42, 30 };
    public IReadOnlyList<double> Orders { get; } = new double[] { 126, 118, 122, 104, 110, 92, 86 };
    public string[] AxisLabels { get; } = { "10-14", "10-15", "10-16", "10-17", "10-18", "10-19", "10-20" };

    public IReadOnlyList<double> ChannelValues { get; } = new double[] { 46, 28, 17, 9 };

    public ObservableCollection<MemberRow> Members { get; } = new(SeedData.Members.Take(5));
}

public sealed class MembersPanel : AdminPanel
{
    public MembersPanel(ICommand segmentCommand)
    {
        Segments.Add(new SegmentItem("全部", segmentCommand));
        Segments.Add(new SegmentItem("管理员", segmentCommand));
        Segments.Add(new SegmentItem("编辑", segmentCommand));
        Segments.Add(new SegmentItem("访客", segmentCommand));
        Segments[0].IsSelected = true;
        PrimaryAction = "邀请成员";
    }

    public override string Title => "成员与权限";
    public override string Subtitle => "共 128 位成员 · 本周新增 6 人";
    public ObservableCollection<MemberRow> Members { get; } = new(SeedData.Members);
}

public sealed class OrdersPanel : AdminPanel
{
    public OrdersPanel(ICommand segmentCommand)
    {
        Segments.Add(new SegmentItem("全部", segmentCommand));
        Segments.Add(new SegmentItem("待支付", segmentCommand));
        Segments.Add(new SegmentItem("已支付", segmentCommand));
        Segments.Add(new SegmentItem("已退款", segmentCommand));
        Segments[1].IsSelected = true;
        PrimaryAction = "导出";
    }

    public override string Title => "订单管理";
    public override string Subtitle => "近 7 天共 3,214 笔订单 · 待处理 42 笔";
    public ObservableCollection<OrderRow> Orders { get; } = new(SeedData.Orders);
}

public sealed class AuditPanel : AdminPanel
{
    public AuditPanel(ICommand segmentCommand)
    {
        Segments.Add(new SegmentItem("全部", segmentCommand));
        Segments.Add(new SegmentItem("登录", segmentCommand));
        Segments.Add(new SegmentItem("成员变更", segmentCommand));
        Segments.Add(new SegmentItem("数据导出", segmentCommand));
        Segments[0].IsSelected = true;
        PrimaryAction = "导出日志";
    }

    public override string Title => "审计日志";
    public override string Subtitle => "记录成员的关键操作与登录行为，保留 90 天";
    public ObservableCollection<AuditRow> Rows { get; } = new(SeedData.Audit);
}

public sealed class ReportsPanel : AdminPanel
{
    public ReportsPanel(ICommand segmentCommand)
    {
        Segments.Add(new SegmentItem("今日", segmentCommand));
        Segments.Add(new SegmentItem("近 7 天", segmentCommand));
        Segments.Add(new SegmentItem("本月", segmentCommand));
        Segments[1].IsSelected = true;
        PrimaryAction = "生成报告";
    }

    public override string Title => "数据报表";
    public override string Subtitle => "核心业务指标与环比变化";

    public ObservableCollection<ReportCard> Cards { get; } = new()
    {
        new ReportCard { Title = "日活跃用户", Value = "12,486", Delta = "↑ 12.4% 较上一周期" },
        new ReportCard { Title = "订单转化率", Value = "4.82%", Delta = "↓ 0.8% 较上一周期", Up = false },
        new ReportCard { Title = "客单价", Value = "¥150.24", Delta = "↑ 3.1% 较上一周期" },
        new ReportCard { Title = "复购率", Value = "38.6%", Delta = "↑ 1.9% 较上一周期" }
    };
}

public sealed class ExportPanel : AdminPanel
{
    public ExportPanel()
    {
        PrimaryAction = "新建导出";
    }

    public override string Title => "导出记录";
    public override string Subtitle => "最近 30 天的数据导出历史";
    public ObservableCollection<ExportRow> Rows { get; } = new(SeedData.Exports);
}

public sealed class AdminViewModel : ObservableObject
{
    private object _activePanel;

    public AdminViewModel()
    {
        SelectPanelCommand = new RelayCommand(o =>
        {
            if (o is SubNavLinkEntry e) Select(e.Key);
        });
        SegmentCommand = new RelayCommand(_ => { });

        Panels = new Dictionary<string, AdminPanel>
        {
            ["overview"] = new OverviewPanel(SegmentCommand),
            ["members"] = new MembersPanel(SegmentCommand),
            ["orders"] = new OrdersPanel(SegmentCommand),
            ["audit"] = new AuditPanel(SegmentCommand),
            ["reports"] = new ReportsPanel(SegmentCommand),
            ["export"] = new ExportPanel()
        };

        _activePanel = Panels["overview"];
    }

    public ICommand SelectPanelCommand { get; }
    public ICommand SegmentCommand { get; }
    public Dictionary<string, AdminPanel> Panels { get; }

    public object ActivePanel
    {
        get => _activePanel;
        private set => Set(ref _activePanel, value);
    }

    public void Select(string key)
    {
        if (Panels.TryGetValue(key, out var panel)) ActivePanel = panel;
    }
}

internal static class SeedData
{
    public static readonly List<MemberRow> Members = new()
    {
        new MemberRow { Name = "陈墨", Email = "chenmo@aster.io", AvatarFrom = "#7A96FF", AvatarTo = "#5C7BF7", Role = "管理员", Status = "正常", StatusKind = "ok", LastActive = "2 分钟前", Department = "产品部" },
        new MemberRow { Name = "林晚", Email = "linwan@aster.io", AvatarFrom = "#B79CFF", AvatarTo = "#8C6BE0", Role = "编辑", Status = "正常", StatusKind = "ok", LastActive = "15 分钟前", Department = "运营部" },
        new MemberRow { Name = "苏晓", Email = "suxiao@aster.io", AvatarFrom = "#6FE3D8", AvatarTo = "#2FB6C9", Role = "访客", Status = "待审核", StatusKind = "wait", LastActive = "1 小时前", Department = "市场部" },
        new MemberRow { Name = "周野", Email = "zhouye@aster.io", AvatarFrom = "#8FD9A8", AvatarTo = "#4FB07A", Role = "编辑", Status = "正常", StatusKind = "ok", LastActive = "3 小时前", Department = "技术部" },
        new MemberRow { Name = "何遇", Email = "heyu@aster.io", AvatarFrom = "#D8A3AE", AvatarTo = "#B26A7C", Role = "访客", Status = "已停用", StatusKind = "off", LastActive = "昨天 18:30", Department = "市场部" }
    };

    public static readonly List<OrderRow> Orders = new()
    {
        new OrderRow { Id = "ORD-7108", Customer = "林晚", Amount = "¥1,280.00", Channel = "网页端", Status = "已支付", StatusKind = "ok", CreatedAt = "今天 14:12" },
        new OrderRow { Id = "ORD-7107", Customer = "苏晓", Amount = "¥560.00", Channel = "小程序", Status = "待支付", StatusKind = "wait", CreatedAt = "今天 13:48" },
        new OrderRow { Id = "ORD-7106", Customer = "周野", Amount = "¥3,420.00", Channel = "移动端", Status = "已支付", StatusKind = "ok", CreatedAt = "今天 13:20" },
        new OrderRow { Id = "ORD-7105", Customer = "何遇", Amount = "¥199.00", Channel = "网页端", Status = "已退款", StatusKind = "off", CreatedAt = "今天 12:05" },
        new OrderRow { Id = "ORD-7104", Customer = "陈墨", Amount = "¥890.00", Channel = "移动端", Status = "已支付", StatusKind = "ok", CreatedAt = "今天 11:32" }
    };

    public static readonly List<AuditRow> Audit = new()
    {
        new AuditRow { Time = "14:12", Text = "陈墨 将 林晚 的角色修改为 编辑" },
        new AuditRow { Time = "13:48", Text = "林晚 登录成功 · Windows 11 · 上海", Tone = "green" },
        new AuditRow { Time = "13:20", Text = "系统完成自动备份，耗时 1 分 24 秒", Tone = "cyan" },
        new AuditRow { Time = "12:05", Text = "陈墨 停用了成员 何遇", Tone = "red" },
        new AuditRow { Time = "11:32", Text = "苏晓 提交了加入申请，等待审核", Tone = "amber" },
        new AuditRow { Time = "09:15", Text = "陈墨 导出了成员名单 成员名单.xlsx" }
    };

    public static readonly List<ExportRow> Exports = new()
    {
        new ExportRow { File = "成员名单.xlsx", Owner = "陈墨", Time = "2026-10-20 14:02", Size = "2.1 MB" },
        new ExportRow { File = "订单明细.csv", Owner = "林晚", Time = "2026-10-19 18:20", Size = "860 KB" },
        new ExportRow { File = "月度报表.pdf", Owner = "陈墨", Time = "2026-10-18 09:15", Size = "4.7 MB" }
    };
}
