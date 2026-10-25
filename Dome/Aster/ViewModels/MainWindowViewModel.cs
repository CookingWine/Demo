// ============================================================================
// MainWindowViewModel.cs —— 应用壳（Shell）的唯一根视图模型
// 1) Stage（AppStage）三态驱动 BootView / LoginView / ShellView 的显隐；
// 2) 一级导航 RailItem + 二级导航 SubNavEntry，ActiveView 交给 DataTemplate 按类型渲染；
// 3) IsSettingsOpen 控制设置浮层，DialogKind 驱动登录页「忘记密码 / 联系管理员」两个两步弹窗。
// 协作：HomeViewModel / AdminViewModel / SettingsViewModel 都是它的子 VM，在此统一 new 出来。
// 坑：Stage 与 Dialog 都是「一个枚举 + 若干派生 bool」，换值后必须手动连带通知派生属性。
// ============================================================================
//
// 文件内的阅读顺序（也是数据流方向）：
//   AppStage / DialogKind 两个枚举（状态的取值域）
//   ↓
//   后备字段 + 构造函数（状态从哪来、初始化顺序）
//   ↓
//   子 VM / 命令 / 导航集合（对外暴露的只读面）
//   ↓
//   Stage setter（阶段切换 + 三条派生通知）
//   ↓
//   布局联动（SubNavWidth / IsSubNavUserVisible / IsRailAvatarVisible）
//   ↓
//   登录表单字段 + Dialog setter（弹窗状态 + 七条派生通知）
//   ↓
//   SelectModule / SelectSubNav / SyncRailSelection（导航数据流）
//   ↓
//   Login（唯一带时序的动作）
//
// 谁消费这些状态（都在 Dome/Aster/Views 下）：
//   MainWindow.axaml —— IsApp / IsLogin / IsBoot / IsSettingsOpen（四个视图的显隐）
//   ShellView.axaml  —— RailTop / RailSettings / SubNavEntries / SubNavTitle /
//                       SubNavWidth / IsSubNavVisible / IsSubNavUserVisible /
//                       IsRailAvatarVisible / ActiveView
//   LoginView.axaml  —— UserName / Password / Remember / ShowPassword / LoginError /
//                       IsForgotDialog / IsContactDialog / IsForgotStep1 / IsForgotStep2 /
//                       IsContactStep1 / IsContactStep2 / ForgotEmail / AdminCopyText
// 记住这张表，后面每个 setter 里「为什么发这几条通知」都能对回具体控件。

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Aster.Services;
using Avalonia.Threading;

namespace Aster.ViewModels;

// ---------- 枚举：壳阶段与弹窗状态 ----------
/// <summary>
/// 应用的三个顶层阶段：Boot（启动动画）→ Login（登录页）→ App（主壳）。
/// 由 MainWindowViewModel.Stage 持有，视图侧用 IsBoot/IsLogin/IsApp 三个派生 bool 绑定显隐。
/// </summary>
// 三态的合法迁移只有三条，且都有唯一出处，读代码时可以据此判断「现在该在哪个阶段」：
//   Boot  → Login   构造函数里 2 秒 DispatcherTimer 的 Tick（启动动画放完）
//   Login → App     Login() 里 420ms 定时器的 Tick（模拟登录成功）
//   App   → Login   LogoutCommand（退出登录）
// 没有 Login → Boot，也没有 App → Boot：回到 Boot 只可能发生在进程重启，
// 所以 BootView 的动画每进程只播一次。
// 另有一条隐式约束：Tick 里会先判断 Stage 还是不是 Boot，避免定时器还没跑完就被别的逻辑切走。
public enum AppStage
{
    // 启动动画阶段：BootView 显示，此时两个业务视图（Login / App）都不可见。
    // 选它做默认值（而不是 Login），是因为构造函数末尾的计时器要从 Boot 推进过去。
    Boot,
    // 登录页阶段：LoginView 显示；UserName / Password / LoginError / Dialog 全部在这一阶段被读写。
    Login,
    // 主壳阶段：ShellView 显示（rail + 二级导航 + 内容区），SettingsOverlay 也只在此时才有意义。
    App
}

/// <summary>
/// 登录页弹窗的唯一状态源：None 表示无弹窗，其余四个值对应「两个弹窗 × 两个步骤」。
/// 用单个枚举而不是两套 bool，是为了保证「同一时刻只可能有一个弹窗 / 一步」天然成立，
/// 避免 IsForgotOpen 与 IsContactOpen 同时为 true 这种非法组合。
/// </summary>
// ── 五个取值与界面元素的对应关系 ───────────────────────────────────────────────
// ┌────────────────┬───────────────────────────────────────────────────────────┐
// │ 取值           │ 界面上看到的东西                                          │
// ├────────────────┼───────────────────────────────────────────────────────────┤
// │ None           │ 无遮罩，登录表单可交互                                    │
// │ ForgotStep1    │ 「重置密码」卡片 + 邮箱输入 + 取消/发送                   │
// │ ForgotStep2    │ 「重置密码」卡片 + 绿色对勾 + 知道了                      │
// │ ContactStep1   │ 「联系管理员」卡片 + 管理员名片 + 关闭/发送邮件           │
// │ ContactStep2   │ 「联系管理员」卡片 + 绿色对勾 + 知道了                    │
// └────────────────┴───────────────────────────────────────────────────────────┘
// 注意「卡片」本身不变，只是卡片内两段 StackPanel 按 Is*Step1 / Is*Step2 互斥显示，
// 所以切换步骤时不会出现卡片重建、位置跳动。
// 由此也能看出：五次赋值全部发生在构造函数里注册的那几个命令体内（Open* / Send* / CloseDialog），
// 除了 Dialog setter 自己，没有别处会写这个字段。
public enum DialogKind
{
    // 唯一表示「关着」的值。Dialog setter 里复用这个值来做「关闭时复位 IsAdminCopied」的判断。
    None,
    // 忘记密码第 1 步：填邮箱。
    ForgotStep1,
    // 忘记密码第 2 步：发送成功。
    ForgotStep2,
    // 联系管理员第 1 步：展示管理员联系方式。
    ContactStep1,
    // 联系管理员第 2 步：发送成功。
    ContactStep2
}

/// <summary>
/// 应用壳 VM：集中持有「阶段 + 导航 + 设置浮层 + 登录表单 + 弹窗」五类状态，是全工程唯一的全局状态源。
/// MainWindow.axaml 直接以它为 DataContext；BootView / LoginView / ShellView 的显隐由阶段派生 bool 控制。
/// </summary>
// 为什么五类状态都堆在一个类里：本工程只有一个窗口、一个壳，拆成多个 VM 反而要处理
// 「VM 之间互相引用」的问题（比如登录成功后要重置导航）。集中在一个类里，
// 所有跨区域的联动都只是同一个对象内部的字段读写，代价是这个文件偏长。
// 三条不变量（读代码时可以用它们验证逻辑是否正确）：
//   1) 同一时刻 Stage 只有一个值，因此 IsBoot / IsLogin / IsApp 至多一个为 true；
//   2) 同一时刻 Dialog 只有一个值，因此两个弹窗、两个步骤至多一个可见；
//   3) RailItems 里至多一项 IsSelected 为 true，SubNavEntries 里也至多一项（由遍历整组重算保证）。
// 所有命令都是「无状态转接器」：不缓存数据，只把一次点击翻译成一次字段写入，
// 派生属性由 setter 连带通知，界面自己重绘。
public sealed class MainWindowViewModel : ObservableObject
{
    // ---------- 后备字段 ----------
    // 默认停在 Boot：首屏先播启动动画，2 秒后由构造函数里的 DispatcherTimer 推进到 Login。
    // 用字段初始化器而不是在构造函数里赋值，是为了让「初始值」和「声明」放在一起，
    // 也保证任何命令体在构造期间被意外触发时读到的都是有效值。
    private AppStage _stage = AppStage.Boot;
    // 当前一级模块 key。四个业务入口的 key 分别是 workbench / approval / clients / workflow，
    // 由 SelectModule 写入；注意「现状」：现在虽然每次都写它，但读它的地方已经没有了 ——
    // SubNavWidth 退化成常量 260，不再按模块分宽度。留着是为了将来恢复多模块时不用改调用方。
    private string _moduleKey = "admin";
    // 二级导航顶部的标题（"工作区"），绑到 ShellView 二级栏顶部的 TextBlock。
    private string _subNavTitle = "管理";
    // 内容区当前承载的 ViewModel 对象；默认 null，构造函数里 SelectModule 会把它换成 Admin。
    // 类型是 object? 而不是某个基类，因为各页面 VM 之间没有共同基类，模板靠运行时类型分发。
    private object? _activeView;
    // 设置浮层的开关，public setter，绑 MainWindow.axaml 里 SettingsOverlay.IsVisible。
    private bool _isSettingsOpen;
    // 弹窗状态，唯一状态源；五个取值见 DialogKind。
    private DialogKind _dialog = DialogKind.None;
    // 登录表单三个字段：账号、密码、记住我。全部与 LoginView 的输入控件双向绑定。
    // Password 存明文（演示项目），遮罩只在视图层通过 PasswordChar 做。
    private string _userName = string.Empty;
    private string _password = string.Empty;
    // 默认勾选：演示时少点一次，也让「记住我」的初始视觉是「已选」。
    // 真实值由本地缓存覆盖（构造函数步骤 0），用户在缓存文件里存过 false 就以缓存为准。
    private bool _remember = true;
    // 密码是否明文显示，由眼睛按钮翻转；它只有两个派生属性（PasswordChar / PasswordRevealChar），
    // 而 LoginView 实际用的是 TextBox.RevealPassword 直接绑这个 bool，见 ShowPassword 的说明。
    private bool _showPassword;
    // 登录请求进行中标记。只被 Login() 置 true、被 420ms 后的 Tick 置回 false，
    // 唯一的消费者是 LoginCommand 的 canExecute（而那个 canExecute 实际不生效，见构造函数）。
    private bool _isBusy;

