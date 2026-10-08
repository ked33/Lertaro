using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Lertaro.Plugins.CoreExtensions.FileDialog;

// Native input is kept outside the completion loop so retries can be covered without sending desktop keys.
internal static class StandardDialogNavigation
{
    internal static bool Navigate(IntPtr hwnd, string target, Func<string?> readPath, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(target) || !Path.IsPathFullyQualified(target) || target.Contains('\0')) return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        bool IsCurrent() => !token.IsCancellationRequested && IsWindow(hwnd)
            && GetWindowThreadProcessId(hwnd, out var currentPid) != 0 && currentPid == pid
            && GetForegroundWindow() == hwnd && IsWindowEnabled(hwnd);

        if (!IsCurrent()) return false;
        if (!Path.EndsInDirectorySeparator(target))
        {
            // File results keep their established Open/Save semantics. Never guess a filename control
            // from enumeration order: address/search edits may precede it on modern Windows.
            var edit = FindEdit(hwnd, 1148);
            if (edit == IntPtr.Zero) edit = FindEdit(hwnd, 1001);
            for (var attempt = 0; attempt < 10 && IsCurrent(); attempt++)
            {
                if (ModifiersReleased() && FocusEdit(edit) && IsCurrent() && WriteAndReadBack(edit, target))
                    return IsCurrent() && ModifiersReleased() && HasFocus(edit) && SendKeys(0x0D);
                Thread.Sleep(50);
            }
            return false;
        }

        var addressRequested = false;
        bool Submit()
        {
            if (!IsCurrent() || !ModifiersReleased()) return false;
            var edit = FindEdit(hwnd, 0xA205, visibleOnly: true);
            if (edit == IntPtr.Zero || !HasFocus(edit))
            {
                // Ask only once; readiness is polled below. Repeated Alt+D would race a slow dialog.
                if (!addressRequested)
                {
                    addressRequested = SendKeys(0x12, 0x44); // Alt+D
                }
                return false;
            }
            if (!WriteAndReadBack(edit, target)) return false;
            return IsCurrent() && ModifiersReleased() && HasFocus(edit) && SendKeys(0x0D);
        }

        var result = NavigateFolder(target, IsCurrent, readPath, Submit, token);
        if (result && IsCurrent())
        {
            var edit = FindEdit(hwnd, 1148);
            if (edit == IntPtr.Zero) edit = FindEdit(hwnd, 1001);
            if (edit == IntPtr.Zero) edit = FindEdit(hwnd, 1152);
            // The address bar route never overwrites the user's filename.
            if (edit != IntPtr.Zero) FocusEdit(edit);
        }
        return result;
    }

    internal static bool NavigateFolder(string target, Func<bool> isCurrent, Func<string?> readPath,
        Func<bool> submit, CancellationToken token, Action<int>? wait = null)
    {
        wait ??= Thread.Sleep;
        var elapsed = Stopwatch.StartNew();
        var submitted = false;
        // Bounded activity for one navigation only; no timer survives completion/failure.
        for (var attempt = 0; attempt < 40 && elapsed.ElapsedMilliseconds < 2500; attempt++)
        {
            if (token.IsCancellationRequested || !isCurrent()) return false;
            if (PathsEqual(readPath(), target)) return !token.IsCancellationRequested && isCurrent();
            if (token.IsCancellationRequested || !isCurrent()) return false;
            if (!submitted) submitted = submit();
            wait(50);
        }
        // A submitted Enter is not evidence of navigation. Don't send another one on an unknown outcome.
        return false;
    }

    internal static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        try
        {
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
        catch (NotSupportedException) { return false; }
        catch (PathTooLongException) { return false; }
    }

    internal static IntPtr FindEdit(IntPtr hwnd, int id, bool visibleOnly = false)
    {
        var result = IntPtr.Zero;
        EnumChildWindows(hwnd, (child, _) =>
        {
            if (GetDlgCtrlID(child) != id || (visibleOnly && !IsWindowVisible(child))) return true;
            var name = new StringBuilder(32);
            GetClassName(child, name, name.Capacity);
            if (name.ToString() != "Edit") return true;
            result = child;
            return false;
        }, IntPtr.Zero);
        return result;
    }

    private static bool WriteAndReadBack(IntPtr edit, string text)
    {
        if (edit == IntPtr.Zero || !IsWindowEnabled(edit)) return false;
        if (SendMessageTimeout(edit, 0x000C /* WM_SETTEXT */, IntPtr.Zero, text, 0x0002, 150, out var written) == IntPtr.Zero
            || written == IntPtr.Zero) return false;
        var buffer = new StringBuilder(text.Length + 1);
        return SendMessageTimeout(edit, 0x000D /* WM_GETTEXT */, (IntPtr)buffer.Capacity, buffer, 0x0002, 150, out _) != IntPtr.Zero
            && string.Equals(buffer.ToString(), text, StringComparison.Ordinal);
    }

    private static bool ModifiersReleased() => (GetAsyncKeyState(0x10) & 0x8000) == 0
        && (GetAsyncKeyState(0x11) & 0x8000) == 0 && (GetAsyncKeyState(0x12) & 0x8000) == 0
        && (GetAsyncKeyState(0x5B) & 0x8000) == 0 && (GetAsyncKeyState(0x5C) & 0x8000) == 0;

    private static bool HasFocus(IntPtr edit)
    {
        if (edit == IntPtr.Zero) return false;
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(GetWindowThreadProcessId(edit, out _), ref info) && info.Focus == edit;
    }

    private static bool FocusEdit(IntPtr edit)
    {
        if (edit == IntPtr.Zero) return false;
        var targetThread = GetWindowThreadProcessId(edit, out _);
        var callerThread = GetCurrentThreadId();
        // Pool workers do not necessarily have an input queue yet; AttachThreadInput requires one.
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
        var attached = targetThread != callerThread && AttachThreadInput(callerThread, targetThread, true);
        try { SetFocus(edit); return HasFocus(edit); }
        finally { if (attached) AttachThreadInput(callerThread, targetThread, false); }
    }

    private static bool SendKeys(ushort key, ushort second = 0)
    {
        var inputs = second == 0
            ? new[] { Key(key), Key(key, up: true) }
            : new[] { Key(key), Key(second), Key(second, up: true), Key(key, up: true) };
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) == (uint)inputs.Length;
    }

    private static Input Key(ushort key, bool up = false) => new()
    {
        Type = 1,
        Data = new InputData { Keyboard = new KeyboardInput { VirtualKey = key, Flags = up ? 2u : 0u } }
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputData Data; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputData
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse; // INPUT's union size/alignment includes MOUSEINPUT on x86/x64.
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput { public ushort VirtualKey, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint Size, Flags;
        public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public int Left, Top, Right, Bottom;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Window;
        public uint Id;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }

    private delegate bool EnumChildProc(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr hwnd, EnumChildProc callback, IntPtr data);
    [DllImport("user32.dll")] private static extern int GetDlgCtrlID(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint caller, uint target, bool attach);
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr hwnd);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wParam, string text, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wParam, StringBuilder text, uint flags, uint timeout, out IntPtr result);
}
