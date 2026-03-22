using System.Runtime.InteropServices;

namespace VoxInject.Infrastructure.Win32;

/// <summary>
/// P/Invoke declarations only. No logic here.
/// </summary>
internal static class NativeMethods
{
    // ── Window styles ──────────────────────────────────────────────────────────
    public const int GWL_EXSTYLE       = -20;
    public const int WS_EX_NOACTIVATE  = 0x08000000;
    public const int WS_EX_TOOLWINDOW  = 0x00000080;

    // ── Messages ───────────────────────────────────────────────────────────────
    public const int WM_HOTKEY    = 0x0312;
    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYUP     = 0x0101;
    public const int WM_SYSKEYUP  = 0x0105;

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint   vkCode;
        public uint   scanCode;
        public uint   flags;
        public uint   time;
        public IntPtr dwExtraInfo;
    }

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn,
                                                  IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    // ── Hotkey modifiers ───────────────────────────────────────────────────────
    public const uint MOD_ALT      = 0x0001;
    public const uint MOD_CTRL     = 0x0002;
    public const uint MOD_SHIFT    = 0x0004;
    public const uint MOD_WIN      = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    // ── SendInput ──────────────────────────────────────────────────────────────
    public const uint KEYEVENTF_KEYUP   = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;
    public const uint INPUT_KEYBOARD    = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint   dwFlags;
        public uint   time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int    dx, dy, mouseData;
        public uint   dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HARDWAREINPUT
    {
        public uint   uMsg;
        public ushort wParamL, wParamH;
    }

    // On 64-bit Windows the union sits at offset 8 (4 bytes type + 4 bytes
    // padding to align ULONG_PTR). FieldOffset(4) is the common copy-paste
    // bug that causes the wrong VK code to be sent (our dwFlags value is
    // misread as wVk, injecting middle-mouse-button events instead of text).
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct INPUT
    {
        [FieldOffset(0)]  public uint          type;
        [FieldOffset(8)]  public MOUSEINPUT    mi;
        [FieldOffset(8)]  public KEYBDINPUT    ki;
        [FieldOffset(8)]  public HARDWAREINPUT hi;
    }

    // ── Imports ────────────────────────────────────────────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    // ── DPI / Monitor ──────────────────────────────────────────────────────────
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(System.Drawing.Point pt, uint dwFlags);

    // MDT_EFFECTIVE_DPI = 0
    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType,
                                              out uint dpiX, out uint dpiY);
}
