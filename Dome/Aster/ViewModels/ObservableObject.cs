// ============================================================================
// ObservableObject.cs —— MVVM 基础设施（本工程零第三方库，这里手写最小实现）
// 1) ObservableObject：INotifyPropertyChanged 的极薄封装，所有 VM 与条目模型都继承它；
// 2) RelayCommand：ICommand 的简化实现，把委托包装成可绑定到 Button.Command 的命令。
// 协作：Items.cs 的条目靠 Set<T> 刷新选中态，MainWindowViewModel 的 Stage 靠它切换页面。
// 坑：Set<T> 的返回值语义是「值是否真的变了」，忽略它会导致漏通知或重复通知。
// ============================================================================

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Aster.ViewModels;

// ---------- ObservableObject ----------
/// <summary>轻量 ObservableObject，避免引入额外依赖。</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    // 绑定引擎（Avalonia 的 Binding）订阅这个事件，收到后才会刷新对应控件。
    // 类型带 ?：没有订阅者时委托字段就是 null，所以下面发通知必须写成 ?.Invoke（空条件调用），
    // 否则第一个属性变更就会 NullReferenceException —— 而 VM 在还没挂到界面上时确实没有订阅者。
    // 订阅关系由绑定引擎在绑定建立时挂上、绑定解除时摘掉，VM 自己不需要维护订阅列表。
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>触发变更通知。[CallerMemberName] 让调用方省去手写属性名，默认取所在成员的名称。</summary>
    // ── [CallerMemberName] 到底在什么时候、被谁填进去 ────────────────────────────
    // 调用点不显式传 name 时（本工程所有调用都是这种），C# 编译器在生成 IL 的那一刻
    // 就把「包含这次调用的成员名」当成字符串常量压进第一个实参位置，
    // 等价于手写了 OnPropertyChanged("IsBoot")。由此可以推出三件事：
    //   1) 填写发生在编译期，运行期没有反射、没有栈回溯、不分配字符串，开销为零；
    //   2) 在属性 setter 里调用 → 填属性名。注意编译器取的是属性名（Stage），
    //      不是编译器生成的访问器名（set_Stage），所以绑定能正常命中；
    //   3) 在普通方法 / 构造函数 / 字段初始化器里调用 → 分别填方法名、".ctor"、".ctor"，
    //      这些名字不会命中任何属性，属于「发了通知但没人理」的静默失效。
    //      真要在方法里发某个属性的通知，必须显式写 nameof(Xxx)。
    // 参数默认值 null 有两个作用：让「不传」合法；允许显式传 null 表示「所有属性都可能变了」，
    // 此时 PropertyChangedEventArgs.PropertyName 为 null，绑定引擎会重读该对象上的全部绑定。
    // 方法名以 On 开头是 .NET 的「事件触发方法（raise method）」命名惯例：On + 事件名。
    // 本类没有派生类重写它，因此是普通 protected 方法而不是 virtual。
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>
    /// 赋值并在值真的变化时发出通知，是各 setter 的统一写法。
    /// 返回语义：false = 新值与旧值相等，字段未写入、未发通知；true = 已写入并发过通知。
    /// 用 EqualityComparer&lt;T&gt;.Default 而不是 !=，是为了同时对值类型/引用类型生效且不装箱。
    /// 调用方常写 if (!Set(...)) return; 再补充派生属性的连带通知（见 MainWindowViewModel.Stage）。
    /// </summary>
    // ── 返回值语义，逐条讲清（本类最容易误读的地方）──────────────────────────────
    //
    // 情形 A：EqualityComparer<T>.Default.Equals(field, value) 为 true
    //   ① 立即 return false，字段不写入（写进去也是同一个值，索性省掉）；
    //   ② PropertyChanged 不发，绑定不刷新，界面保持原样；
    //   ③ 调用方拿到的 false 表示「这次赋值什么都没发生」。
    // 情形 B：两者不相等
    //   ① 先把 value 写回 field（ref 参数直接改调用方的后备字段，不是副本）；
    //   ② 再 OnPropertyChanged(name) 发一次通知 —— 注意是「已经发出去了」；
    //   ③ 最后 return true。所以 true 的含义是「已写入且已通知完毕」，不是「待通知」。
    //
    // 由这两条推出两种正确用法：
    //   · 只关心「界面跟着变」→ 直接 Set(...)，返回值可以丢掉；
    //   · 还有派生属性要连带通知 → if (!Set(...)) return; 后面手写 OnPropertyChanged。
    //     Set 只负责 name 本身那一项，它不可能知道 PasswordChar / IsBoot 这些派生属性的存在。
    //
    // ── 为什么有人会误用成「if (Set(...)) { 再发派生属性通知 }」──────────────────
    // 这种正向写法本身不算错（值真变了才需要发派生通知），但有两个坑：
    //   a) 很容易写反成 if (Set(...)) return; —— 值变了反而提前退出，主流程只在「没变」时执行，
    //      症状是「改值没反应、改成相同的值倒执行了一堆逻辑」；
    //   b) 一旦在 if 体内顺带写别的副作用（比如复位 IsAdminCopied），值没变时整段被跳过，
    //      而调用方往往默认「setter 每次都会完整跑一遍」，于是出现难查的「偶发不生效」。
    // 本工程统一用反向写法 if (!Set(...)) return;：早退分支只处理「没变」，
    // 主流程天然就是「变了之后要做的全部事情」，不必在大括号里绕。
    //
    // ── 为什么用 EqualityComparer<T>.Default 而不是 field != value ───────────────
    //   1) 泛型 T 没有约束，C# 不允许对无约束类型参数用 != / ==，根本编译不过；
    //   2) 若退化成 (object)field != (object)value，每次比较都要装箱，属性一多就是持续 GC 压力；
    //   3) EqualityComparer<T>.Default 在 T 实现 IEquatable<T> 时走泛型特化（无装箱），
    //      否则回落 object.Equals，语义始终等于「该类型自己定义的相等」。
    //
    // ── 对值类型 / 引用类型分别意味着什么 ───────────────────────────────────────
    //   · 值类型（bool / double / 枚举）：调 T.Equals(T)，逐值比较。double 的 Equals 认为
    //     NaN 等于 NaN（而 == 运算符认为是 false），-0.0 也与 0.0 相等；
    //     自定义 struct 默认逐字段比较，字段一多就比较贵，故高频属性不要用大 struct。
    //   · 引用类型：默认是引用比较，换了实例就算「变了」；但 string 重写了 Equals 做逐字符比较，
    //     所以 UserName / Password 这类字符串属性「内容相同」即视为没变，重赋同样内容不会多刷一次。
    //     本工程的 ICommand / IBrush 等引用属性同理，只在实例真的被替换时才通知。
    //   · null 的对称性：两边都是 null 时 Equals 返回 true，所以把 null 赋给本来就是 null 的属性
    //     不会发通知 —— 这正是初始化阶段想要的效果，避免刷一屏无意义的通知。
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        // 步骤 1：先比较。相等就到此为止 —— 字段不改、通知不发、返回 false。
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        // 步骤 2：写入后备字段。这一行之后，属性 getter 读到的已经是新值。
        field = value;
        // 步骤 3：发通知。name 已由编译器按调用方成员名填好，这里不再判空。
        //         绑定引擎收到后立刻重读属性、把新值写进目标控件；
        //         但屏幕像素要等这一轮 UI 消息处理结束、渲染器跑下一次 Render 才更新。
        OnPropertyChanged(name);
        // 步骤 4：告诉调用方「已写入且已通知」，供其决定要不要补发派生属性。
        return true;
    }
}

