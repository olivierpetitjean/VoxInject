using System.Runtime.InteropServices;
using System.Windows;
using VoxInject.Infrastructure.Win32;

namespace VoxInject.Core.Services;

/// <summary>
/// Injects Unicode text into the currently focused window using SendInput with
/// KEYEVENTF_UNICODE — bypasses keyboard layout, supports all Unicode characters.
///
/// For strings longer than the clipboard threshold, falls back to clipboard + Ctrl+V
/// because some applications (browsers, Electron) handle pasted content more
/// reliably than simulated keystrokes.
/// </summary>
public sealed class SendInputTextInjectionService : ITextInjectionService
{
    public void Inject(string text, bool appendEnter = false)
    {
        if (string.IsNullOrEmpty(text)) return;
        InjectViaKeystrokes(text, appendEnter);
    }

    private static void InjectViaKeystrokes(string text, bool appendEnter)
    {
        // Each char needs keydown + keyup; surrogate pairs need 2 × 2 events
        var inputs = new List<NativeMethods.INPUT>(text.Length * 2 + 2);

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                // Surrogate pair — emit both UTF-16 code units
                AppendUnicodeKey(inputs, c,          keyDown: true);
                AppendUnicodeKey(inputs, text[i + 1], keyDown: true);
                AppendUnicodeKey(inputs, c,           keyDown: false);
                AppendUnicodeKey(inputs, text[i + 1], keyDown: false);
                i++;
            }
            else
            {
                AppendUnicodeKey(inputs, c, keyDown: true);
                AppendUnicodeKey(inputs, c, keyDown: false);
            }
        }

        if (appendEnter)
        {
            AppendVkKey(inputs, 0x0D, keyDown: true);   // VK_RETURN
            AppendVkKey(inputs, 0x0D, keyDown: false);
        }

        var arr      = inputs.ToArray();
        var cbSize   = Marshal.SizeOf<NativeMethods.INPUT>();
        var sent     = NativeMethods.SendInput((uint)arr.Length, arr, cbSize);
        var errCode  = Marshal.GetLastWin32Error();
        var elevated = IsElevated();
        VoxInject.Diagnostics.FileLogger.Log($"SendInput: cbSize={cbSize} requested={arr.Length} sent={sent} err={errCode} elevated={elevated} ptr={IntPtr.Size*8}bit");
    }

    private static void InjectViaClipboard(string text, bool appendEnter)
    {
        // Must run on STA thread — WPF apps are STA so this is fine
        var previous = string.Empty;
        try
        {
            previous = Clipboard.GetText();
        }
        catch { /* clipboard may be locked */ }

        try
        {
            Clipboard.SetText(text);

            // Ctrl+V
            var inputs = new NativeMethods.INPUT[4];
            inputs[0] = MakeVkInput(0x11, keyDown: true);   // VK_CTRL down
            inputs[1] = MakeVkInput(0x56, keyDown: true);   // 'V' down
            inputs[2] = MakeVkInput(0x56, keyDown: false);  // 'V' up
            inputs[3] = MakeVkInput(0x11, keyDown: false);  // VK_CTRL up

            var sent    = NativeMethods.SendInput(4, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
            var errCode = Marshal.GetLastWin32Error();
            VoxInject.Diagnostics.FileLogger.Log($"SendInput (clipboard Ctrl+V): requested=4 sent={sent} err={errCode}");

            if (appendEnter)
            {
                var enter = new[]
                {
                    MakeVkInput(0x0D, keyDown: true),
                    MakeVkInput(0x0D, keyDown: false)
                };
                NativeMethods.SendInput(2, enter, Marshal.SizeOf<NativeMethods.INPUT>());
            }

            // Restore clipboard after a brief delay (paste needs to complete first)
            var capture = previous;
            Task.Delay(500).ContinueWith(_ =>
            {
                try { Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(capture)); }
                catch { }
            });
        }
        catch (Exception ex)
        {
            VoxInject.Diagnostics.FileLogger.Log($"InjectViaClipboard FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void AppendUnicodeKey(List<NativeMethods.INPUT> list, char c, bool keyDown)
    {
        list.Add(new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            ki   = new NativeMethods.KEYBDINPUT
            {
                wVk     = 0,
                wScan   = c,
                dwFlags = keyDown
                    ? NativeMethods.KEYEVENTF_UNICODE
                    : NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP
            }
        });
    }

    private static void AppendVkKey(List<NativeMethods.INPUT> list, ushort vk, bool keyDown)
        => list.Add(MakeVkInput(vk, keyDown));

    private static bool IsElevated()
    {
        using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(id)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    private static NativeMethods.INPUT MakeVkInput(ushort vk, bool keyDown) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        ki   = new NativeMethods.KEYBDINPUT
        {
            wVk     = vk,
            wScan   = 0,
            dwFlags = keyDown ? 0u : NativeMethods.KEYEVENTF_KEYUP
        }
    };
}
