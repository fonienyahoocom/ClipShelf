using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClipShelfCore;

internal sealed class BubbleForm : Form
{
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private IntPtr _beforeMenu;
    private bool _dragging;
    private bool _moved;
    private Point _mouseStart;
    private Point _windowStart;

    public event Action? OpenRequested;
    public event Action? ManageRequested;
    public event Action? HideRequested;
    public event Action? ExitRequested;
    public event Action<bool>? ModeChangeRequested;
    public event Action<int, int>? PositionCommitted;

    private readonly ToolStripMenuItem _normalModeItem;
    private readonly ToolStripMenuItem _nonInterferenceModeItem;

    public BubbleForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(66, 66);
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        DoubleBuffered = true;

        var menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem("開啟快捷選取");
        var manage = new ToolStripMenuItem("開啟完整管理");
        manage.Click += (_, _) => ManageRequested?.Invoke();
        open.Click += (_, _) => OpenRequested?.Invoke();

        var hide = new ToolStripMenuItem("真正隱藏（Ctrl+~）");
        hide.Click += (_, _) => HideRequested?.Invoke();

        var modeMenu = new ToolStripMenuItem("視窗切換模式");
        _normalModeItem = new ToolStripMenuItem("一般模式（工作列 / Alt+Tab）") { CheckOnClick = false };
        _normalModeItem.Click += (_, _) => ModeChangeRequested?.Invoke(false);
        _nonInterferenceModeItem = new ToolStripMenuItem("不干擾模式（浮動圖示）") { CheckOnClick = false };
        _nonInterferenceModeItem.Click += (_, _) => ModeChangeRequested?.Invoke(true);
        modeMenu.DropDownItems.Add(_normalModeItem);
        modeMenu.DropDownItems.Add(_nonInterferenceModeItem);

        var exit = new ToolStripMenuItem("離開 ClipShelf");
        exit.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.Add(open);
        menu.Items.Add(manage);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(hide);
        menu.Items.Add(modeMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);
        ContextMenuStrip = menu;
        // Temporary activation only while the native popup is open. The idle bubble
        // retains WS_EX_NOACTIVATE; no global hook or polling is installed.
        menu.Opening += (_, _) => {
            _beforeMenu = GetForegroundWindow();
            _dragging = false; Capture = false;
            SetWindowLong(Handle, -20, GetWindowLong(Handle, -20) & ~WS_EX_NOACTIVATE);
            SetForegroundWindow(Handle);
        };
        menu.Closed += (_, _) => {
            SetWindowLong(Handle, -20, GetWindowLong(Handle, -20) | WS_EX_NOACTIVATE);
            if (GetForegroundWindow() == Handle && _beforeMenu != IntPtr.Zero) SetForegroundWindow(_beforeMenu);
        };

        MouseDown += Bubble_MouseDown;
        MouseMove += Bubble_MouseMove;
        MouseUp += Bubble_MouseUp;
    }


    public void SetNonInterferenceMode(bool enabled)
    {
        _normalModeItem.Checked = !enabled;
        _nonInterferenceModeItem.Checked = enabled;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedRect(new Rectangle(4, 4, 58, 58), 18);
        using var fill = new SolidBrush(Color.FromArgb(15, 118, 110));
        using var border = new Pen(Color.FromArgb(213, 245, 239), 2);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);

        using var font = new Font("Segoe UI Symbol", 24, FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.White);
        var text = "▣";
        var size = e.Graphics.MeasureString(text, font);
        e.Graphics.DrawString(text, font, brush, (ClientSize.Width - size.Width) / 2f, 13);

        using var badgeBrush = new SolidBrush(Color.FromArgb(236, 254, 255));
        e.Graphics.FillEllipse(badgeBrush, 44, 44, 15, 15);
        using var badgeFont = new Font("Segoe UI", 8, FontStyle.Bold, GraphicsUnit.Pixel);
        using var badgeText = new SolidBrush(Color.FromArgb(15, 118, 110));
        e.Graphics.DrawString("C", badgeFont, badgeText, 48, 46);
    }

    public void ShowAtSavedOrDefault(bool hasSaved, double left, double top)
    {
        if (hasSaved && IsVisiblePosition((int)left, (int)top))
            Location = new Point((int)left, (int)top);
        else
        {
            var area = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(Cursor.Position);
            Location = new Point(area.Right - Width - 18, area.Bottom - Height - 18);
        }

        if (!Visible) Show();
        BringToFront();
    }

    private bool IsVisiblePosition(int x, int y)
    {
        var rect = new Rectangle(x, y, Width, Height);
        foreach (var screen in Screen.AllScreens)
            if (screen.WorkingArea.IntersectsWith(rect)) return true;
        return false;
    }

    private void Bubble_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        _moved = false;
        _mouseStart = Cursor.Position;
        _windowStart = Location;
        Capture = true;
    }

    private void Bubble_MouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var now = Cursor.Position;
        var dx = now.X - _mouseStart.X;
        var dy = now.Y - _mouseStart.Y;
        if (Math.Abs(dx) > 3 || Math.Abs(dy) > 3) _moved = true;
        Location = new Point(_windowStart.X + dx, _windowStart.Y + dy);
    }

    private void Bubble_MouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !_dragging) return;
        _dragging = false;
        Capture = false;
        if (_moved)
            PositionCommitted?.Invoke(Left, Top);
        else
            OpenRequested?.Invoke();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
