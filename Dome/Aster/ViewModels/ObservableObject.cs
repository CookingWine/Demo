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
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>触发变更通知。[CallerMemberName] 让调用方省去手写属性名，默认取所在成员的名称。</summary>
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>
    /// 赋值并在值真的变化时发出通知，是各 setter 的统一写法。
    /// 返回语义：false = 新值与旧值相等，字段未写入、未发通知；true = 已写入并发过通知。
    /// 用 EqualityComparer&lt;T&gt;.Default 而不是 !=，是为了同时对值类型/引用类型生效且不装箱。
    /// 调用方常写 if (!Set(...)) return; 再补充派生属性的连带通知（见 MainWindowViewModel.Stage）。
    /// </summary>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

// ---------- RelayCommand ----------
/// <summary>简单命令实现。</summary>
public sealed class RelayCommand : System.Windows.Input.ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    /// <summary>
    /// 无参重载：XAML 里 Command 传入的始终是 object?，
    /// 所以这里转调下面那个重载，把无参委托包一层丢弃参数，省去调用方写 _ =&gt; 语法。
    /// </summary>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    /// <summary>带参数重载：常用 o =&gt; { if (o is RailItem item) ... } 的写法，dataTemplate 里靠 CommandParameter 传条目。</summary>
    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    // Avalonia 订阅此事件来重新求值 CanExecute，从而刷新按钮的 IsEnabled。
    public event EventHandler? CanExecuteChanged;

    /// <summary>未提供 canExecute 时默认恒为可执行。</summary>
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    /// <summary>
    /// 手动通知「可执行状态可能变了」。本工程没有 CommandManager 式的自动重查机制，
    /// 若 CanExecute 依赖会变化的状态，就需要在状态变更处显式调用它。
    /// </summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
