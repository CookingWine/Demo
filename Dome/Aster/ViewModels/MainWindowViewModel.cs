using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Avalonia.Threading;

namespace Aster.ViewModels;

public enum AppStage
{
    Boot,
    Login,
    App
}

public enum DialogKind
{
    None,
    ForgotStep1,
    ForgotStep2,
    ContactStep1,
    ContactStep2
}

public sealed class MainWindowViewModel : ObservableObject
{
    private AppStage _stage = AppStage.Boot;
    private string _moduleKey = "admin";
    private string _subNavTitle = "管理";
    private object? _activeView;
    private bool _isSettingsOpen;
    private DialogKind _dialog = DialogKind.None;
    private string _userName = string.Empty;
    private string _password = string.Empty;
    private bool _remember = true;
    private bool _showPassword;
    private bool _isBusy;

    public MainWindowViewModel()
    {
        SelectModuleCommand = new RelayCommand(o =>
        {
            if (o is RailItem item) SelectModule(item);
        });
        OpenSettingsCommand = new RelayCommand(() => IsSettingsOpen = true);
        ShowHomeCommand = new RelayCommand(ShowHome);
        CloseSettingsCommand = new RelayCommand(() => IsSettingsOpen = false);
        LoginCommand = new RelayCommand(Login, () => !IsBusy);
        LogoutCommand = new RelayCommand(() =>
        {
            IsSettingsOpen = false;
            Stage = AppStage.Login;
        });
        TogglePasswordCommand = new RelayCommand(() => ShowPassword = !ShowPassword);
        OpenForgotCommand = new RelayCommand(() => Dialog = DialogKind.ForgotStep1);
        OpenContactCommand = new RelayCommand(() => Dialog = DialogKind.ContactStep1);
        CloseDialogCommand = new RelayCommand(() => Dialog = DialogKind.None);
        SendResetCommand = new RelayCommand(() => Dialog = DialogKind.ForgotStep2);
        SendMailCommand = new RelayCommand(() => Dialog = DialogKind.ContactStep2);
        CopyAdminCommand = new RelayCommand(() => IsAdminCopied = true);

        Home = new HomeViewModel();
        Markdown = new MarkdownViewModel();
        MusicResult = new MusicViewModel();
        Admin = new AdminViewModel();
        Settings = new SettingsViewModel(LogoutCommand);

        RailItems = new ObservableCollection<RailItem>
        {
            new("md", "文档", Controls.Icons.DocText, SelectModuleCommand),
            new("music", "音乐", Controls.Icons.Music, SelectModuleCommand),
            new("admin", "管理", Controls.Icons.Bars, SelectModuleCommand),
            new("settings", "设置", Controls.Icons.Sliders, OpenSettingsCommand)
        };

        RailTop = new ObservableCollection<RailItem>(RailItems.Take(3));
        RailSettings = RailItems[3];

        SubNavEntries = new ObservableCollection<SubNavEntry>();
        SubNavTitle = "工作台";
        ActiveView = Home;

        var bootTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2000) };
        bootTimer.Tick += (_, _) =>
        {
            bootTimer.Stop();
            if (Stage == AppStage.Boot) Stage = AppStage.Login;
        };
        bootTimer.Start();
    }

    public HomeViewModel Home { get; }
    public MarkdownViewModel Markdown { get; }
    public MusicViewModel MusicResult { get; }
    public AdminViewModel Admin { get; }
    public SettingsViewModel Settings { get; }

    public ICommand SelectModuleCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand ShowHomeCommand { get; }
    public ICommand CloseSettingsCommand { get; }
    public ICommand LoginCommand { get; }
    public ICommand LogoutCommand { get; }
    public ICommand TogglePasswordCommand { get; }
    public ICommand OpenForgotCommand { get; }
    public ICommand OpenContactCommand { get; }
    public ICommand CloseDialogCommand { get; }
    public ICommand SendResetCommand { get; }
    public ICommand SendMailCommand { get; }
    public ICommand CopyAdminCommand { get; }

    public ObservableCollection<RailItem> RailItems { get; }
    public ObservableCollection<RailItem> RailTop { get; }
    public RailItem RailSettings { get; }
    public ObservableCollection<SubNavEntry> SubNavEntries { get; }

    public AppStage Stage
    {
        get => _stage;
        private set
        {
            if (!Set(ref _stage, value)) return;
            OnPropertyChanged(nameof(IsBoot));
            OnPropertyChanged(nameof(IsLogin));
            OnPropertyChanged(nameof(IsApp));
        }
    }

    public bool IsBoot => _stage == AppStage.Boot;
    public bool IsLogin => _stage == AppStage.Login;
    public bool IsApp => _stage == AppStage.App;

    public string SubNavTitle
    {
        get => _subNavTitle;
        private set => Set(ref _subNavTitle, value);
    }

    public bool IsSubNavUserVisible => _moduleKey == "admin";
    public bool IsRailAvatarVisible => !IsSubNavUserVisible;
    public string SubNavUserInitial => "张";
    public string SubNavUserName => "张客服";
    public string SubNavUserRole => "客服";

    private bool _isSubNavVisible = true;

    public bool IsSubNavVisible
    {
        get => _isSubNavVisible;
        private set => Set(ref _isSubNavVisible, value);
    }

    /// <summary>回到工作台首页。</summary>
    private void ShowHome()
    {
        foreach (var r in RailItems) r.IsSelected = false;
        _moduleKey = "home";
        SubNavEntries.Clear();
        SubNavTitle = "工作台";
        IsSubNavVisible = false;
        ActiveView = Home;
    }

    public object? ActiveView
    {
        get => _activeView;
        private set => Set(ref _activeView, value);
    }

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set => Set(ref _isSettingsOpen, value);
    }

    // ===================== 登录 =====================
    public string UserName
    {
        get => _userName;
        set => Set(ref _userName, value);
    }

    public string Password
    {
        get => _password;
        set => Set(ref _password, value);
    }

    public bool Remember
    {
        get => _remember;
        set => Set(ref _remember, value);
    }

    public bool ShowPassword
    {
        get => _showPassword;
        set
        {
            if (!Set(ref _showPassword, value)) return;
            OnPropertyChanged(nameof(PasswordChar));
            OnPropertyChanged(nameof(PasswordRevealChar));
        }
    }

    public char PasswordChar => ShowPassword ? '\0' : '●';
    public char PasswordRevealChar => ShowPassword ? '\0' : '●';

    public bool IsBusy
    {
        get => _isBusy;
        private set => Set(ref _isBusy, value);
    }

    public string LoginError { get; private set; } = string.Empty;

    // ===================== 弹窗 =====================
    public DialogKind Dialog
    {
        get => _dialog;
        private set
        {
            if (!Set(ref _dialog, value)) return;
            OnPropertyChanged(nameof(IsDialogOpen));
            OnPropertyChanged(nameof(IsForgotDialog));
            OnPropertyChanged(nameof(IsContactDialog));
            OnPropertyChanged(nameof(IsForgotStep1));
            OnPropertyChanged(nameof(IsForgotStep2));
            OnPropertyChanged(nameof(IsContactStep1));
            OnPropertyChanged(nameof(IsContactStep2));
            if (value == DialogKind.None) IsAdminCopied = false;
        }
    }

    public bool IsDialogOpen => _dialog != DialogKind.None;
    public bool IsForgotDialog => _dialog is DialogKind.ForgotStep1 or DialogKind.ForgotStep2;
    public bool IsContactDialog => _dialog is DialogKind.ContactStep1 or DialogKind.ContactStep2;
    public bool IsForgotStep1 => _dialog == DialogKind.ForgotStep1;
    public bool IsForgotStep2 => _dialog == DialogKind.ForgotStep2;
    public bool IsContactStep1 => _dialog == DialogKind.ContactStep1;
    public bool IsContactStep2 => _dialog == DialogKind.ContactStep2;

    public string ForgotEmail
    {
        get => UserName;
        set => UserName = value;
    }

    private bool _isAdminCopied;
    public bool IsAdminCopied
    {
        get => _isAdminCopied;
        private set
        {
            if (!Set(ref _isAdminCopied, value)) return;
            OnPropertyChanged(nameof(AdminCopyText));
        }
    }

    public string AdminCopyText => IsAdminCopied ? "已复制" : "复制";

    // ===================== 模块切换 =====================
    private void SelectModule(RailItem item)
    {
        if (item.IsSettings)
        {
            IsSettingsOpen = true;
            return;
        }
        SelectModule(item, true);
    }

    private void SelectModule(RailItem item, bool raise)
    {
        if (!raise && item is null) return;
        _moduleKey = item.Key;
        foreach (var r in RailItems) r.IsSelected = ReferenceEquals(r, item);

        SubNavEntries.Clear();

        IsSubNavVisible = true;
        OnPropertyChanged(nameof(IsSubNavUserVisible));
        OnPropertyChanged(nameof(IsRailAvatarVisible));

        switch (item.Key)
        {
            case "md":
                SubNavTitle = "文档";
                ActiveView = Markdown;
                AddSubNav("all", "全部文档", Controls.Icons.Folder, "128");
                AddSubNav("recent", "最近编辑", Controls.Icons.Clock, "5");
                AddSubNav("star", "收藏夹", Controls.Icons.Star, "12");
                AddSubNav("dirty", "未保存", Controls.Icons.Edit, "2");
                SubNavEntries.Add(new SubNavGroupEntry("回收"));
                AddSubNav("trash", "回收站", Controls.Icons.Trash, "0");
                SelectSubNav("all");
                break;

            case "music":
                SubNavTitle = "音乐";
                ActiveView = MusicResult;
                AddSubNav("m-all", "全部音乐", Controls.Icons.Music, "248");
                AddSubNav("m-fav", "我喜欢的", Controls.Icons.Heart, "32");
                AddSubNav("m-recent", "最近播放", Controls.Icons.Clock, "18");
                AddSubNav("m-local", "本地文件", Controls.Icons.Hdd, "96");
                SubNavEntries.Add(new SubNavGroupEntry("歌单"));
                AddSubNav("pl-focus", "Focus Flow", Controls.Icons.List, "24");
                AddSubNav("pl-night", "深夜编码", Controls.Icons.List, "41");
                AddSubNav("pl-commute", "通勤电台", Controls.Icons.List, "12");
                SelectSubNav("m-all");
                break;

            default:
                SubNavTitle = "工作区";
                ActiveView = Admin;
                AddSubNav("workbench", "工作台", Controls.Icons.Grid, "12");
                AddSubNav("clients", "客户工作空间", Controls.Icons.Users, null);
                AddSubNav("import", "信息导入中心", Controls.Icons.Download, null);
                AddSubNav("settlement", "基础结算", Controls.Icons.Currency, null);
                AddSubNav("approval", "协作与审批", Controls.Icons.List, null);
                SubNavEntries.Add(new SubNavGroupEntry("项目"));
                AddSubNav("project", "河南省中医院", Controls.Icons.Folder, null);
                SubNavEntries.Add(new SubNavGroupEntry("最近"));
                AddSubNav("recent-1", "清远电子 首月结算", Controls.Icons.Clock, null);
                AddSubNav("recent-2", "恒川制造 员工增减", Controls.Icons.Clock, null);
                AddSubNav("recent-3", "远航科技 政策审核", Controls.Icons.Clock, null);
                SelectSubNav("workbench");
                break;
        }
    }

    private void AddSubNav(string key, string title, string icon, string? badge)
    {
        SubNavEntries.Add(new SubNavLinkEntry(key, title, icon, SubNavCommand, badge));
    }

    private ICommand? _subNavCommand;

    private ICommand SubNavCommand => _subNavCommand ??= new RelayCommand(o =>
    {
        if (o is SubNavLinkEntry entry) SelectSubNav(entry.Key);
    });

    private void SelectSubNav(string key)
    {
        foreach (var entry in SubNavEntries.OfType<SubNavLinkEntry>())
            entry.IsSelected = entry.Key == key;

        switch (_moduleKey)
        {
            case "md":
                Markdown.FilterCommand.Execute(key);
                break;
            case "music":
                MusicResult.FilterCommand.Execute(key switch
                {
                    "m-fav" => "收藏",
                    "m-recent" => "最近",
                    "m-local" => "专辑",
                    "pl-focus" => "全部",
                    "pl-night" => "全部",
                    "pl-commute" => "全部",
                    _ => "全部"
                });
                break;
            default:
                Admin.Select(key);
                break;
        }
    }

    // ===================== 登录动作 =====================
    private void Login()
    {
        if (string.IsNullOrWhiteSpace(UserName) || string.IsNullOrWhiteSpace(Password))
        {
            LoginError = "请输入账号与密码";
            OnPropertyChanged(nameof(LoginError));
            return;
        }

        LoginError = string.Empty;
        OnPropertyChanged(nameof(LoginError));
        IsBusy = true;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            IsBusy = false;
            Stage = AppStage.App;
            ShowHome();
        };
        timer.Start();
    }
}
