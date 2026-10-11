using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClipShelfCore;

internal sealed class NativeMessageWindow : NativeWindow, IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private const int HOTKEY_ID = 0x4353;
    private const uint MOD_CONTROL = 0x0002;
    private const uint VK_OEM_3 = 0xC0;

    public event Action? HotkeyPressed;
    public event Action? ClipboardChanged;

    public NativeMessageWindow()
    {
        CreateHandle(new CreateParams { Caption = "ClipShelf.Core.MessageWindow" });
        AddClipboardFormatListener(Handle);

    }

    public bool HotkeyRegistered { get; private set; }
    private uint _modifiers, _key;
    public bool ConfigureHotkey(uint modifiers, uint key)
    {
        if (HotkeyRegistered && modifiers == _modifiers && key == _key) return true;
        if (HotkeyRegistered) UnregisterHotKey(Handle, HOTKEY_ID);
        _modifiers = modifiers; _key = key;
        HotkeyRegistered = RegisterHotKey(Handle, HOTKEY_ID, modifiers | 0x4000, key); // MOD_NOREPEAT
        return HotkeyRegistered;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
            HotkeyPressed?.Invoke();
        else if (m.Msg == WM_CLIPBOARDUPDATE)
            ClipboardChanged?.Invoke();

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero)
        {
            try { UnregisterHotKey(Handle, HOTKEY_ID); } catch { }
            try { RemoveClipboardFormatListener(Handle); } catch { }
            DestroyHandle();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
}
