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
        Admin = new AdminViewModel();
        Settings = new SettingsViewModel(LogoutCommand);

        // 文档 / 音乐模块暂时关闭，只保留管理与设置
        RailItems = new ObservableCollection<RailItem>
        {
            new("admin", "管理", Controls.Icons.Bars, SelectModuleCommand),
            new("settings", "设置", Controls.Icons.Sliders, OpenSettingsCommand)
        };

        RailTop = new ObservableCollection<RailItem>(RailItems.Take(1));
        RailSettings = RailItems[1];

        SubNavEntries = new ObservableCollection<SubNavEntry>();
        SelectModule(RailItems[0], true);

        var bootTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2000) };
        bootTimer.Tick += (_, _) =>
        {
            bootTimer.Stop();
            if (Stage == AppStage.Boot) Stage = AppStage.Login;
        };
        bootTimer.Start();
    }

    public HomeViewModel Home { get; }
    public AdminViewModel Admin { get; }
    public SettingsViewModel Settings { get; }

    public ICommand SelectModuleCommand { get; }
    public ICommand OpenSettingsCommand { get; }
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

    public double SubNavWidth => _moduleKey == "admin" ? 260 : 184;
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
        OnPropertyChanged(nameof(SubNavWidth));
        OnPropertyChanged(nameof(IsRailAvatarVisible));

        switch (item.Key)
        {
            default:
                SubNavTitle = "工作区";
                ActiveView = Admin;
                AddSubNav("workbench", "工作台", Controls.Icons.Grid, "12");
                AddSubNav("clients", "客户工作空间", Controls.Icons.Users, null);
                AddSubNav("import", "信息导入中心", Controls.Icons.Download, null);
                AddSubNav("settlement", "基础结算", Controls.Icons.Currency, null);
                AddSubNav("approval", "协作与审批", Controls.Icons.List, null);
                AddSubNav("account", "账号与权限", Controls.Icons.Lock, null);
                SubNavEntries.Add(new SubNavGroupEntry("项目"));
                SubNavEntries.Add(new SubNavLinkEntry("project", "河南省中医院", Controls.Icons.Folder, SubNavCommand, null, true, true));
                SubNavEntries.Add(new SubNavGroupEntry("最近"));
                AddRecent("recent-1", "清远电子 首月结算");
                AddRecent("recent-2", "恒川制造 员工增减");
                AddRecent("recent-3", "远航科技 政策审核");
                SelectSubNav("workbench");
                break;
        }
    }

    private void AddRecent(string key, string title)
    {
        SubNavEntries.Add(new SubNavLinkEntry(key, title, Controls.Icons.Clock, SubNavCommand,
            null, showIcon: false, emphasized: false, height: 34));
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

        Admin.Select(key);
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
            SelectModule(RailItems[0], true);
        };
        timer.Start();
    }
}
