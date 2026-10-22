// ============================================================================
// MainWindowViewModel.cs —— 应用壳（Shell）的唯一根视图模型
// 1) Stage（AppStage）三态驱动 BootView / LoginView / ShellView 的显隐；
// 2) 一级导航 RailItem + 二级导航 SubNavEntry，ActiveView 交给 DataTemplate 按类型渲染；
// 3) IsSettingsOpen 控制设置浮层，DialogKind 驱动登录页「忘记密码 / 联系管理员」两个两步弹窗。
// 协作：HomeViewModel / AdminViewModel / SettingsViewModel 都是它的子 VM，在此统一 new 出来。
// 坑：Stage 与 Dialog 都是「一个枚举 + 若干派生 bool」，换值后必须手动连带通知派生属性。
// ============================================================================

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Avalonia.Threading;

namespace Aster.ViewModels;

// ---------- 枚举：壳阶段与弹窗状态 ----------
/// <summary>
/// 应用的三个顶层阶段：Boot（启动动画）→ Login（登录页）→ App（主壳）。
/// 由 MainWindowViewModel.Stage 持有，视图侧用 IsBoot/IsLogin/IsApp 三个派生 bool 绑定显隐。
/// </summary>
public enum AppStage
{
    Boot,
    Login,
    App
}

/// <summary>
/// 登录页弹窗的唯一状态源：None 表示无弹窗，其余四个值对应「两个弹窗 × 两个步骤」。
/// 用单个枚举而不是两套 bool，是为了保证「同一时刻只可能有一个弹窗 / 一步」天然成立，
/// 避免 IsForgotOpen 与 IsContactOpen 同时为 true 这种非法组合。
/// </summary>
public enum DialogKind
{
    None,
    ForgotStep1,
    ForgotStep2,
    ContactStep1,
    ContactStep2
}

/// <summary>
/// 应用壳 VM：集中持有「阶段 + 导航 + 设置浮层 + 登录表单 + 弹窗」五类状态，是全工程唯一的全局状态源。
/// MainWindow.axaml 直接以它为 DataContext；BootView / LoginView / ShellView 的显隐由阶段派生 bool 控制。
/// </summary>
public sealed class MainWindowViewModel : ObservableObject
{
    // ---------- 后备字段 ----------
    // 默认停在 Boot：首屏先播启动动画，2 秒后由构造函数里的 DispatcherTimer 推进到 Login。
    private AppStage _stage = AppStage.Boot;
    // 当前一级模块 key。目前只有 admin 一个业务模块，SubNavWidth 等联动属性都读它。
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