    /// <summary>
    /// 构造函数里按顺序做五件事：回填缓存里的账号 → 建命令 → 建子 VM → 初始化导航集合 → 启动 2 秒启动计时器。
    /// 所有命令都只在此处创建一次并暴露为只读属性，XAML 侧只负责 {Binding XxxCommand}。
    /// </summary>
    // ── 初始化顺序（顺序不能随便换，每一步都依赖前一步的产物）────────────────────
    // 步骤 0  从 Services.LocalCache 回填「记住我」的账号 —— 不依赖任何其它成员，放最前面。
    // 步骤 1  建 12 个命令 —— 此时不读任何状态；lambda 只是捕获 this，真正执行要等用户点击。
    // 步骤 2  建 3 个子 VM —— SettingsViewModel 要吃步骤 1 建好的 LogoutCommand，故必须在 1 之后。
    // 步骤 3  建 RailItems 集合 —— 每一项都要传命令实例，故也在 1 之后；
    //         集合里「设置的 key 叫 settings 且排在最后」是步骤 4 的隐含前提。
    // 步骤 4  取 RailTop / RailSettings —— Take(4) 与 [4] 硬依赖步骤 3 的顺序，改顺序这里立即错位。
    // 步骤 5  建空 SubNavEntries —— 必须在步骤 6 之前，否则 SelectModule 里 Clear() 会抛 NullReference。
    // 步骤 6  SelectModule(RailItems[0], true) —— 首屏就切换一次，依赖 1（命令）、3（RailItems）、
    //         5（空集合就绪）。它顺带把 ActiveView 设成 Admin，让 ShellView 即使还不可见也已备好数据。
    // 步骤 7  启动 2 秒 Boot 计时器 —— 依赖 _stage 的初始值 Boot。放最后，
    //         保证前六步全部完成后才可能出现第一次阶段切换。
    // 依赖链一句话：0 → 1 → 2 → 3 → 4 → 5 → 6 → 7，其中 6 同时依赖 1、3、5。
    // 为什么命令要最先建：RailItem 的构造函数要求传 ICommand，而命令体是延迟执行的，
    // 所以「命令体引用了后面才赋值的字段」不构成问题 —— lambda 被调用时才去读字段。
    public MainWindowViewModel()
    {
        // ===== 步骤 0：从本地缓存回填「记住我」的账号 =====
        // 放在所有命令 / 子 VM 之前，是为了让 UserName、Remember 在界面第一次绑定取值时就已经是
        // 上次的值（DataContext 在窗口构造后才赋值，所以这里改不触发任何多余的绑定刷新）。
        // 读的是内存里的缓存副本；文件本身已在 LocalCache 里惰性加载过（必要时当场创建）。
        RestoreRememberedLogin();

        // ===== 步骤 1：建全部命令（一次建好、永不重建）=====
        // 一级导航命令：由 DataTemplate 通过 CommandParameter 把被点的 RailItem 传进来。
        // 谁触发：ShellView 的 RailTemplate（Button.Command="{Binding Command}"），
        //         CommandParameter="{Binding}" 传的就是这个 RailItem 自身。
        // o is RailItem item 是类型模式匹配：参数类型是 object?，匹配失败说明 CommandParameter
        // 不是 RailItem（例如模板被复用到别处），此时什么都不做，也不抛异常。
        // 做什么：转调单参 SelectModule，那里会先拦「设置」，再真正切换。
        SelectModuleCommand = new RelayCommand(o =>
        {
            if (o is RailItem item) SelectModule(item);
        });
        // 谁触发：RailTemplate 的「设置」项（RailItems[4]，它的 Command 就是本条命令，
        //         不是 SelectModuleCommand —— 这是「设置」能绕过模块切换的原因）；
        // 注意 SelectModule 里那段 if (item.IsSettings) 分支因此永远走不到，属于残留逻辑。
        // 改什么状态：只把 IsSettingsOpen 置 true，由 MainWindow.axaml 的 SettingsOverlay.IsVisible 消费。
        OpenSettingsCommand = new RelayCommand(() => IsSettingsOpen = true);
        // 谁触发：SettingsOverlay 右上角的关闭按钮，以及浮层底部的「关闭」按钮（两处都绑这条）。
        // 改什么状态：IsSettingsOpen = false，浮层隐藏；App 阶段与导航状态都不受影响。
        CloseSettingsCommand = new RelayCommand(() => IsSettingsOpen = false);
        // canExecute 传 !IsBusy，意图是「请求进行中禁止重复提交」。
        // 但要清楚它目前只是声明了意图：RelayCommand.CanExecuteChanged 从不触发，
        // 而 Button 仅在挂上 Command 时查询一次 CanExecute，所以登录那 420ms 里按钮不会真的变灰。
        // 想让它生效，需要在 IsBusy 的 setter 里调用 ((RelayCommand)LoginCommand).RaiseCanExecuteChanged()。
        // 补充：CanExecute 为 false 时 Button 会显示禁用样式（变灰），但这一切的前提是「命令通知了」。
        // 现状是通知缺失，所以 420ms 内按钮外观与可点性都不变 —— 这一点在 Login() 里也标了注解，
        // 两处说法一致：canExecute 目前只是「声明意图」，不是「生效的约束」。
        // 谁触发：LoginView 的主按钮（Content="登录"），键盘回车不会触发（页面没有默认按钮）。
        LoginCommand = new RelayCommand(Login, () => !IsBusy);
        // 退出登录：先关掉设置浮层，否则回到登录页后浮层还会压在上面。
        // 谁触发：SettingsOverlay 里那个「退出登录」菜单项 —— 命令是由本类通过构造函数
        //         注入给 SettingsViewModel 的，所以点击链路是「视图 → SettingsViewModel 的菜单模型 → 这里」。
        // 改什么状态：先 IsSettingsOpen=false（浮层隐藏，避免压住登录页），再 Stage=Login；
        // Stage setter 会连带通知 IsApp / IsLogin / IsBoot，于是 ShellView 隐藏、LoginView 出现。
        // 注意它不会清空 UserName / Password / LoginError，也不会把 Dialog 复位 ——
        // 于是退出后登录页仍保留着上次输入的账号密码（见文件末尾 Login() 的说明）。
        LogoutCommand = new RelayCommand(() =>
        {
            IsSettingsOpen = false;
            Stage = AppStage.Login;
            // 退出登录是「一次会话的结束」，把缓存立刻刷盘：此刻不写，
            // 之后用户如果直接关窗口，700ms 防抖里的那次写可能来不及发生。
            LocalCache.Save();
        });
        // 谁触发：LoginView 密码行右侧的眼睛按钮（Classes="ghost"，ToolTip="显示密码"）。
        // 改什么状态：把 ShowPassword 取反。真正生效的绑定是 TextBox.RevealPassword="{Binding ShowPassword}"，
        // 为 true 时 TextBox 直接回显明文；命令行里的 ShowPassword = !ShowPassword 会走 setter 的 Set + 两条派生通知。
        TogglePasswordCommand = new RelayCommand(() => ShowPassword = !ShowPassword);
        // 登录页两个弹窗的「打开 / 下一步 / 关闭」全部只是改 Dialog 枚举值，没有额外的可见性字段。
        // 谁触发（逐个对应 LoginView.axaml 里的按钮）：
        //   OpenForgotCommand  ← 「忘记密码？」链接按钮
        //   OpenContactCommand ← 「联系管理员」链接按钮
        //   CloseDialogCommand ← 两个弹窗右上角的 X、「取消」、「关闭」、「知道了」（共 6 处）
        //   SendResetCommand   ← 忘记密码第 1 步的「发送重置链接」
        //   SendMailCommand    ← 联系管理员第 1 步的「发送邮件」
        // 五条命令都没有参数：弹窗只有「哪个弹窗、哪一步」两个维度，全由目标枚举值写死，
        // 所以不需要 CommandParameter，也不会出现「传错步骤」的问题。
        OpenForgotCommand = new RelayCommand(() => Dialog = DialogKind.ForgotStep1);
        OpenContactCommand = new RelayCommand(() => Dialog = DialogKind.ContactStep1);
        CloseDialogCommand = new RelayCommand(() => Dialog = DialogKind.None);
        SendResetCommand = new RelayCommand(() => Dialog = DialogKind.ForgotStep2);
        SendMailCommand = new RelayCommand(() => Dialog = DialogKind.ContactStep2);
        // 复制按钮是纯演示：不碰剪贴板，只把文案切成「已复制」，关闭弹窗时由 Dialog setter 复位。
        // 谁触发：联系管理员弹窗里管理员名片右侧的复制按钮，它的 Content 绑 AdminCopyText。
        // 改什么状态：IsAdminCopied = true → 连带通知 AdminCopyText → 按钮文字变「已复制」。
        // 只能「置 true」不能「切换」：再点一次没有任何变化（Set 返回 false，连通知都不发），
        // 这正是想要的 —— 复制是幂等的，视觉上停在「已复制」直到关闭弹窗。
        CopyAdminCommand = new RelayCommand(() => IsAdminCopied = true);

        // ===== 步骤 2：建子 VM（只建一次，切模块 / 切阶段都复用，避免状态丢失）=====
        // 子 VM 在壳里只创建一次，切换模块 / 阶段时复用，避免反复 new 导致状态丢失。
        // 注意顺序：Settings 需要 LogoutCommand，所以必须排在步骤 1 之后；
        // Home 与 Admin 谁先谁后无所谓（互不引用）。
        // 现状提醒：Home 被创建但从未被赋给 ActiveView（SelectModule 只设 Admin），
        // Markdown / Music 两个 VM 甚至没在这里创建 —— 它们的 DataTemplate 目前是「备用」。
        Home = new HomeViewModel();
        Admin = new AdminViewModel();
        // 注入而不是让 SettingsViewModel 自己实现退出：退出要切 Stage，那是壳的职责，
        // 子 VM 不该反向依赖壳，所以这里把命令当参数传下去。
        Settings = new SettingsViewModel(LogoutCommand);

        // ===== 步骤 3：建一级导航集合（五项，顺序有含义）=====
        // 工作区四个一级入口 + 设置，对齐 HR Agent 原型的侧栏结构
        // 每一项的第三个参数是 Controls.Icons 里的路径数据常量，模板会把它喂给 ctrl:Icon。
        // 前四项共用 SelectModuleCommand（靠 CommandParameter 区分点了哪项），
        // 第五项单独用 OpenSettingsCommand —— 它不是「切模块」，而是「开浮层」。
        RailItems = new ObservableCollection<RailItem>
        {
            new("workbench", "工作台", Controls.Icons.Grid, SelectModuleCommand),
            new("approval", "审批", Controls.Icons.List, SelectModuleCommand),
            new("clients", "客户", Controls.Icons.Users, SelectModuleCommand),
            new("workflow", "Workflow", Controls.Icons.Trend, SelectModuleCommand),
            new("settings", "设置", Controls.Icons.Sliders, OpenSettingsCommand)
        };

        // ===== 步骤 4：拆出 RailTop（上四项）与 RailSettings（第五项）=====
        // RailTop 与 RailSettings 是同一批数据的两种取法：上方渲染 RailTop，底部单独摆设置入口。
        // Take(4) 里的 4 是「业务入口数量」，与上面集合里前四项一一对应；
        // 这些是同一批实例（Take 只复制引用），所以 SelectModule 里改 RailItems[i].IsSelected
        // 会立刻反映到 RailTop 渲染出的同一对象上，不需要再同步一次。
        // RailSettings 是单个对象而不是集合，因为 ShellView 用 ContentControl 单独摆它。
        RailTop = new ObservableCollection<RailItem>(RailItems.Take(4));
        RailSettings = RailItems[4];

        // ===== 步骤 5：建空的二级导航集合（原地复用，永不换实例）=====
        // 二级导航集合只建一次，之后每次切模块都是 Clear 再重新填充（见 SelectModule）。
        // 必须原地复用：ItemsControl 绑的是这个集合实例，整体换新实例会让绑定指向旧对象、
        // 界面反而看不到新数据（而 Clear/Add 会发集合变更事件，界面自动刷新）。
        SubNavEntries = new ObservableCollection<SubNavEntry>();
        // ===== 步骤 6：首屏切换一次（依赖已建好的命令、RailItems、空集合）=====
        // 传 true 显式跑一次切换，保证首屏就有二级菜单和默认选中项。
        // 这一句会连锁触发：RailItems 选中态 → SubNavEntries 填充 → SubNavTitle/ActiveView 赋值
        // → SelectSubNav → Admin.Select("workbench")。也就是说主壳还没显示，数据已经备好。
        SelectModule(RailItems[0], true);

        // ===== 步骤 7：启动 2 秒 Boot 计时器 =====
        // 用 DispatcherTimer 模拟「启动耗时」：不阻塞 UI 线程，2 秒后在 UI 线程回调里推进阶段。
        // 2000ms 是纯展示用时长（让启动动画播完一轮），没有业务含义；
        // 换成 Thread.Sleep(2000) 会卡住 UI 线程，启动动画根本画不出来。
        // 计时器是局部变量：只要它还在跑，回调里的闭包就持有它，不会被 GC；
        // Stop() 之后没有其它引用，随下一次 GC 回收 —— 一次性计时器的常见写法。
        var bootTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2000) };
        bootTimer.Tick += (_, _) =>
        {
            // 先停：DispatcherTimer 默认会重复触发，不 Stop 会每 2 秒推进一次。
            bootTimer.Stop();
            // 仅当仍停在 Boot 才推进，避免期间有别的逻辑已经切走时被覆盖。
            // 这是本类唯一的「防重入」式判断：赋值前先确认前置状态仍然成立。
            if (Stage == AppStage.Boot) Stage = AppStage.Login;
        };
        bootTimer.Start();
    }

    // ===================== 子 VM / 命令 / 导航集合 =====================
    // 三个子 VM 常驻且只读：壳只决定「显示哪一个」，不重建实例，因此子 VM 内部状态在切换时得以保留。
    // Admin 是唯一被真正放进 ActiveView 的（SelectModule 里赋值）；
    // Home 目前只创建不使用（ActiveView 从不指向它），保留是因为空 DataTemplate 已经写好了。
    // Settings 不参与 ActiveView 的切换 —— 它是浮层，由 IsSettingsOpen 控制显隐。
    public HomeViewModel Home { get; }
    public AdminViewModel Admin { get; }
    public SettingsViewModel Settings { get; }

    // 命令按用途分为三组：导航（SelectModule / Open / CloseSettings）、
    // 登录（Login / Logout / TogglePassword）、弹窗（Open* / Send* / CloseDialog / CopyAdmin）。
    // 全部声明成 ICommand 而不是 RelayCommand：XAML 只认 ICommand 接口，
    // 暴露接口能防止外部误调 RaiseCanExecuteChanged；代价是内部要调时得先强转。
    // 每一项的触发控件见构造函数里各命令上方的「谁触发」注释。
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
    // private set 的效果：外部只能读，写只能由本类内部的三个调用点发起
    // （构造函数里的 bootTimer、Login() 里的 420ms 定时器、LogoutCommand）。
    // 为什么不做成 public：阶段切换牵动整个窗口的内容，必须保证「谁能改」是明确的；
    // 若外部可随意写，就会出现绕过 LogoutCommand（不清设置浮层）之类的非法路径。
    public AppStage Stage
    {
        // 读后备字段而不是读属性自身，避免任何隐式递归。
        get => _stage;
        private set
        {
            // Set 返回 false 表示新旧值相同：字段没写、通知不发、派生属性也一定没变，所以直接早退。
            // 典型场景：登录成功时 Stage 已经是 App（重复登录），再赋一次 App 不应该引发任何刷新。
            if (!Set(ref _stage, value)) return;
            // 以下三条是「派生属性必须手动补发」的教科书例子：
            // Set 只通知了 "Stage" 这一个属性名，但 XAML 里没有任何地方绑 Stage，
            // 三处绑定（都在 MainWindow.axaml 的叠层 Panel 里）读的都是下面这些 bool：
            //   IsBoot  → BootView.IsVisible="{Binding IsBoot}"
            //   IsLogin → LoginView.IsVisible="{Binding IsLogin}"
            //   IsApp   → ShellView.IsVisible="{Binding IsApp}"
            // 漏掉任何一条，对应视图就停在旧的显隐状态（例如 Stage 变成 App 了但 ShellView 没出现）。
            // 三条都发而不是「只发真的变了的那两条」：判断哪条从 true 变 false 需要额外记住旧值，
            // 而多发一条只是让绑定重读一次布尔值，代价可以忽略。
            OnPropertyChanged(nameof(IsBoot));
            OnPropertyChanged(nameof(IsLogin));
            OnPropertyChanged(nameof(IsApp));
        }
    }

    // 三个派生 bool 直接读 _stage 而不是 Stage：省一次属性调用，也彻底避免潜在的重入。
    // 构造完成前它们就已有正确值（_stage 用字段初始化器设成了 Boot），所以绑定初始化时不会读到意外状态。
    // 它们是计算属性、不存状态 —— 这正是必须在 Stage setter 里补通知的根本原因。
    public bool IsBoot => _stage == AppStage.Boot;
    public bool IsLogin => _stage == AppStage.Login;
    public bool IsApp => _stage == AppStage.App;

    // ===================== 二级导航布局联动 =====================
    // 这一小节只有四个属性，但它们要连着读：三个布局属性共同决定「用户信息显示在哪」。
    // 绑定点（都在 ShellView.axaml）：
    //   SubNavTitle          → 二级栏顶部标题 TextBlock.Text
    //   SubNavWidth          → 二级栏 Border.Width（三栏布局第二列是 Auto，跟着它伸缩）
    //   IsSubNavUserVisible  → 二级栏底部用户卡片 Border.IsVisible
    //   IsRailAvatarVisible  → rail 底部 32px 圆形头像 Border.IsVisible
    public string SubNavTitle
    {
        get => _subNavTitle;
        // 唯一写入点：SelectModule 的 switch 里（目前只有 default 分支，写 "工作区"）。
        // 用 Set 是为了「切模块但标题没变」时不刷界面；初始化时 _subNavTitle 是 "管理"，
        // 第一次 SelectModule 会把它改成 "工作区"，这一次是真的变了，所以会发通知。
        private set => Set(ref _subNavTitle, value);
    }

    /// <summary>
    /// 二级栏宽度：现在只剩工作区一个模块（四个页面共用同一条侧栏），固定 260 保证项目/会话/用户卡片放得下。
    /// 注意它是计算属性，_moduleKey 改动后必须手动 OnPropertyChanged（在 SelectModule 里做）。
    /// </summary>
    // 现状说明（与上面的设计意图有出入，读代码时别被绕进去）：
    // 它有 getter 没有 setter，是只读计算属性，但函数体是常量 260 —— 不读 _moduleKey。
    // 也就是说「按模块分宽度」的能力已经退化，260 是唯一返回值；
    // SelectModule 里那条 OnPropertyChanged(nameof(SubNavWidth)) 每次通知的都是同一个值，
    // 绑定会重读、发现没变、不做任何事。真正支撑 260 的是布局约束：
    // 项目名 + 会话名 + 徽标 + 底部「张客服 / 客服」这一行要放得下，再窄就要省略号截断。
    // ShellView.axaml 里注释写的是「管理模块 260，其它 184」，那是旧方案的遗留描述，
    // 与当前实现（恒定 260）不一致。
    public double SubNavWidth => 260;

    /// <summary>二级栏底部始终展示「当前用户」卡片。</summary>
    // 恒为 true：直接写成表达式而不是字段，表示「这个形态下没有第二种可能」。
    // 它的存在意义是与下面的 IsRailAvatarVisible 构成互斥关系（见那里的说明）。
    public bool IsSubNavUserVisible => true;

    /// <summary>
    /// 与 IsSubNavUserVisible 恰好互斥：二级栏里已有用户卡片时，rail 底部不再重复显示头像；
    /// 反之（窄二级栏）才把头像放回 rail 底部。三者必须同进同退，因此通知也集中在 SelectModule 里发。
    /// </summary>
    // ── 三者的联动逻辑，逐行拆开 ────────────────────────────────────────────────
    // 规则本身很简单：同一个人（当前用户）在一屏里只出现一次。
    //   ① SubNavWidth 足够宽（260）→ 二级栏底部能放下完整卡片（头像 + 姓名 + 角色），
    //      于是 IsSubNavUserVisible = true，用户信息在二级栏里"完整呈现"；
    //   ② 一旦 IsSubNavUserVisible 为 true，IsRailAvatarVisible 就必须为 false ——
    //      否则 rail 底部那个小头像和二级栏的卡片会同时出现，同一个人画两遍；
    //   ③ 反过来，如果某个模块把二级栏收窄到放不下卡片（历史上是 184 宽的那种形态），
    //      就该把 IsSubNavUserVisible 置 false，此时本属性自动变 true，
    //      信息降级成「只有头像」回到 rail 底部，至少还能看出当前是谁。
    // 为什么写成 !IsSubNavUserVisible 而不是各自一个字段：
    // 两者是同一个事实的两种表现，用取反表达，从根本上排除了「两个都 true / 两个都 false」的非法组合。
    // 现状：IsSubNavUserVisible 恒 true → 本属性恒 false → ShellView 里 rail 底部的头像永远不可见。
    // 所以这条联动目前只剩「代码形状」，没有任何一个时刻会真的把头像放到 rail 上。
    // SelectModule 里之所以还要通知它，是为了将来恢复按模块分宽度时不必再改调用方。
    public bool IsRailAvatarVisible => !IsSubNavUserVisible;

    // 演示用的固定用户信息；真实项目会来自登录结果或用户接口。
    // 三个都是常量表达式（无字段、无通知）：这一屏里用户不会变，绑定读一次就够。
    // "张" 是姓氏首字，画在 30px 圆形头像里代替真实图片（RailAvatar 那里用的是字母 "L"，两处不一致）。
    public string SubNavUserInitial => "张";
    public string SubNavUserName => "张客服";
    public string SubNavUserRole => "客服";

    // 与上面的 IsSubNavUserVisible 不同：这个是「整条二级栏显不显示」，有后备字段。
    private bool _isSubNavVisible = true;

    /// <summary>二级栏整体显隐（当前逻辑里切模块时会被置为 true）。</summary>
    // 唯一写入点：SelectModule 里的 IsSubNavVisible = true。
    // 因为初始值就是 true，这个赋值实际上永远返回 false（Set 判定相等直接早退），
    // 也就是说这条通知一次都不会发 —— 但界面本来也不需要变。
    // 若将来要做「某些模块没有二级菜单」的形态，把它置 false 即可让 Auto 列宽归零、内容区自动补位。
    public bool IsSubNavVisible
    {
        get => _isSubNavVisible;
        private set => Set(ref _isSubNavVisible, value);
    }

    /// <summary>
    /// 主内容区当前显示的 ViewModel。ShellView 里用 ContentControl + DataTemplate 按类型匹配渲染，
    /// 所以这里放的是「对象」而不是控件，切换模块只需换实例。
    /// </summary>
    // 赋值点只有 SelectModule 的 switch（每个模块一行 ActiveView = 对应的子 VM）。
    // 这条赋值的连锁反应值得记一下：ActiveView 变了 → ContentControl.Content 变 →
    // Avalonia 拿新对象的运行时类型去 DataTemplates 里找隐式模板
    // （HomeViewModel → HomeView、AdminViewModel → AdminView 等）→ 实例化/复用对应视图。
    // 因此「切页面」在 C# 侧只是换了一个对象引用，没有任何 if/switch 去操作控件。
    // private set：外部只能读，避免某个视图直接把 ActiveView 换成别的实例、绕过导航状态。
    public object? ActiveView
    {
        get => _activeView;
        private set => Set(ref _activeView, value);
    }

    /// <summary>设置浮层开关。和登录弹窗是两套东西：这个属于 App 阶段的壳内浮层，setter 是 public。</summary>
    // 这里是全类唯一一个 public setter 的布尔状态：SettingsOverlay 里可能有「点遮罩关闭」
    // 之类的写法需要直接赋值，所以放宽了可见性。
    // 它与 Dialog 的区别要说清：Dialog 只影响 LoginView 内部的遮罩层；
    // IsSettingsOpen 影响 MainWindow 最上层的 SettingsOverlay，且在 App 阶段才有意义
    // （Login 阶段它是 false，因为 LogoutCommand 会先把它关掉再切阶段）。
    // 消费点：MainWindow.axaml 的 <views:SettingsOverlay IsVisible="{Binding IsSettingsOpen}" />。
    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set => Set(ref _isSettingsOpen, value);
    }

    // ===================== 登录 =====================
    // 四个表单字段（UserName / Password / Remember / ShowPassword）共同点：
    // 都是「视图写、VM 读」，双向绑定。它们的 setter 只做 Set，没有派生属性需要补通知 ——
    // 唯一的例外是 ShowPassword（它有两个 char 派生属性）。
    /// <summary>账号，与登录页 TextBox 双向绑定；忘记密码弹窗里的「账号邮箱」也复用这份数据。</summary>
    // 双向绑定的意义：用户每敲一个字符，TextBox 就把 Text 写回这里；
    // 反过来 Login() 里读的就是这个字段，所以「校验」不需要从控件取值。
    // 复用关系：ForgotEmail 是本属性的别名（getter/setter 都转调它），所以弹窗里的邮箱框
    // 与登录页的账号框是同一份数据 —— 登录页填过什么，打开弹窗就能看到。
    public string UserName
    {
        get => _userName;
        set => Set(ref _userName, value);
    }

    /// <summary>密码明文存放（演示用）。视图侧靠 PasswordChar 遮罩，不在这里加密。</summary>
    // 明文存放是刻意的演示取舍：真实项目这里应该是「不保存、只在提交时读一次」，
    // 或至少不放在 VM 的公开属性上。现在退出登录后密码还留在内存里（见 LogoutCommand 的说明）。
    public string Password
    {
        get => _password;
        set => Set(ref _password, value);
    }

    /// <summary>
    /// 「记住我」勾选态。勾选状态与账号名会写进本地缓存，下次启动自动回填（见 RestoreRememberedLogin）。
    /// </summary>
    // 绑 CheckBox.IsChecked（bool? 与 bool 之间的转换由绑定引擎处理）。
    // 行为约定：勾上时立刻把「当前账号 + true」存起来；取消勾选时把账号名一起清掉 ——
    // 用户点掉「记住我」的意图就是「别再留我的账号」，只改 bool 不清名字等于没记住用户的选择。
    // 写入走 SaveSoon（延迟合并写），勾选这种一次性动作其实也可以直接 Save，
    // 这里统一用 SaveSoon 是为了让「勾选 → 输账号 → 登录」这串操作只落一次盘。
    public bool Remember
    {
        get => _remember;
        set
        {
            if (!Set(ref _remember, value)) return;
            // 无条件写：取消勾选时 value 为 false，下面的三元会把 UserName 存成空串。
            var login = LocalCache.Data.Login;
            login.Remember = value;
            login.UserName = value ? UserName : string.Empty;
            LocalCache.SaveSoon();
        }
    }

    /// <summary>
    /// 密码是否明文显示，由眼睛按钮的 TogglePasswordCommand 翻转。
    /// 因为 PasswordChar / PasswordRevealChar 是计算属性，这里必须在 Set 返回 true 后手动补两条通知。
    /// </summary>
    public bool ShowPassword
    {
        // 读后备字段。
        get => _showPassword;
        set
        {
            // 早退写法：值没变就什么都不做。这里尤其重要 —— 眼睛按钮是「切换」语义，
            // 若外部重复赋同一个值，不该引发多余的字符重排与通知。
            if (!Set(ref _showPassword, value)) return;
            // 两条派生通知：PasswordChar / PasswordRevealChar 都是计算属性，
            // 值随 ShowPassword 变，但 Set 只通知了 "ShowPassword" 这一个名字。
            // 现状：这两条通知发给了没有任何绑定读取的属性（LoginView 用的是硬编码的 PasswordChar
            // 加 TextBox.RevealPassword 绑定），所以它们目前是「无害的空转」。
            OnPropertyChanged(nameof(PasswordChar));
            // Avalonia 的 TextBox 只有 PasswordChar 与 RevealPassword 两个相关属性，
            // 没有 PasswordRevealChar；这个名字在本工程里没有任何 XAML 绑定，纯冗余。
            OnPropertyChanged(nameof(PasswordRevealChar));
        }
    }

    /// <summary>
    /// 遮罩字符：明文时不遮罩（'\0' 表示不绘制遮罩字符），否则用实心圆点。
    /// 现状：LoginView 里 PasswordChar 是硬编码 "●"、显隐交给 TextBox.RevealPassword 绑定，
    /// 这两个属性目前没被 XAML 使用，保留作「遮罩字符也由 VM 决定」的备选写法。
    /// </summary>
    // 两个字符属性都是计算属性，所以 ShowPassword 的 setter 里必须手动通知（已在那里说明）。
    // '\0' 的语义：TextBox 用 NUL 表示「不绘制遮罩字符」，于是文本按原样显示 ——
    // 这就是「明文模式」的实现方式；'●' 则是遮罩时替换每个字符的占位符。
    // 本页实际用的是更直接的 Avalonia 属性 TextBox.RevealPassword（见 LoginView 密码行），
    // 所以这两个属性目前只是备选方案，没有任何绑定读取。
    public char PasswordChar => ShowPassword ? '\0' : '●';
    // 与上一行完全相同：保留两个名字，是为了将来若某个绑定只认其中一个也能编过。
    public char PasswordRevealChar => ShowPassword ? '\0' : '●';

    /// <summary>登录请求进行中。LoginCommand 的 CanExecute 依赖它，置 true 即自动禁用登录按钮。</summary>
    // 更正上面这句「自动禁用」：那是设计意图，实际不成立。
    // CanExecute 确实会返回 false，但没有任何人重新查询它（RelayCommand 从不触发 CanExecuteChanged），
    // 所以按钮在那 420ms 里既不变灰也不会拒绝点击。
    // 详见构造函数里 LoginCommand 那一段，以及 LoginView 主按钮上方的说明 —— 三处说法一致：不生效。
    // 另外 IsBusy 也没有任何 XAML 绑定（比如转圈动画），置 true 目前对界面零影响。
    public bool IsBusy
    {
        get => _isBusy;
        // private set：只有 Login() 与它内部的定时器能改，避免视图侧干预请求状态。
        private set => Set(ref _isBusy, value);
    }

    /// <summary>
    /// 登录错误文案。它是普通自动属性（没有后备字段的 Set 调用），
    /// 因此 Login() 里每次赋值之后都必须手动 OnPropertyChanged，否则界面不会刷新。
    /// </summary>
    // 为什么不用 Set：没有后备字段可以传 ref，硬要写还得多声明一个字段；
    // 这种只有两三处写入的简单属性，手写通知反而更直观。
    // 代价是：任何新增的赋值点都得自己记得补一行 OnPropertyChanged，编译器不会提醒，
    // 漏了的表现就是「校验失败但界面没提示」。目前写入点只有 Login() 里的两处。
    // 消费点：LoginView 主按钮下方那行红色 TextBlock（Text="{Binding LoginError}"）；
    // 文案为空时仍占一行高度，所以错误出现/消失不会引起布局跳动。
    public string LoginError { get; private set; } = string.Empty;

    // ===================== 弹窗 =====================
    /// <summary>
    /// 登录页弹窗的唯一状态源。用「一个枚举 + 多个派生 bool」而不是四五个独立 bool，
    /// 好处有两点：一是「同一时刻只有一个弹窗、一步」在类型层面就成立，不会出现忘记互斥的非法组合；
    /// 二是复位只需一个地方（设为 None），顺带能清掉复制按钮的临时状态。
    /// 代价是换值时必须在 setter 里把全部派生属性通知一遍，漏一个就会「状态变了界面不变」。
    /// </summary>
    // 赋值点全部在构造函数注册的命令里：Open* / Send* / CloseDialog 五条命令各写一个目标值，
    // 没有任何别处会碰 _dialog（private set 也保证了这一点）。
    // 因此「弹窗当前在哪一步」完全可枚举，不存在中间态。
    public DialogKind Dialog
    {
        // 读后备字段。
        get => _dialog;
        private set
        {
            // 早退写法：同一个值重复赋（例如连点两次「发送」）不会引发任何刷新与复位副作用。
            if (!Set(ref _dialog, value)) return;
            // 以下七条通知，逐条对应 LoginView.axaml 里的一个绑定点：
            //   IsDialogOpen     → 没有控件用它（遮罩改由两个 IsXxxDialog 各自控制 IsVisible）；
            //                      保留是因为语义上「有没有弹窗」是一个独立事实，方便将来做「点遮罩关闭」。
            //   IsForgotDialog   → 「忘记密码」那个遮罩 Panel 的 IsVisible
            //   IsContactDialog  → 「联系管理员」那个遮罩 Panel 的 IsVisible
            //   IsForgotStep1    → 忘记密码卡片内第 1 段 StackPanel 的 IsVisible（邮箱输入 + 发送）
            //   IsForgotStep2    → 忘记密码卡片内第 2 段 StackPanel 的 IsVisible（成功态）
            //   IsContactStep1   → 联系管理员卡片内第 1 段 StackPanel 的 IsVisible（名片 + 发送邮件）
            //   IsContactStep2   → 联系管理员卡片内第 2 段 StackPanel 的 IsVisible（成功态）
            // 这七条一次全发，是因为一次 Dialog 变化可能同时影响「哪个弹窗」和「哪一步」
            // （例如 ForgotStep1 → ForgotStep2：弹窗没变、步骤变了；None → ContactStep1：两步一起变）。
            OnPropertyChanged(nameof(IsDialogOpen));
            OnPropertyChanged(nameof(IsForgotDialog));
            OnPropertyChanged(nameof(IsContactDialog));
            OnPropertyChanged(nameof(IsForgotStep1));
            OnPropertyChanged(nameof(IsForgotStep2));
            OnPropertyChanged(nameof(IsContactStep1));
            OnPropertyChanged(nameof(IsContactStep2));
            // 关闭弹窗时顺手复位「已复制」文案，下次打开不会残留上一次的状态。
            // 这是本类里唯一「写 A 顺带改 B」的地方：复位的触发点放在 Dialog 上而不是
            // CopyAdminCommand 上，因为「关闭」有五条路径（X / 取消 / 关闭 / 知道了 ×2），
            // 集中在这里只需写一次。IsAdminCopied 的 setter 自己会再通知 AdminCopyText。
            if (value == DialogKind.None) IsAdminCopied = false;
        }
    }

    // 以下派生属性分别对应：遮罩层是否有弹窗、是哪个弹窗、当前停在哪一步。
    // 模板把它们直接绑到 Panel / StackPanel 的 IsVisible 上，做到「同一份结构，按步骤显示不同内容」。
    // 全部是纯计算属性（每次读都重新比较 _dialog），所以必须靠上面的七条通知驱动刷新。
    //   IsDialogOpen  —— _dialog != None，最粗粒度的「有没有弹窗」。
    //   IsForgotDialog / IsContactDialog —— 两个遮罩 Panel 各自的总开关，
    //                    一个弹窗的两个步骤都归同一个 Panel 管，所以这里的判断是「或」。
    //   Is*Step1/2    —— 卡片内部两段内容的互斥开关，判断是「等于」。
    // 用 is A or B 而不是 (== A || == B)：模式匹配写法更短，语义完全等价。
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
    // 别名属性的代价：它没有自己的通知。写 ForgotEmail 时实际走的是 UserName 的 setter，
    // 发出的通知名是 "UserName"，绑在 ForgotEmail 上的 TextBox 不会因为这次赋值而刷新 ——
    // 反向同理：在弹窗里输入只会让登录框的 UserName 绑定刷新，而 ForgotEmail 自己那份绑定
    // 因为「它就是发起方」本来也不需要刷新。所以当前用法下不会出问题；
    // 但若哪天有代码在弹窗打开时程序化地改 UserName，弹窗里的输入框不会同步。
    public string ForgotEmail
    {
        get => UserName;
        set => UserName = value;
    }

    // 复制按钮的临时状态。放在 Dialog 附近是为了提醒：它的生命周期由 Dialog 控制（关闭即复位）。
    private bool _isAdminCopied;

    /// <summary>「复制管理员邮箱」的临时状态（演示用，不真的写剪贴板）。</summary>
    // 写入点只有两处：CopyAdminCommand（置 true）与 Dialog setter（关闭时置 false）。
    // 因为 CopyAdminCommand 只置 true 不做翻转，重复点击是幂等的（Set 直接返回 false）。
    public bool IsAdminCopied
    {
        get => _isAdminCopied;
        private set
        {
            // 早退：值没变就不必通知 AdminCopyText。
            if (!Set(ref _isAdminCopied, value)) return;
            // 按钮文字由 AdminCopyText 计算得出，状态一变就要连带通知。
            OnPropertyChanged(nameof(AdminCopyText));
        }
    }

    // 绑在复制按钮的 Content 上，所以它一变按钮文字就变。
    // 注意「复制」与「已复制」相差一个字，按钮宽度会随之变化（它在 Auto 列里），
    // 于是名片中间那列被压缩、复制按钮向左长出一小截 —— 这是可接受的观感，但确实会动。
    public string AdminCopyText => IsAdminCopied ? "已复制" : "复制";

    // ===================== 模块切换 =====================
    // ── 两个重载的分工，以及 raise 参数到底是干什么的 ──────────────────────────
    // 调用点只有三处，分走两个重载：
    //   ① 单参：SelectModuleCommand 的 lambda（用户点了一级导航）；
    //   ② 双参：构造函数里的首屏初始化、Login() 成功回调，两处都传 raise: true。
    // 所以 raise 目前是个「恒为 true 的实参」。从命名推测设计意图是
    // 「true = 主动触发一次切换，false = 只做静默重算」，但实现里它只参与下面那句
    // `if (!raise && item is null) return;`；而 item 是非空类型 RailItem、调用方又都传 true，
    // 条件永远不成立 ⇒ 实际是死代码。保留而不删，是因为删掉要同步改三处调用方。
    // 两个重载真正的行为差异只有一条：单参版本多了 settings 特判。
    /// <summary>
    /// 单参重载：面向「用户点击一级导航」的场景。先拦截「设置」——它不是模块，
    /// 只是把 IsSettingsOpen 打开，因此不进入下面的切换逻辑；其余模块转调双参重载。
    /// </summary>
    // 谁调用：SelectModuleCommand 的命令体（o is RailItem item → SelectModule(item)）。
    // 前提状态：无。它对当前处于哪个模块、二级栏有没有内容都不作假设。
    // 做完之后：要么只开了设置浮层（settings 分支），要么整个模块切完（转调双参）。
    private void SelectModule(RailItem item)
    {
        // settings 特判：点「设置」时只开浮层，不碰 _moduleKey / 选中态 / 二级导航。
        // 现状提醒：RailItems[4] 的 Command 是 OpenSettingsCommand 而不是 SelectModuleCommand，
        // 所以这个分支实际永远进不来（详见构造函数里 OpenSettingsCommand 的注释）。
        if (item.IsSettings)
        {
            // 用赋值而不是切换：重复点「设置」不应该把浮层关掉。
            IsSettingsOpen = true;
            // 提前 return，避免落到下面的模块切换逻辑。
            return;
        }
        // 其余四个业务项：转调双参重载，raise 固定 true（表示「真的要切」）。
        SelectModule(item, true);
    }

    /// <summary>
    /// 双参重载：真正执行模块切换。两个重载的区别在于「入口职责」——
    /// 单参版本负责 settings 特判，双参版本只做切换本身，便于构造函数等内部场景直接调用。
    /// raise 参数名义上表示「是否允许推进切换」，实现里只用于下面的空值守卫；
    /// 目前两处调用都传 true，所以该守卫实际不会命中，属于预留参数。
    /// </summary>
    // 这是本类最长的方法，按步骤读：记 key → 刷 rail 选中 → 清二级导航 → 重置布局 →
    // 按模块填充二级导航 → 恢复选中并切换内容（SelectSubNav）。
    // 它会让四个集合/属性同时变化，但只有「集合」和「属性」两种刷新路径：
    //   SubNavEntries 靠 CollectionChanged 自动重建条目；
    //   SubNavTitle / ActiveView 靠各自的 Set 通知；
    //   三个布局属性靠下面那几条手动 OnPropertyChanged。
    private void SelectModule(RailItem item, bool raise)
    {
        // 守卫（死代码，原因见上方说明）。注意若它真的生效，是为了挡住 item 为 null 时
        // 下面 item.Key 的 NullReferenceException —— 也就是说它防的是「空引用」而不是「重复切换」。
        if (!raise && item is null) return;
        // 步骤 1：记下当前模块 key。目前没有读者（SubNavWidth 已退化为常量），属于预留状态。
        _moduleKey = item.Key;
        // 步骤 2：整组重算 rail 选中态。
        // 用引用比较而不是 key 比较，保证「只有被点的那一项」被选中，避免 key 重复时误选。
        // 遍历的是 RailItems（含第五项「设置」），所以设置项的 IsSelected 也会被顺带置 false。
        foreach (var r in RailItems) r.IsSelected = ReferenceEquals(r, item);

        // 步骤 3：清空二级导航整组，之后再按模块填充。
        // 二级导航整组重建：先清空再按模块填充，比逐项对比更新简单得多。
        // Clear 会发 CollectionChanged(Reset)，ItemsControl 立刻卸载全部子项；
        // 代价是滚动位置会回到顶部（条目都是新建的），收益是不用写「哪些留下、哪些删掉」的 diff 逻辑。
        SubNavEntries.Clear();

        // 步骤 4：重置二级栏的布局联动状态。
        // 下面三个都是读 _moduleKey 的计算属性，改完 key 必须挨个手动通知，否则宽度/头像不会变。
        // 现状补充：IsSubNavUserVisible 恒 true、SubNavWidth 恒 260、IsRailAvatarVisible 恒 false，
        // 所以这三条通知实际通知的都是「没变的值」，绑定重读后什么都不会发生；
        // IsSubNavVisible 已是 true，赋值同样被 Set 判等后静默返回。
        // 保留这一组的意义是：将来若要按模块设不同宽度，只需把常量换成读 _moduleKey 的表达式，
        // 通知链路已经就位。
        IsSubNavVisible = true;
        OnPropertyChanged(nameof(IsSubNavUserVisible));
        OnPropertyChanged(nameof(SubNavWidth));
        OnPropertyChanged(nameof(IsRailAvatarVisible));

        // 步骤 5：按模块填充二级导航与标题。
        // 注意这个 switch 只有 default 一个分支 —— 说明四个一级入口已经被合并成同一套内容
        // （四种页面共用一条侧栏），多模块差异化的分支结构留着，方便将来直接加 case。
        switch (item.Key)
        {
            // 所有 key（workbench / approval / clients / workflow）都会落到这里；
            // 若哪天新增了 key 却忘记加 case，也会静默走这套默认内容，不会报错。
            default:
                // 二级栏标题：从初始的 "管理" 改成 "工作区"，这是本属性唯一一次真正变化的赋值。
                SubNavTitle = "工作区";
                // ActiveView 换成 AdminViewModel，ShellView 的 DataTemplate 会按类型匹配到 AdminView。
                // 注意这里不区分 key：无论点哪一项，内容区都是同一个 AdminViewModel 实例，
                // 具体显示哪个页面由下面的 SelectSubNav → Admin.Select(key) 决定。
                ActiveView = Admin;
                // 四个页面入口与一级导航一一对应，点击侧栏与点击 rail 的效果一致。
                // 第四个参数是右侧徽标文案，null 表示不显示徽标（HasBadge=false）。
                // v2 起不再写死 "12"：待办数量由本地缓存决定，这里挂一个假数字会和审批页的「0 项」自相矛盾。
                AddSubNav("workbench", "工作台", Controls.Icons.Grid, null);
                AddSubNav("approval", "协作与审批", Controls.Icons.List, null);
                AddSubNav("clients", "客户工作空间", Controls.Icons.Users, null);
                AddSubNav("workflow", "Workflow", Controls.Icons.Trend, null);
                // 分组标题与链接混装：模板用 DataTemplate 区分渲染，分组不可点击。
                // 从这里开始进入「项目」区块，标题行只是视觉分节。
                SubNavEntries.Add(new SubNavGroupEntry("项目"));
                // emphasized 传 true，让当前项目以更大的字号字重突出显示。
                // 这一行用的是位置参数而不是命名参数：第三个 true 是 showIcon、第四个 true 是 emphasized，
                // 读的时候要对着 SubNavLinkEntry 的签名看，容易看错（同文件里的 thread-new 用了命名参数）。
                SubNavEntries.Add(new SubNavLinkEntry("customer-qinghe", "青禾 · 月度核对",
                    Controls.Icons.Folder, SubNavCommand, null, true, true));
                // 「8 月个税交叉验证」在这里出现两次：一次属于「项目」下的运行记录（thread-tax），
                // 一次属于「最近会话」（recent-tax）。key 不同、点击后走 AdminViewModel 的不同分支。
                AddRecent("thread-tax", "8 月个税交叉验证");
                // 「＋ 项目内新会话」是操作入口而不是页面，所以 showIcon:false（项目符号就是＋本身）、
                // height:34（比常规入口矮，视觉上更像一条动作而非一条导航）。
                SubNavEntries.Add(new SubNavLinkEntry("thread-new", "＋ 项目内新会话",
                    Controls.Icons.Plus, SubNavCommand, null, showIcon: false, emphasized: false, height: 34));
                // 第二个分组标题：到这里「项目」区块结束，「最近会话」区块开始。
                SubNavEntries.Add(new SubNavGroupEntry("最近会话"));
                // 三条同名的演示数据（"新的工作会话"）：文案相同但 key 不同，
                // 这样列表看起来有真实感，又不会因为 key 冲突导致选中错乱。
                AddRecent("recent-1", "新的工作会话");
                AddRecent("recent-2", "新的工作会话");
                AddRecent("recent-3", "新的工作会话");
                AddRecent("recent-tax", "8 月个税交叉验证");
                // 步骤 6：填充完毕后统一设置二级选中态（下面三行是对这一步的补充说明）。
                // 重建完成后恢复选中态：与被点击的一级导航保持一致。
                // 传的是 item.Key（一级导航的 key），所以二级列表里「key 相同的那一项」被选中 ——
                // 例如点 rail 的 "approval"，二级栏里的 "approval" 条目同时高亮。
                // 这一步还会向下调用 Admin.Select(key) 真正切换内容区页面。
                SelectSubNav(item.Key);
                // switch 只有一个分支，break 只是语法要求（C# 不允许 case 穿透）。
                break;
        }
    }

    /// <summary>「最近」条目的快捷构造：不显示图标、行高更矮（34），视觉上弱于常规入口。</summary>
    // 只包住「形态固定」的部分：key/title 由调用方给，其余全部写死。
    // 与 AddSubNav 的区别就是那两个参数（showIcon:false, height:34）；
    // 注意它仍然传了 Clock 图标，只是因为 showIcon=false 而不会被渲染出来 ——
    // 也就是说这个图标参数目前是「传了但不显示」，属于冗余实参。
    private void AddRecent(string key, string title)
    {
        // 直接 Add：集合变更事件会让 ItemsControl 为这一项创建模板，无需通知任何属性。
        SubNavEntries.Add(new SubNavLinkEntry(key, title, Controls.Icons.Clock, SubNavCommand,
            null, showIcon: false, emphasized: false, height: 34));
    }

    /// <summary>常规二级入口的快捷构造，只暴露业务关心的四个参数。</summary>
    // 剩下的参数全走默认值（showIcon:true, emphasized:false, height:38），
    // 所以 AddSubNav 造出来的都是「标准形态」的条目。
    private void AddSubNav(string key, string title, string icon, string? badge)
    {
        // 同样只 Add，不做选中处理 —— 选中态统一由 SelectSubNav 在填充完之后一次性设置。
        SubNavEntries.Add(new SubNavLinkEntry(key, title, icon, SubNavCommand, badge));
    }

    private ICommand? _subNavCommand;

    /// <summary>
    /// 二级导航共用一个命令（惰性创建，??= 只建一次）：靠 CommandParameter 传回被点的条目。
    /// 不在构造函数里建，是为了避免在字段初始化阶段捕获尚未赋值的成员。
    /// </summary>
    // ??= 的语义：左边为 null 时才求值右边并赋值，否则直接返回已有值 ——
    // 所以无论 AddSubNav 被调用多少次，命令实例只有一个（否则每个条目一个命令，纯属浪费）。
    // 为什么可以共享：命令本身不携带「点的是哪一项」，条目信息完全靠 CommandParameter 传进来，
    // 模板里写的是 CommandParameter="{Binding}"（整个 SubNavLinkEntry）。
    // 为什么惰性而不是构造函数里建：这个属性被 SubNavLinkEntry 的构造调用（经 AddSubNav），
    // 而 AddSubNav 又在构造函数里被调；惰性求值让「先有鸡还是先有蛋」的顺序问题自然消失。
    private ICommand SubNavCommand => _subNavCommand ??= new RelayCommand(o =>
    {
        // 类型模式匹配：参数不是 SubNavLinkEntry（例如误把分组标题传进来）就静默忽略，不抛异常。
        if (o is SubNavLinkEntry entry) SelectSubNav(entry.Key);
    });

    /// <summary>
    /// 同步二级导航的选中态并切换内容。
    /// 只遍历 SubNavLinkEntry：分组标题（SubNavGroupEntry）没有 Key，也不该被选中。
    /// </summary>
    // 谁调用：① SelectModule 填充完集合之后（首屏与每次切模块）；
    //          ② SubNavCommand 的命令体（用户点了侧栏里的某个链接）。
    // 两条路径最终都落到同一套逻辑，所以「点 rail」与「点侧栏」的效果完全一致。
    // 做完之后三件事：二级栏高亮、rail 高亮、内容区页面切换。
    private void SelectSubNav(string key)
    {
        // 步骤 1：整组重算选中态。OfType<SubNavLinkEntry>() 过滤掉分组标题 ——
        // 分组标题没有 Key 属性，若不过滤连编译都过不了（这正是它「不该被选中」的类型级保证）。
        // 用 == 比较 key：同一个 key 在集合里最多出现一次（构造时人工保证），所以不会多选。
        foreach (var entry in SubNavEntries.OfType<SubNavLinkEntry>())
            entry.IsSelected = entry.Key == key;

        // 步骤 2：让一级 rail 的高亮跟着走。反向映射表在 SyncRailSelection 里。
        SyncRailSelection(key);

        // 步骤 3：切换内容区页面。
        // 具体内容由 AdminViewModel 自己按 key 切换，壳层不参与。
        // 也就是说「当前显示哪个页面」有两层状态：壳层的 ActiveView 只决定用哪个 VM，
        // AdminViewModel 内部的 page 字段决定显示哪个子视图 —— 壳层不需要知道后者。
        Admin.Select(key);
    }

    /// <summary>让一级 rail 的高亮跟随当前页面：点侧栏入口或「项目 / 最近会话」时也要同步。</summary>
    // key → rail key 的映射表（左列是二级导航 / 项目 / 会话的 key，右列是 RailTop 里的 key）：
    //   "approval"                                    → "approval"   （审批）
    //   "workflow"                                    → "workflow"   （Workflow）
    //   "clients" / "customers" / "customer-qinghe"   → "clients"    （客户）
    //   "thread-tax" / "thread-new"                   → "clients"    （都属于客户项目下的会话）
    //   其它（含 "workbench"、"recent-*"）           → "workbench"  （兜底到工作台）
    //   注：AdminView 内部还有 "detail"、"detail-people" 等 key，但它们只传给 AdminViewModel.Select，
    //       不走这张表；若哪天也送进来，会同样落到 workbench 兜底分支。
    // 为什么要有这张表：二级导航里有很多「更细的 key」，它们共享同一个一级入口，
    // 若不翻译，rail 上就找不到 key 相同的那一项，高亮会全部熄灭。
    // "recent-*" 落到 workbench 是兜底的结果而不是刻意设计 —— 从「最近会话」点进去时，
    // rail 会跳回工作台高亮，与内容区的实际页面可能并不一致。
    private void SyncRailSelection(string key)
    {
        var railKey = key switch
        {
            "approval" => "approval",
            "workflow" => "workflow",
            "clients" or "customers" or "customer-qinghe" or "thread-tax" or "thread-new" => "clients",
            _ => "workbench"
        };

        // 只遍历 RailTop（前四项），因为 RailSettings 是「设置」入口，不参与页面高亮。
        // 这里用 string 比较（不像 SelectModule 用引用比较），是因为手上只有 key、没有对象。
        foreach (var item in RailTop) item.IsSelected = item.Key == railKey;
    }

    // ===================== 登录动作 =====================
    /// <summary>
    /// 登录动作（LoginCommand 的执行体）。这里没有真实后端，只做「校验 → 转圈 → 进主壳」三步。
    /// 关键在于用 DispatcherTimer 而不是 Thread.Sleep 模拟异步：前者回到 UI 线程执行回调，
    /// 期间界面仍可重绘，登录按钮也能正常变灰。
    /// </summary>
    // 更正摘要里最后半句：按钮不会变灰。CanExecute 确实变成了 false，但没人重新查询它
    // （RelayCommand 从不触发 CanExecuteChanged）—— 完整解释见构造函数里 LoginCommand 那一段，
    // 以及 LoginView 主按钮上方的注释。三处说法统一为「不生效」，不要按「会变灰」去理解代码。
    //
    // 完整时序（按发生顺序，含界面表现）：
    //   ① 用户点「登录」→ Button 派发 Click → ICommand.Execute → 进入本方法（同步，在 UI 线程）；
    //   ② 校验 UserName / Password：任一为空白就写 LoginError + 通知 + return，
    //      此时 IsBusy 仍是 false、计时器没建，界面只多出一行红字；
    //   ③ 校验通过：清空 LoginError 并通知（若本来就是空的，这行通知仍会发，因为没用 Set 判等）；
    //   ④ IsBusy = true：只改一个字段 + 发一条没人听的属性通知，界面无变化；
    //   ⑤ 新建 420ms 的 DispatcherTimer 并 Start；本方法随即返回，UI 线程继续处理消息、界面照常重绘；
    //   ⑥ 约 420ms 后 Tick 在 UI 线程触发：Stop → IsBusy=false → Stage=App → SelectModule(RailItems[0], true)；
    //   ⑦ Stage 变化让 MainWindow 的叠层切换：LoginView 隐藏、ShellView 显示；
    //      SelectModule 把内容重置到第一个模块的默认子页。
    // 两个已知副作用，读代码时要留意：
    //   · 不重置 Password / UserName / Dialog：登录成功再退出，登录页仍保留上次输入与上次的弹窗状态；
    //   · 没有防重入：因为按钮不变灰，420ms 内连点会创建多个计时器；
    //     每个计时器都会走一遍「Stage=App + SelectModule」，其中第二次之后的赋值
    //     会被 Set 判等后静默返回，唯一可见的副作用是二级导航集合被重复重建、选中态被重算
    //     （最终结果相同，用户基本看不出来）。
    private void Login()
    {
        // 步骤 1：校验账号与密码是否非空。
        // 只做最基础的非空校验；LoginError 是自动属性，赋值后必须手动发通知。
        // IsNullOrWhiteSpace 会把「全是空格」也当作空 —— 比 IsNullOrEmpty 更符合表单直觉。
        if (string.IsNullOrWhiteSpace(UserName) || string.IsNullOrWhiteSpace(Password))
        {
            // 两个字段共用一个提示文案：不为空的情况在演示里没必要区分「缺账号」还是「缺密码」。
            LoginError = "请输入账号与密码";
            OnPropertyChanged(nameof(LoginError));
            // 提前返回：既不改 IsBusy，也不建计时器，界面停在校验失败态。
            return;
        }

        // 步骤 2：清空上一次的错误文案，为这一次请求让出干净的界面。
        // 清掉上一次的错误提示，同样要手动通知，否则旧文案会一直留在界面上。
        // 注意这里是直接赋值（没有 Set 判等），所以哪怕原本就是空串也会白通知一次。
        LoginError = string.Empty;
        OnPropertyChanged(nameof(LoginError));
        // 步骤 3：进入忙碌态（意图是禁掉登录按钮，实际不生效，见下两行的澄清）。
        // 置 true 会让 CanExecute 变为 false，登录按钮在等待期间不可再点。
        // 说明：这句描述的是「CanExecute 的返回值」，不是「按钮的实际表现」——
        // 因为没有 CanExecuteChanged 通知，Button 不会重新查询，按钮既不灰也可点（见方法头）。
        IsBusy = true;
        // 步骤 4：用一次性计时器模拟一次 420ms 的请求耗时。
        // 420ms 只是为了让 loading 态可见，真实项目这里换成 await 一个服务调用。
        // 换成 await 后本方法要改成 async void，且 IsBusy 的复位要放进 finally；
        // 用计时器则天然不会忘记复位（除非进程在此期间退出）。
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) };
        timer.Tick += (_, _) =>
        {
            // 步骤 5：先停表。DispatcherTimer 不 Stop 会持续按间隔触发，这里只要一次。
            timer.Stop();
            // 步骤 6：复位忙碌标记。此时按钮本来也没被禁用过，这一步纯粹是为了状态自洽。
            IsBusy = false;
            // 步骤 7：切到 App 阶段（三条派生 bool 由 Stage setter 连带通知）。
            // 切到 App 阶段：IsLogin 变 false、IsApp 变 true，LoginView 隐藏、ShellView 显示。
            // 赋值顺序很重要：先切阶段再重置导航，避免 ShellView 刚显示时内容区还是空的。
            Stage = AppStage.App;
            // 步骤 7.5：登录成功是「值得记住」的那一刻 —— 把账号名与时间写进本地缓存。
            // 用 Save（同步落盘）而不是 SaveSoon：用户点完登录很可能马上就去点窗口关闭按钮，
            // 防抖窗口里没等到 Tick 的话这次「记住我」就白勾了。
            RememberLogin();
            // 步骤 8：把导航重置到首屏模块。
            // 进入主壳后重置一次一级导航，保证每次都从第一个模块的默认子页开始。
            // 传 true 与构造函数里那次一致（raise 参数实际不参与判断，见 SelectModule 的说明）。
            SelectModule(RailItems[0], true);
        };
        // 步骤 9：启动计时器。Start 之后本方法立刻结束，控制权交回消息循环。
        timer.Start();
    }

    // ===================== 本地缓存：登录记忆 =====================
    /// <summary>
    /// 启动时把「记住我」的账号回填到登录框。只读缓存，不写盘。
    /// </summary>
    // 调用点：构造函数步骤 0（早于所有命令与子 VM）。
    // 两个字段直接赋值而不是走属性：此刻还没有任何绑定挂在 VM 上（DataContext 在窗口构造后才赋），
    // 走 setter 会多发几条没人听的 PropertyChanged，还容易让人误以为「这里有联动逻辑」。
    //
    // 为什么不回填密码：缓存文件里压根没有密码字段（见 LoginCache 的说明）。
    // 所以「记住我」在这里的语义是「记住账号」，不是「免密码登录」——
    // 下次启动仍然要输密码，只是不用再打一遍账号。
    private void RestoreRememberedLogin()
    {
        var login = LocalCache.Data.Login;
        _remember = login.Remember;
        // 没勾「记住我」时缓存里的名字本来就是空的；这里仍加一层判空，
        // 是为了容忍手工编辑过的缓存（有名字但 remember 为 false 的矛盾状态）。
        if (login.Remember && !string.IsNullOrWhiteSpace(login.UserName))
            _userName = login.UserName;
    }

    /// <summary>
    /// 登录成功时写缓存：勾了「记住我」就存账号，没勾就存空串（顺带清掉上次留下的名字）。
    /// </summary>
    // 调用点：Login() 里 420ms 计时器的回调（步骤 7.5）。
    // 用同步 Save：这是「用户可感知的承诺」，必须当场落地。
    private void RememberLogin()
    {
        var login = LocalCache.Data.Login;
        login.Remember = Remember;
        login.UserName = Remember ? UserName.Trim() : string.Empty;
        login.LastLoginAt = DateTimeOffset.Now;
        LocalCache.Save();
    }
}
