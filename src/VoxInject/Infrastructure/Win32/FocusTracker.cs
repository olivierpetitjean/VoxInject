namespace VoxInject.Infrastructure.Win32;

/// <summary>
/// Captures the foreground window at hotkey press time, before any VoxInject UI
/// is shown. This handle is used by the text injection service to verify that
/// focus has not moved unexpectedly.
/// </summary>
public sealed class FocusTracker
{
    private IntPtr _targetHwnd;

    /// <summary>
    /// Must be called synchronously from the hotkey handler, before any await
    /// or UI operation, to capture the correct foreground window.
    /// </summary>
    public void Snapshot()
        => _targetHwnd = NativeMethods.GetForegroundWindow();

    public IntPtr Target => _targetHwnd;

    public void Clear() => _targetHwnd = IntPtr.Zero;
}
