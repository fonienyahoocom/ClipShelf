using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ClipShelf;

internal static class PickerClipboard
{
    public static void Write(ClipItem item, bool plainText)
    {
        if (item.IsDeleted || item.IsPendingPurge) throw new InvalidOperationException("這筆內容已刪除。");
        if (plainText && item.Kind != "Text") throw new InvalidOperationException("純文字貼上只適用於文字。");
        if (item.Kind == "Text") Clipboard.SetText(item.Text);
        else if (item.Kind == "Files")
        {
            var files = item.Files.Where(p => File.Exists(p) || Directory.Exists(p)).Distinct().ToArray();
            if (files.Length != item.Files.Count || files.Length == 0) throw new FileNotFoundException("檔案已移動或不存在；請到完整管理檢查。");
            var list = new StringCollection(); list.AddRange(files); Clipboard.SetFileDropList(list);
        }
        else if (item.Kind == "Image" && File.Exists(item.ImagePath))
        {
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(item.ImagePath!, UriKind.Absolute); image.EndInit(); image.Freeze(); Clipboard.SetImage(image);
        }
        else throw new FileNotFoundException("這筆內容無法讀取；請到完整管理檢查。");
    }

    public static bool ValidTarget(IntPtr target, int pid)
    {
        if (target == IntPtr.Zero || !IsWindow(target)) return false;
        GetWindowThreadProcessId(target, out var current);
        if (current == Environment.ProcessId || current != pid) return false;
        try { using var process = Process.GetProcessById(pid); return process.ProcessName != "ClipShelf" && process.ProcessName != "ClipShelf.UI"; }
        catch { return false; }
    }

    public static async Task<string?> SendPasteAsync(IntPtr target, int pid)
    {
        if (!ValidTarget(target, pid)) return "原視窗已關閉或沒有貼上目標。內容已複製，可自行貼上。";
        // Never synthesize a Shift release (can switch Chinese IME). Wait for the
        // physical shortcut/Shift+Enter to finish before injecting Ctrl+V.
        for (var i = 0; i < 50 && ModifiersDown(); i++) await Task.Delay(10);
        if (ModifiersDown()) return "快捷鍵尚未放開；內容已複製，可自行貼上。";
        if (IsIconic(target)) ShowWindow(target, 9);
        SetForegroundWindow(target);
        for (var i = 0; i < 20 && GetForegroundWindow() != target; i++) await Task.Delay(10);
        if (!ValidTarget(target, pid) || GetForegroundWindow() != target)
            return "Windows 未允許恢復原視窗焦點。內容已複製，可自行貼上。";
        var inputs = new[] { Key(0x11, false), Key(0x56, false), Key(0x56, true), Key(0x11, true) };
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length)
            return "目標拒絕鍵盤輸入（可能有系統管理員權限差異）。內容已複製，可自行貼上。";
        return null; // Submitted to Windows; the target app does not acknowledge paste.
    }
    public static void RestoreTarget(IntPtr target, int pid)
    {
        if (ValidTarget(target, pid)) SetForegroundWindow(target);
    }
    private static bool ModifiersDown() => new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(k => (GetAsyncKeyState(k) & 0x8000) != 0);
    private static Input Key(ushort vk, bool up) => new() { Type = 1, Union = new InputUnion { Keyboard = new KeyboardInput { Vk = vk, Flags = up ? 2u : 0u } } };
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Union; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Vk, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
}
