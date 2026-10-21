using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Media;

namespace Aster.ViewModels;

public sealed class DocItem : SelectableItem
{
    public string Name { get; init; } = string.Empty;
    public string GroupName { get; init; } = string.Empty;
    public bool IsDirty { get; init; }
    public bool IsStarred { get; init; }
    public bool IsRecent { get; init; }
    public ICommand Command { get; init; } = null!;
    public string DisplayName => IsDirty ? Name : Name;
}

public sealed class DocGroup : ObservableObject
{
    private bool _isExpanded = true;

    public string Name { get; init; } = string.Empty;
    public int Count { get; init; }
    public string Tone { get; init; } = "accent";
    public ObservableCollection<DocItem> Items { get; } = new();
    public ICommand ToggleCommand { get; init; } = null!;

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

    public bool IsCollapsed => !_isExpanded;

    public string ChevronData => "m6 9 6 6 6-6";

    public ITransform ChevronTransform => new RotateTransform(IsExpanded ? 0 : -90);

    public IBrush FolderBrush => Tone switch
    {
        "violet" => Palette.Alpha(Palette.VioletColor, 0.9),
        "muted" => Palette.Alpha("#626A7D", 0.9),
        _ => Palette.Alpha(Palette.AccentColor, 0.9)
    };
}

public sealed class EditorTab : SelectableItem
{
    public string Name { get; init; } = string.Empty;
    public ICommand Command { get; init; } = null!;
    public ICommand CloseCommand { get; init; } = null!;
}

public sealed class MarkdownViewModel : ObservableObject
{
    private string _filter = "all";
    private string _search = string.Empty;
    private string _mode = "分栏";
    private DocItem? _current;
    private string _text = string.Empty;
    private bool _isDirty;
    private string _saveState = "已保存";

    public MarkdownViewModel()
    {
        FilterCommand = new RelayCommand(o => ApplyFilter(o as string ?? "all"));
        SelectGroupCommand = new RelayCommand(o =>
        {
            if (o is DocGroup g) g.IsExpanded = !g.IsExpanded;
        });
        SelectDocCommand = new RelayCommand(o =>
        {
            if (o is DocItem d) SelectDoc(d);
        });
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
        CloseTabCommand = new RelayCommand(o =>
        {
            if (o is EditorTab t) Tabs.Remove(t);
        });
        ModeCommand = new RelayCommand(o =>
        {
            if (o is SegmentItem s) SetMode(s.Label);
        });

        BuildDocs();
        Tabs.Add(new EditorTab { Name = "架构设计.md", Command = SelectTabCommand, CloseCommand = CloseTabCommand, IsSelected = true });
        Tabs.Add(new EditorTab { Name = "技术选型.md", Command = SelectTabCommand, CloseCommand = CloseTabCommand });
        Tabs.Add(new EditorTab { Name = "2026-10-20.md", Command = SelectTabCommand, CloseCommand = CloseTabCommand });
        Modes = new ObservableCollection<SegmentItem>
        {
            new("编辑", ModeCommand),
            new("分栏", ModeCommand),
            new("预览", ModeCommand)
        };
        Modes[1].IsSelected = true;

        RebuildTree();
        SelectDoc(AllDocs[0]);
    }

    public List<DocItem> AllDocs { get; } = new();
    public ObservableCollection<DocGroup> Groups { get; } = new();
    public ObservableCollection<EditorTab> Tabs { get; } = new();
    public ObservableCollection<SegmentItem> Modes { get; }

    public ICommand FilterCommand { get; }
    public ICommand SelectGroupCommand { get; }
    public ICommand SelectDocCommand { get; }
    public ICommand SelectTabCommand { get; }
    public ICommand CloseTabCommand { get; }
    public ICommand ModeCommand { get; }

    public string DocCountText => "128 篇文档";

    public string Filter
    {
        get => _filter;
        private set
        {
            if (Set(ref _filter, value)) RebuildTree();
        }
    }

    public string Search
    {
        get => _search;
        set
        {
            if (Set(ref _search, value)) RebuildTree();
        }
    }

    public string Mode => _mode;

    public bool ShowEditor => _mode != "预览";
    public bool ShowPreview => _mode != "编辑";
    public bool IsSplit => _mode == "分栏";
    public GridLength EditorWidth => _mode == "分栏" ? new GridLength(3, GridUnitType.Star) : new GridLength(1, GridUnitType.Star);
    public GridLength PreviewWidth => _mode == "分栏" ? new GridLength(2, GridUnitType.Star) : new GridLength(1, GridUnitType.Star);

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

    public string CurrentName => _current?.Name ?? "未打开文档";
    public string CurrentGroup => _current?.GroupName ?? "Notes";

    public string Text
    {
        get => _text;
        set
        {
            if (!Set(ref _text, value)) return;
            LineNumbers = string.Join("\n", Enumerable.Range(1, Math.Max(1, value.Count(c => c == '\n') + 1)));
            IsDirty = true;
            SaveState = "未保存";
        }
    }

    private string _lineNumbers = "1";

    public string LineNumbers
    {
        get => _lineNumbers;
        private set => Set(ref _lineNumbers, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set => Set(ref _isDirty, value);
    }

    public string SaveState
    {
        get => _saveState;
        private set => Set(ref _saveState, value);
    }

    private void BuildDocs()
    {
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

    private void RebuildTree()
    {
        Groups.Clear();
        var groups = new[] { "Notes", "Journal", "Drafts", "Archive" };
        var tones = new[] { "accent", "violet", "muted", "muted" };
        var counts = new[] { 6, 3, 4, 17 };

        for (var i = 0; i < groups.Length; i++)
        {
            var name = groups[i];
            IEnumerable<DocItem> items = AllDocs.Where(d => d.GroupName == name);
            items = _filter switch
            {
                "recent" => items.Where(d => d.IsRecent),
                "star" => items.Where(d => d.IsStarred),
                "dirty" => items.Where(d => d.IsDirty),
                "trash" => Enumerable.Empty<DocItem>(),
                _ => items
            };
            if (!string.IsNullOrWhiteSpace(_search))
                items = items.Where(d => d.Name.Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase));

            var list = items.ToList();
            if (_filter != "all" && list.Count == 0) continue;

            var group = new DocGroup
            {
                Name = name,
                Count = _filter == "all" ? counts[i] : list.Count,
                Tone = tones[i],
                ToggleCommand = SelectGroupCommand
            };
            foreach (var item in list) group.Items.Add(item);
            Groups.Add(group);
        }
    }

    private void ApplyFilter(string filter)
    {
        _filter = filter;
        OnPropertyChanged(nameof(Filter));
        RebuildTree();
    }

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

    private void SelectDoc(DocItem doc)
    {
        foreach (var d in AllDocs) d.IsSelected = ReferenceEquals(d, doc);
        Current = doc;
        Text = Contents.TryGetValue(doc.Name, out var md) ? md : $"# {System.IO.Path.GetFileNameWithoutExtension(doc.Name)}\n\n开始书写…\n";
        _isDirty = false;
        OnPropertyChanged(nameof(IsDirty));
        SaveState = doc.IsDirty ? "未保存" : "已保存";

        if (!Tabs.Any(t => t.Name == doc.Name))
        {
            Tabs.Add(new EditorTab
            {
                Name = doc.Name,
                Command = SelectTabCommand,
                CloseCommand = CloseTabCommand
            });
        }

        foreach (var t in Tabs) t.IsSelected = t.Name == doc.Name;
    }

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
