using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace VoxInject.Infrastructure.Win32;

/// <summary>
/// Registers a global hotkey via a hidden message-only HwndSource.
/// Key-release detection (for push-to-talk) uses a WH_KEYBOARD_LL hook because
/// RegisterHotKey only delivers WM_HOTKEY on press, never on release.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 9001;

    private HwndSource? _source;
    private uint        _currentMods;
    private uint        _currentVk;
    private bool        _disposed;

    // Low-level keyboard hook for release detection
    private IntPtr                          _llHook = IntPtr.Zero;
    private NativeMethods.LowLevelKeyboardProc? _llProc; // keep delegate alive

    /// <summary>Fires when the registered hotkey is pressed.</summary>
    public event Action? HotkeyPressed;

    /// <summary>Fires when the hotkey key is released (for push-to-talk).</summary>
    public event Action? HotkeyReleased;

    public void Register(uint modifiers, uint vk)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // If same hotkey, nothing to do
        if (_source != null && _currentMods == modifiers && _currentVk == vk)
            return;

        Unregister();

        _source = new HwndSource(new HwndSourceParameters("VoxInject-HotkeyWindow")
        {
            // Message-only window: not visible, not in Alt+Tab
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
            Width  = 0,
            Height = 0
        });
        _source.AddHook(WndProc);

        // MOD_NOREPEAT prevents repeated firing while key is held
        var result = NativeMethods.RegisterHotKey(
            _source.Handle, HotkeyId,
            modifiers | NativeMethods.MOD_NOREPEAT,
            vk);

        if (!result)
            throw new InvalidOperationException(
                $"RegisterHotKey failed (error {Marshal.GetLastWin32Error()}). " +
                "The hotkey may be in use by another application.");

        _currentMods = modifiers;
        _currentVk   = vk;

        // Install low-level keyboard hook to detect key-up
        _llProc = LowLevelKeyboardHook;
        var hMod = NativeMethods.GetModuleHandle(null);
        _llHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _llProc, hMod, 0);
    }

    public void Unregister()
    {
        if (_source is null) return;

        if (_llHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_llHook);
            _llHook = IntPtr.Zero;
            _llProc = null;
        }

        NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        _source.RemoveHook(WndProc);
        _source.Dispose();
        _source = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private IntPtr LowLevelKeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var msg = wParam.ToInt32();
            if (msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP)
            {
                var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                if (data.vkCode == _currentVk)
                    HotkeyReleased?.Invoke();
            }
        }
        return NativeMethods.CallNextHookEx(_llHook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unregister();
    }
}
