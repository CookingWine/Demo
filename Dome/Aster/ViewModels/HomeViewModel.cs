using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Aster.ViewModels;

public sealed class HomeViewModel : ObservableObject
{
    public string Greeting { get; } = "下午好，";
    public string UserName { get; } = "林深";
    public string Subtitle { get; } = "今天写了 3 篇文档，听了 42 分钟音乐 — 继续保持";

    public ObservableCollection<KpiItem> Kpis { get; } = new()
    {
        new KpiItem { Title = "文档总数", Value = "128", Delta = "↑ 12  本周新增", DeltaKind = "up" },
        new KpiItem { Title = "总写作时长", Value = "32", Unit = "h", Tail = "18m", Delta = "↑ 8.4%  较上周", DeltaKind = "up" },
        new KpiItem
        {
            Title = "待处理事项", Value = "12", Delta = "3 项逾期", DeltaKind = "warn",
            ValueBrushOverride = Palette.Solid(Palette.AmberColor)
        },
        new KpiItem { Title = "存储占用", Value = "6.8", Unit = "GB", Tail = "/ 20 GB", Delta = "34% 已使用", DeltaKind = "flat" }
    };

    public ObservableCollection<RecentDocItem> RecentDocs { get; } = new()
    {
        new RecentDocItem { Name = "Aster 架构设计", Meta = "Notes / 产品 · 2,148 字", Time = "3 分钟前" },
        new RecentDocItem { Name = "Q4 产品路线图评审", Meta = "Notes / 会议 · 1,032 字", Time = "1 小时前", Tone = "violet" },
        new RecentDocItem { Name = "2026-10-20 工作日志", Meta = "Journal / 日常 · 486 字", Time = "今天 09:12", Tone = "cyan" },
        new RecentDocItem { Name = "Avalonia 跨平台踩坑记录", Meta = "Notes / 技术 · 3,720 字", Time = "昨天" }
    };

    public ObservableCollection<QuickAction> QuickActions { get; } = new()
    {
        new QuickAction { Title = "新建 Markdown", Icon = Controls.Icons.Plus },
        new QuickAction { Title = "插入模板", Icon = Controls.Icons.DocText },
        new QuickAction { Title = "导入本地音乐", Icon = Controls.Icons.Music },
        new QuickAction { Title = "导出报告", Icon = Controls.Icons.Download }
    };

    public string NowPlayingTitle { get; } = "Midnight Architecture";
    public string NowPlayingArtist { get; } = "Aurora Field · Nocturne";
    public string NowPlayingPosition { get; } = "1:47";
    public string NowPlayingDuration { get; } = "4:12";
    public double NowPlayingProgress { get; } = 42;

    public IReadOnlyList<double> Rhythm { get; } = new double[]
    {
        60, 52, 56, 38, 44, 24, 30, 14, 26, 18, 34, 22, 30
    };
}