// ---------- RelayCommand ----------
/// <summary>简单命令实现。</summary>
// 为什么手写而不是用 ReactiveCommand / CommunityToolkit：本工程零第三方依赖，
// ICommand 的契约只有三个成员（Execute / CanExecute / CanExecuteChanged），二十行足够。
// 为什么 sealed：它只做「委托转接」，没有任何可扩展点，密封能避免派生类改坏语义。
// 完整接口名 System.Windows.Input.ICommand 是历史命名空间（源自 WPF），Avalonia 沿用了同一套契约，
// 所以 WPF 的 MVVM 写法可以几乎原样搬过来。
public sealed class RelayCommand : System.Windows.Input.ICommand
{
    // 统一存成「带参数」的形态：ICommand.Execute(object? parameter) 的签名决定了底层只能这么存，
    // 无参委托由下面的重载在构造时包一层适配（见后面的说明）。
    // 两个字段都是 readonly：命令一旦建成，行为就固定，运行期换实现没有意义。
    private readonly Action<object?> _execute;
    // 可空：null 表示「不限制」，由 CanExecute 里的 ?? true 兜底。
    private readonly Func<object?, bool>? _canExecute;

    /// <summary>
    /// 无参重载：XAML 里 Command 传入的始终是 object?，
    /// 所以这里转调下面那个重载，把无参委托包一层丢弃参数，省去调用方写 _ =&gt; 语法。
    /// </summary>
    // ── 两个重载如何把 Action 适配成 Action<object?> ─────────────────────────────
    // 适配全部发生在构造期间，靠的是构造函数链 : this(...) 和两个「丢弃参数」的 lambda：
    //   Action execute      → _ => execute()         参数被扔掉，真正的 execute 在 Execute 时才跑；
    //   Func<bool>? canExec → _ => canExecute()      同理，查询可执行性时忽略传入的 parameter。
    // 这两个 lambda 在这里捕获形参 execute / canExecute（编译成闭包类字段），
    // 之后每次 Execute 都要多穿过这一层包装（一次间接调用，开销可忽略）。
    // 三元 canExecute is null ? null : ... 不能省：若直接写 _ => canExecute()，
    // CanExecute 里对 null 的解引用会在第一次求值时抛 NullReferenceException；
    // 保持 null 才能让 CanExecute 的 `?? true` 生效。
    // 重载决议示例：new RelayCommand(Login, () => !IsBusy) —— 方法组 Login 无参，
    // 无法转成 Action<object?>，因此唯一候选就是本重载，不存在歧义。
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
        // 空方法体：所有工作都在上面的构造函数链里完成（字段赋值由被调用的重载负责）。
        // 注意本重载不校验 execute 是否为 null —— 传 null 会在第一次 Execute 时崩，
        // 而不是在构造时崩，定位成本更高；调用方需自己保证非空。
    }

    /// <summary>带参数重载：常用 o =&gt; { if (o is RailItem item) ... } 的写法，dataTemplate 里靠 CommandParameter 传条目。</summary>
    // 这是真正干活的构造函数，无参重载最终都会链到这里。
    // parameter 的来源固定是 XAML 的 CommandParameter：ShellView 的 RailTemplate 写的是
    // CommandParameter="{Binding}"，把整个 RailItem / SubNavLinkEntry 带进来，
    // 于是 VM 只需要在命令体里写 if (o is RailItem item) 就能知道「点的是哪一项」。
    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        // 纯字段赋值，不调用、不校验：命令是「可执行动作的引用」，构造阶段没有副作用。
        _execute = execute;
        _canExecute = canExecute;
    }

    // Avalonia 订阅此事件来重新求值 CanExecute，从而刷新按钮的 IsEnabled。
    // 没人订阅时（本工程所有命令都是这种情况）该字段为 null，
    // RaiseCanExecuteChanged 里的 ?.Invoke 就是空操作：不抛异常，也不会「排队补发」。
    // 订阅时机由 Avalonia 控制：Button.Command 被赋值时订阅、换命令或卸载时退订，
    // 并在订阅的同时立刻查一次 CanExecute 决定初始 IsEnabled。
    public event EventHandler? CanExecuteChanged;

    /// <summary>未提供 canExecute 时默认恒为可执行。</summary>
    // 拆开看：_canExecute?.Invoke(...) 在字段为 null 时返回 bool? 的 null，?? true 把它兜成 true。
    // 所以「不传 canExecute」= 永远可点，与「传了但恒返回 true」在行为上等价，只是少一层调用。
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    // 直接转调，不做任何检查 —— 命令不负责「该不该执行」，那是 CanExecute 与视图的事。
    // 因此代码里如果直接 Execute，即使 CanExecute 返回 false 也照样执行；
    // 真正拦下点击的是 Button：IsEnabled=false 时它根本不派发 Click。
    public void Execute(object? parameter) => _execute(parameter);

    /// <summary>
    /// 手动通知「可执行状态可能变了」。本工程没有 CommandManager 式的自动重查机制，
    /// 若 CanExecute 依赖会变化的状态，就需要在状态变更处显式调用它。
    /// </summary>
    // 事件没人订阅时这里同样是空操作 —— 这正是 LoginCommand 的 CanExecute 形同虚设的原因：
    // 它在构造时传了 () => !IsBusy，但 IsBusy 每次变化都没有触发这个事件，
    // Button 的 IsEnabled 就冻结在首次查询的结果（true）上。详见 MainWindowViewModel 的构造函数。
    // 参数 EventArgs.Empty 是复用同一个静态实例，避免每次通知都 new 一个空对象。
    // 若要修好，只需在 IsBusy 的 setter 里补一行 RaiseCanExecuteChanged()（但 IsBusy 的 setter 拿不到
    // 强类型命令，得先把 ICommand 字段强转成 RelayCommand，这也是本工程把命令声明成 ICommand 的代价）。
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
