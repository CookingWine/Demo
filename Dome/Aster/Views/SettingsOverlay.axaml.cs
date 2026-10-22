// ============================================================================
// SettingsOverlay.axaml.cs —— 设置浮层的代码隐藏，只保留「点遮罩关闭」这一个交互
// 职责：把 XAML 里遮罩 Border 的 PointerPressed 事件转成 MainWindowViewModel 的关闭命令。
// 为什么放在代码隐藏：点击遮罩关闭是纯视图行为，用事件比在 VM 里暴露一个 MaskPressedCommand 更直接，
// 也不会让 SettingsViewModel 反过来依赖窗口的显隐状态。
// 关系：XAML 根节点 x:DataType 是 MainWindowViewModel（Settings 只是它的一个属性），
// 所以这里取 DataContext 时也按 MainWindowViewModel 判断类型，而不是 SettingsViewModel。
// 坑：本类是 partial，InitializeComponent 由 XAML 编译器生成，删掉这行调用会导致控件树为空。
//
// 补充：本类没有成员字段，也不持有 ViewModel。它之所以能拿到 VM，完全靠 DataContext 的属性继承——
// MainWindow.axaml 里 <views:SettingsOverlay IsVisible="{Binding IsSettingsOpen}" /> 是 MainWindow
// 的子元素，DataContext 从窗口一路继承下来，所以这里读到的永远是 MainWindowViewModel 实例。
// 由此带来一个约束：本控件不能单独放进别的窗口预览，否则 DataContext 类型不匹配，点遮罩不会有反应。
// 生命周期：随 MainWindow 的可视树创建一次；浮层「关闭」只是 IsVisible=false，本实例仍留在树上，
// 所以构造函数只会执行一次，事件处理器也不会被重复订阅。
// ============================================================================

using Avalonia.Controls;
using Avalonia.Input;
using Aster.ViewModels;

namespace Aster.Views;

/// <summary>
/// 设置浮层的视图外壳。面板内容全部由 XAML 里的隐式 DataTemplate 驱动，
/// 这里不持有任何业务状态，只负责把遮罩上的点击翻译成关闭命令。
/// </summary>
public partial class SettingsOverlay : UserControl
{
    // 唯一被执行的代码：InitializeComponent。
    // 谁调用：Avalonia 在解析 MainWindow.axaml、实例化 <views:SettingsOverlay /> 时调这个无参构造函数；
    //   没有别处 new 它，所以整个进程只跑一次。
    // 前置状态：无。
    // 做完之后：控件树已建好（含遮罩 Border 与 PointerPressed 的接线），但还没有 DataContext，
    //   也没有参与布局。此时 OnMaskPressed 即使被调用，也会因为 DataContext 不是 MainWindowViewModel 而什么都不做。
    // 谁会被通知：这一步没有任何属性通知；之后 Avalonia 会依次走 AttachedToVisualTree → Measure → Arrange → Render，
    //   在此期间 DataContext 从父级继承下来，绑定（含 IsVisible）才开始生效。
    public SettingsOverlay()
    {
        // 步骤 1：加载 XAML 并挂接事件处理器。这是编译期生成的方法，
        // 它内部 new 出整棵控件树、给遮罩 Border 的 PointerPressed 绑上 OnMaskPressed，
        // 并把 x:Name 字段（本类没有）填好。删掉这一行，控件会是空的。
        InitializeComponent();
    }

    /// <summary>
    /// 遮罩（XAML 里那个 #BD030407 的 Border）被按下时关闭浮层。
    /// 只绑在遮罩上，不绑在 Panel 上：Panel 会收到面板内部冒泡上来的点击，
    /// 那样点设置项也会顺手把浮层关掉；遮罩是面板的下层兄弟，只有点面板外的区域才会命中它。
    /// </summary>
    // 谁调用它：Avalonia 的输入系统。用户在遮罩区域按下指针时，命中测试落到遮罩 Border，
    //   该 Border 的 PointerPressed 事件触发，路由到本方法（XAML 里 PointerPressed="OnMaskPressed"）。
    // 前提：DataContext 必须是 MainWindowViewModel（正常运行时由 MainWindow 继承而来）。
    // 做完之后：IsSettingsOpen 变成 false，浮层的 IsVisible 绑定失效 → 整块从视觉树里隐藏（不是销毁），
    //   本实例与 SettingsViewModel 里的所有状态都留在内存中，下次打开还是原样。
    // 谁被通知：MainWindowViewModel.IsSettingsOpen 的 Set 发出 PropertyChanged，
    //   MainWindow.axaml 上 <views:SettingsOverlay IsVisible="{Binding IsSettingsOpen}" /> 收到后隐藏本控件。
    //   没有别的订阅者，所以关闭动作不会连锁触发别的界面变化。
    private void OnMaskPressed(object? sender, PointerPressedEventArgs e)
    {
        // 走命令而不是直接改 IsSettingsOpen：关闭入口与标题栏的关闭按钮、底部的「完成」保持同一条路径，
        // 将来关闭时要附带副作用（比如落盘），只改命令实现即可
        //
        // 步骤 1：从 DataContext 取 VM，并且只认 MainWindowViewModel。判断它而不是 SettingsViewModel 的原因：
        //   a) 本控件在 XAML 里的 x:DataType 就是 MainWindowViewModel，DataContext 从窗口继承下来；
        //   b) 真正持有 IsSettingsOpen 的是 MainWindowViewModel，SettingsViewModel 里没有这个字段，
        //      它连「浮层是否打开」都不知道（见 SettingsViewModel 的类注释）；
        //   c) `is MainWindowViewModel vm` 同时完成类型检查与转型，类型不符时安静地什么都不做——
        //      在设计器预览、或本控件被误挂到别的 DataContext 下时，这是最安全的降级，
        //      不会抛 InvalidCastException 把整个界面带崩。
        // 步骤 2：执行关闭命令。这里没有先问 CanExecute：
        //   CloseSettingsCommand 是无条件 RelayCommand（CanExecute 恒为 true），问了也是白问；
        //   若将来给它加上条件，这里必须改成先 CanExecute 再 Execute，否则会出现
        //   「按钮看起来能点、点了却没效果甚至抛异常」的错位。
        if (DataContext is MainWindowViewModel vm) vm.CloseSettingsCommand.Execute(null);

        // 补充一：为什么不直接写 vm.IsSettingsOpen = false。
        //   1) 关闭动作目前有三个入口（遮罩、标题栏关闭按钮、底部「完成」），走同一个命令才能保证行为一致；
        //   2) IsSettingsOpen 的 setter 表达的是「显示状态」，不是「关闭动作」；
        //      以后关闭时要停轮询、落盘、恢复焦点，这些副作用该写在命令里而不是 setter 里；
        //   3) 代码隐藏只依赖 ICommand 接口，不依赖 VM 的具体属性名，VM 内部改名时这里不用动。
        // 补充二：sender 与 e 都没有被使用。
        //   sender 就是那个遮罩 Border，e.Source 是命中的子元素（遮罩没有子元素，所以两者相同）；
        //   点在遮罩的哪个位置处理都一样，因此不需要读它们。
        // 补充三：没有写 e.Handled = true。
        //   事件会继续冒泡到窗口根节点；根节点目前没有监听 PointerPressed（只有四边八角的
        //   拖拽热区各自监听，且它们不在本控件的冒泡链上），所以没有副作用。
        //   若以后根节点加了「点空白处做某事」的逻辑，这里需要补上 Handled，否则一次点击会被处理两次。
    }
}