    /// <summary>
    /// 构造函数里按顺序做四件事：建命令 → 建子 VM → 初始化导航集合 → 启动 2 秒启动计时器。
    /// 所有命令都只在此处创建一次并暴露为只读属性，XAML 侧只负责 {Binding XxxCommand}。
    /// </summary>
    public MainWindowViewModel()
    {
        // 一级导航命令：由 DataTemplate 通过 CommandParameter 把被点的 RailItem 传进来。
        SelectModuleCommand = new RelayCommand(o =>
        {
            if (o is RailItem item) SelectModule(item);
        });
        OpenSettingsCommand = new RelayCommand(() => IsSettingsOpen = true);
        CloseSettingsCommand = new RelayCommand(() => IsSettingsOpen = false);
        // canExecute 传 !IsBusy，意图是「请求进行中禁止重复提交」。
        // 但要清楚它目前只是声明了意图：RelayCommand.CanExecuteChanged 从不触发，
        // 而 Button 仅在挂上 Command 时查询一次 CanExecute，所以登录那 420ms 里按钮不会真的变灰。
        // 想让它生效，需要在 IsBusy 的 setter 里调用 ((RelayCommand)LoginCommand).RaiseCanExecuteChanged()。
        LoginCommand = new RelayCommand(Login, () => !IsBusy);
        // 退出登录：先关掉设置浮层，否则回到登录页后浮层还会压在上面。
        LogoutCommand = new RelayCommand(() =>
        {
            IsSettingsOpen = false;
            Stage = AppStage.Login;
        });
        TogglePasswordCommand = new RelayCommand(() => ShowPassword = !ShowPassword);
        // 登录页两个弹窗的「打开 / 下一步 / 关闭」全部只是改 Dialog 枚举值，没有额外的可见性字段。
        OpenForgotCommand = new RelayCommand(() => Dialog = DialogKind.ForgotStep1);
        OpenContactCommand = new RelayCommand(() => Dialog = DialogKind.ContactStep1);
        CloseDialogCommand = new RelayCommand(() => Dialog = DialogKind.None);
        SendResetCommand = new RelayCommand(() => Dialog = DialogKind.ForgotStep2);
        SendMailCommand = new RelayCommand(() => Dialog = DialogKind.ContactStep2);
        // 复制按钮是纯演示：不碰剪贴板，只把文案切成「已复制」，关闭弹窗时由 Dialog setter 复位。
        CopyAdminCommand = new RelayCommand(() => IsAdminCopied = true);

        // 子 VM 在壳里只创建一次，切换模块 / 阶段时复用，避免反复 new 导致状态丢失。
        Home = new HomeViewModel();
        Admin = new AdminViewModel();
        Settings = new SettingsViewModel(LogoutCommand);

        // 工作区四个一级入口 + 设置，对齐 HR Agent 原型的侧栏结构
        RailItems = new ObservableCollection<RailItem>
        {
            new("workbench", "工作台", Controls.Icons.Grid, SelectModuleCommand),
            new("approval", "审批", Controls.Icons.List, SelectModuleCommand),
            new("clients", "客户", Controls.Icons.Users, SelectModuleCommand),
            new("workflow", "Workflow", Controls.Icons.Trend, SelectModuleCommand),
            new("settings", "设置", Controls.Icons.Sliders, OpenSettingsCommand)
        };

        // RailTop 与 RailSettings 是同一批数据的两种取法：上方渲染 RailTop，底部单独摆设置入口。
        RailTop = new ObservableCollection<RailItem>(RailItems.Take(4));
        RailSettings = RailItems[4];

        // 二级导航集合只建一次，之后每次切模块都是 Clear 再重新填充（见 SelectModule）。
        SubNavEntries = new ObservableCollection<SubNavEntry>();
        // 传 true 显式跑一次切换，保证首屏就有二级菜单和默认选中项。
        SelectModule(RailItems[0], true);

        // 用 DispatcherTimer 模拟「启动耗时」：不阻塞 UI 线程，2 秒后在 UI 线程回调里推进阶段。
        var bootTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2000) };
        bootTimer.Tick += (_, _) =>
        {
            bootTimer.Stop();
            // 仅当仍停在 Boot 才推进，避免期间有别的逻辑已经切走时被覆盖。
            if (Stage == AppStage.Boot) Stage = AppStage.Login;
        };
        bootTimer.Start();
    }

    // ===================== 子 VM / 命令 / 导航集合 =====================
    // 三个子 VM 常驻且只读：壳只决定「显示哪一个」，不重建实例，因此子 VM 内部状态在切换时得以保留。
    public HomeViewModel Home { get; }
    public AdminViewModel Admin { get; }
    public SettingsViewModel Settings { get; }

    // 命令按用途分为三组：导航（SelectModule / Open / CloseSettings）、
    // 登录（Login / Logout / TogglePassword）、弹窗（Open* / Send* / CloseDialog / CopyAdmin）。
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

    // RailItems 是全集；RailTop 取前面业务项渲染在 rail 上方，RailSettings 单独钉在 rail 底部。
    public ObservableCollection<RailItem> RailItems { get; }
    public ObservableCollection<RailItem> RailTop { get; }
    public RailItem RailSettings { get; }
    // 二级导航把分组标题和链接混在同一个集合里，模板靠 DataTemplate 按类型分别渲染。
    public ObservableCollection<SubNavEntry> SubNavEntries { get; }

    /// <summary>
    /// 当前阶段。setter 私有，只允许壳内部由导航 / 登录动作修改。
    /// 关键在于手写的三条连带通知：视图绑定的是 IsBoot / IsLogin / IsApp 而不是 Stage 本身，
    /// 不在这里补发的话，阶段变了界面却不会有任何反应。
    /// </summary>
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

    // ===================== 二级导航布局联动 =====================
    public string SubNavTitle
    {
        get => _subNavTitle;
        private set => Set(ref _subNavTitle, value);
    }

    /// <summary>
    /// 二级栏宽度：现在只剩工作区一个模块（四个页面共用同一条侧栏），固定 260 保证项目/会话/用户卡片放得下。
    /// 注意它是计算属性，_moduleKey 改动后必须手动 OnPropertyChanged（在 SelectModule 里做）。
    /// </summary>
    public double SubNavWidth => 260;

    /// <summary>二级栏底部始终展示「当前用户」卡片。</summary>
    public bool IsSubNavUserVisible => true;

    /// <summary>
    /// 与 IsSubNavUserVisible 恰好互斥：二级栏里已有用户卡片时，rail 底部不再重复显示头像；
    /// 反之（窄二级栏）才把头像放回 rail 底部。三者必须同进同退，因此通知也集中在 SelectModule 里发。
    /// </summary>
    public bool IsRailAvatarVisible => !IsSubNavUserVisible;

    // 演示用的固定用户信息；真实项目会来自登录结果或用户接口。
    public string SubNavUserInitial => "张";
    public string SubNavUserName => "张客服";
    public string SubNavUserRole => "客服";

    private bool _isSubNavVisible = true;

    /// <summary>二级栏整体显隐（当前逻辑里切模块时会被置为 true）。</summary>
    public bool IsSubNavVisible
    {
        get => _isSubNavVisible;
        private set => Set(ref _isSubNavVisible, value);
    }

    /// <summary>
    /// 主内容区当前显示的 ViewModel。ShellView 里用 ContentControl + DataTemplate 按类型匹配渲染，
    /// 所以这里放的是「对象」而不是控件，切换模块只需换实例。
    /// </summary>
    public object? ActiveView
    {
        get => _activeView;
        private set => Set(ref _activeView, value);
    }

    /// <summary>设置浮层开关。和登录弹窗是两套东西：这个属于 App 阶段的壳内浮层，setter 是 public。</summary>
    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set => Set(ref _isSettingsOpen, value);
    }

    // ===================== 登录 =====================
    /// <summary>账号，与登录页 TextBox 双向绑定；忘记密码弹窗里的「账号邮箱」也复用这份数据。</summary>
    public string UserName
    {
        get => _userName;
        set => Set(ref _userName, value);
    }

    /// <summary>密码明文存放（演示用）。视图侧靠 PasswordChar 遮罩，不在这里加密。</summary>
    public string Password
    {
        get => _password;
        set => Set(ref _password, value);
    }

    /// <summary>「记住我」勾选态，目前只是绑定占位，未做持久化。</summary>
    public bool Remember
    {
        get => _remember;
        set => Set(ref _remember, value);
    }

    /// <summary>
    /// 密码是否明文显示，由眼睛按钮的 TogglePasswordCommand 翻转。
    /// 因为 PasswordChar / PasswordRevealChar 是计算属性，这里必须在 Set 返回 true 后手动补两条通知。
    /// </summary>
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

    /// <summary>
    /// 遮罩字符：明文时不遮罩（'\0' 表示不绘制遮罩字符），否则用实心圆点。
    /// 现状：LoginView 里 PasswordChar 是硬编码 "●"、显隐交给 TextBox.RevealPassword 绑定，
    /// 这两个属性目前没被 XAML 使用，保留作「遮罩字符也由 VM 决定」的备选写法。
    /// </summary>
    public char PasswordChar => ShowPassword ? '\0' : '●';
    public char PasswordRevealChar => ShowPassword ? '\0' : '●';

    /// <summary>登录请求进行中。LoginCommand 的 CanExecute 依赖它，置 true 即自动禁用登录按钮。</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => Set(ref _isBusy, value);
    }

    /// <summary>
    /// 登录错误文案。它是普通自动属性（没有后备字段的 Set 调用），
    /// 因此 Login() 里每次赋值之后都必须手动 OnPropertyChanged，否则界面不会刷新。
    /// </summary>
    public string LoginError { get; private set; } = string.Empty;

    // ===================== 弹窗 =====================
    /// <summary>
    /// 登录页弹窗的唯一状态源。用「一个枚举 + 多个派生 bool」而不是四五个独立 bool，
    /// 好处有两点：一是「同一时刻只有一个弹窗、一步」在类型层面就成立，不会出现忘记互斥的非法组合；
    /// 二是复位只需一个地方（设为 None），顺带能清掉复制按钮的临时状态。
    /// 代价是换值时必须在 setter 里把全部派生属性通知一遍，漏一个就会「状态变了界面不变」。
    /// </summary>
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
            // 关闭弹窗时顺手复位「已复制」文案，下次打开不会残留上一次的状态。
            if (value == DialogKind.None) IsAdminCopied = false;
        }
    }

    // 以下派生属性分别对应：遮罩层是否有弹窗、是哪个弹窗、当前停在哪一步。
    // 模板把它们直接绑到 Panel / StackPanel 的 IsVisible 上，做到「同一份结构，按步骤显示不同内容」。
    public bool IsDialogOpen => _dialog != DialogKind.None;
    public bool IsForgotDialog => _dialog is DialogKind.ForgotStep1 or DialogKind.ForgotStep2;
    public bool IsContactDialog => _dialog is DialogKind.ContactStep1 or DialogKind.ContactStep2;
    public bool IsForgotStep1 => _dialog == DialogKind.ForgotStep1;
    public bool IsForgotStep2 => _dialog == DialogKind.ForgotStep2;
    public bool IsContactStep1 => _dialog == DialogKind.ContactStep1;
    public bool IsContactStep2 => _dialog == DialogKind.ContactStep2;

    /// <summary>
    /// 忘记密码弹窗里的邮箱输入。它是 UserName 的别名而不是独立字段，
    /// 所以登录页已填过的账号会自动带进弹窗，改弹窗里的值也会同步回登录框。
    /// </summary>
    public string ForgotEmail
    {
        get => UserName;
        set => UserName = value;
    }

    private bool _isAdminCopied;

    /// <summary>「复制管理员邮箱」的临时状态（演示用，不真的写剪贴板）。</summary>
    public bool IsAdminCopied
    {
        get => _isAdminCopied;
        private set
        {
            if (!Set(ref _isAdminCopied, value)) return;
            // 按钮文字由 AdminCopyText 计算得出，状态一变就要连带通知。
            OnPropertyChanged(nameof(AdminCopyText));
        }
    }

    public string AdminCopyText => IsAdminCopied ? "已复制" : "复制";

    // ===================== 模块切换 =====================
    /// <summary>
    /// 单参重载：面向「用户点击一级导航」的场景。先拦截「设置」——它不是模块，
    /// 只是把 IsSettingsOpen 打开，因此不进入下面的切换逻辑；其余模块转调双参重载。
    /// </summary>
    private void SelectModule(RailItem item)
    {
        if (item.IsSettings)
        {
            IsSettingsOpen = true;
            return;
        }
        SelectModule(item, true);
    }

    /// <summary>
    /// 双参重载：真正执行模块切换。两个重载的区别在于「入口职责」——
    /// 单参版本负责 settings 特判，双参版本只做切换本身，便于构造函数等内部场景直接调用。
    /// raise 参数名义上表示「是否允许推进切换」，实现里只用于下面的空值守卫；
    /// 目前两处调用都传 true，所以该守卫实际不会命中，属于预留参数。
    /// </summary>
    private void SelectModule(RailItem item, bool raise)
    {
        if (!raise && item is null) return;
        _moduleKey = item.Key;
        // 用引用比较而不是 key 比较，保证「只有被点的那一项」被选中，避免 key 重复时误选。
        foreach (var r in RailItems) r.IsSelected = ReferenceEquals(r, item);

        // 二级导航整组重建：先清空再按模块填充，比逐项对比更新简单得多。
        SubNavEntries.Clear();

        // 下面三个都是读 _moduleKey 的计算属性，改完 key 必须挨个手动通知，否则宽度/头像不会变。
        IsSubNavVisible = true;
        OnPropertyChanged(nameof(IsSubNavUserVisible));
        OnPropertyChanged(nameof(SubNavWidth));
        OnPropertyChanged(nameof(IsRailAvatarVisible));

        switch (item.Key)
        {
            default:
                SubNavTitle = "工作区";
                // ActiveView 换成 AdminViewModel，ShellView 的 DataTemplate 会按类型匹配到 AdminView。
                ActiveView = Admin;
                // 四个页面入口与一级导航一一对应，点击侧栏与点击 rail 的效果一致。
                AddSubNav("workbench", "工作台", Controls.Icons.Grid, "12");
                AddSubNav("approval", "协作与审批", Controls.Icons.List, null);
                AddSubNav("clients", "客户工作空间", Controls.Icons.Users, null);
                AddSubNav("workflow", "Workflow", Controls.Icons.Trend, null);
                // 分组标题与链接混装：模板用 DataTemplate 区分渲染，分组不可点击。
                SubNavEntries.Add(new SubNavGroupEntry("项目"));
                // emphasized 传 true，让当前项目以更大的字号字重突出显示。
                SubNavEntries.Add(new SubNavLinkEntry("customer-qinghe", "青禾 · 月度核对",
                    Controls.Icons.Folder, SubNavCommand, null, true, true));
                AddRecent("thread-tax", "8 月个税交叉验证");
                SubNavEntries.Add(new SubNavLinkEntry("thread-new", "＋ 项目内新会话",
                    Controls.Icons.Plus, SubNavCommand, null, showIcon: false, emphasized: false, height: 34));
                SubNavEntries.Add(new SubNavGroupEntry("最近会话"));
                AddRecent("recent-1", "新的工作会话");
                AddRecent("recent-2", "新的工作会话");
                AddRecent("recent-3", "新的工作会话");
                AddRecent("recent-tax", "8 月个税交叉验证");
                // 重建完成后恢复选中态：与被点击的一级导航保持一致。
                SelectSubNav(item.Key);
                break;
        }
    }

    /// <summary>「最近」条目的快捷构造：不显示图标、行高更矮（34），视觉上弱于常规入口。</summary>
    private void AddRecent(string key, string title)
    {
        SubNavEntries.Add(new SubNavLinkEntry(key, title, Controls.Icons.Clock, SubNavCommand,
            null, showIcon: false, emphasized: false, height: 34));
    }

    /// <summary>常规二级入口的快捷构造，只暴露业务关心的四个参数。</summary>
    private void AddSubNav(string key, string title, string icon, string? badge)
    {
        SubNavEntries.Add(new SubNavLinkEntry(key, title, icon, SubNavCommand, badge));
    }

    private ICommand? _subNavCommand;

    /// <summary>
    /// 二级导航共用一个命令（惰性创建，??= 只建一次）：靠 CommandParameter 传回被点的条目。
    /// 不在构造函数里建，是为了避免在字段初始化阶段捕获尚未赋值的成员。
    /// </summary>
    private ICommand SubNavCommand => _subNavCommand ??= new RelayCommand(o =>
    {
        if (o is SubNavLinkEntry entry) SelectSubNav(entry.Key);
    });

    /// <summary>
    /// 同步二级导航的选中态并切换内容。
    /// 只遍历 SubNavLinkEntry：分组标题（SubNavGroupEntry）没有 Key，也不该被选中。
    /// </summary>
    private void SelectSubNav(string key)
    {
        foreach (var entry in SubNavEntries.OfType<SubNavLinkEntry>())
            entry.IsSelected = entry.Key == key;

        SyncRailSelection(key);

        // 具体内容由 AdminViewModel 自己按 key 切换，壳层不参与。
        Admin.Select(key);
    }

    /// <summary>让一级 rail 的高亮跟随当前页面：点侧栏入口或「项目 / 最近会话」时也要同步。</summary>
    private void SyncRailSelection(string key)
    {
        var railKey = key switch
        {
            "approval" => "approval",
            "workflow" => "workflow",
            "clients" or "customers" or "customer-qinghe" or "thread-tax" or "thread-new" => "clients",
            _ => "workbench"
        };

        foreach (var item in RailTop) item.IsSelected = item.Key == railKey;
    }

    // ===================== 登录动作 =====================
    /// <summary>
    /// 登录动作（LoginCommand 的执行体）。这里没有真实后端，只做「校验 → 转圈 → 进主壳」三步。
    /// 关键在于用 DispatcherTimer 而不是 Thread.Sleep 模拟异步：前者回到 UI 线程执行回调，
    /// 期间界面仍可重绘，登录按钮也能正常变灰。
    /// </summary>
    private void Login()
    {
        // 只做最基础的非空校验；LoginError 是自动属性，赋值后必须手动发通知。
        if (string.IsNullOrWhiteSpace(UserName) || string.IsNullOrWhiteSpace(Password))
        {
            LoginError = "请输入账号与密码";
            OnPropertyChanged(nameof(LoginError));
            return;
        }

        // 清掉上一次的错误提示，同样要手动通知，否则旧文案会一直留在界面上。
        LoginError = string.Empty;
        OnPropertyChanged(nameof(LoginError));
        // 置 true 会让 CanExecute 变为 false，登录按钮在等待期间不可再点。
        IsBusy = true;
        // 420ms 只是为了让 loading 态可见，真实项目这里换成 await 一个服务调用。
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            IsBusy = false;
            // 切到 App 阶段：IsLogin 变 false、IsApp 变 true，LoginView 隐藏、ShellView 显示。
            Stage = AppStage.App;
            // 进入主壳后重置一次一级导航，保证每次都从第一个模块的默认子页开始。
            SelectModule(RailItems[0], true);
        };
        timer.Start();
    }
}
