using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace ClipShelf;

public partial class MiniWindow : Window
{
    public event Action? RestoreRequested;
    public event Action? HideRequested;
    public event Action? ExitRequested;
    public event Action<double, double>? PositionChangedByUser;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    public MiniWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ExcludeBubbleFromTaskSwitcher();
    }

    private void ExcludeBubbleFromTaskSwitcher()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            var style = GetWindowLong(hwnd, GWL_EXSTYLE);
            style |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            style &= ~WS_EX_APPWINDOW;
            SetWindowLong(hwnd, GWL_EXSTYLE, style);
        }
        catch { }
    }

    public void ShowAtSavedOrDefault(bool hasSavedPosition, double savedLeft, double savedTop)
    {
        if (!IsVisible) Show();
        ExcludeBubbleFromTaskSwitcher();

        if (hasSavedPosition && IsPositionOnScreen(savedLeft, savedTop))
        {
            Left = savedLeft;
            Top = savedTop;
        }
        else
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - Width - 18;
            Top = area.Bottom - Height - 18;
        }
    }

    private bool IsPositionOnScreen(double left, double top)
    {
        return left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 20 &&
               top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 20 &&
               left + Width > SystemParameters.VirtualScreenLeft + 20 &&
               top + Height > SystemParameters.VirtualScreenTop + 20;
    }

    private void Bubble_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        var startLeft = Left;
        var startTop = Top;
        try { DragMove(); } catch { }

        var moved = Math.Abs(Left - startLeft) > 3 || Math.Abs(Top - startTop) > 3;
        if (moved)
            PositionChangedByUser?.Invoke(Left, Top);
        else
            RestoreRequested?.Invoke();

        e.Handled = true;
    }

    private void Bubble_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();
        var open = new MenuItem { Header = "打開 ClipShelf" };
        open.Click += (_, _) => RestoreRequested?.Invoke();
        var hide = new MenuItem { Header = "真正隱藏（Ctrl+~）" };
        hide.Click += (_, _) => HideRequested?.Invoke();
        var exit = new MenuItem { Header = "離開程式" };
        exit.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.Add(open);
        menu.Items.Add(hide);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);
        menu.IsOpen = true;
        e.Handled = true;
    }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

}
