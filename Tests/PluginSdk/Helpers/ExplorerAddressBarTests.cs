using System.Runtime.InteropServices;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.PluginSdk.Tests.Helpers;

// Real off-screen native windows exercise the fallback when no ShellWindows entry represents them.
// Registration and the WM_GETTEXT callback are process-wide, so these tests run serially.
[TestClass]
[DoNotParallelize]
public sealed class ExplorerAddressBarTests
{
    private static readonly WindowProc Procedure = WindowProcedure;
    private static readonly Lazy<bool> Classes = new(RegisterClasses);
    private static Action? _beforeTextRead;

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void AnUnregisteredExplorerReadsItsSharedFrameAddressBar(bool tabbed)
    {
        using var window = new ExplorerWindow(tabbed);
        window.AddAddress(@"地址: D:\Current folder");
        Assert.AreEqual(@"D:\Current folder", ExplorerFolderPathReader.Read(window.Handle, window.Tab));
        Assert.IsNull(ExplorerFolderPathReader.Read(window.Handle, window.Tab, "Hovered folder"),
            "A missing hovered item must not resolve to the address bar's parent folder.");
    }

    [TestMethod]
    public void HiddenAddressBarsCannotReplaceTheVisibleDirectory()
    {
        using var window = new ExplorerWindow(true);
        window.AddAddress(@"Address: C:\Old", visible: false);
        window.AddAddress(@"Address: D:\Current");
        Assert.AreEqual(@"D:\Current", ExplorerFolderPathReader.Read(window.Handle, window.Tab));
    }

    [TestMethod]
    public void MultipleVisibleAddressBarsAreAmbiguous()
    {
        using var window = new ExplorerWindow(true);
        window.AddAddress(@"Address: C:\First");
        window.AddAddress(@"Address: D:\Second");
        Assert.IsNull(ExplorerFolderPathReader.Read(window.Handle, window.Tab));
    }

    [TestMethod]
    public void ATabFromAnotherWindowCannotSelectAnAddressBar()
    {
        using var first = new ExplorerWindow(true);
        using var second = new ExplorerWindow(true);
        first.AddAddress(@"Address: C:\First");
        second.AddAddress(@"Address: D:\Second");
        Assert.IsNull(ExplorerFolderPathReader.Read(first.Handle, second.Tab));
    }

    [TestMethod]
    public void ChangingTabsDuringTheReadDiscardsTheOldDirectory()
    {
        using var window = new ExplorerWindow(true);
        window.AddAddress(@"Address: C:\Old tab");
        _beforeTextRead = () => ShowWindow(window.Tab, 0 /* SW_HIDE */);
        try
        {
            Assert.IsNull(ExplorerFolderPathReader.Read(window.Handle, window.Tab));
            Assert.AreEqual(IntPtr.Zero, ExplorerFolderPathReader.GetActiveTab(window.Handle),
                "The tab must have changed inside WM_GETTEXT, not before the read began.");
        }
        finally { _beforeTextRead = null; }
    }

    [TestMethod]
    public void CancellationDuringTheReadDiscardsTheDirectory()
    {
        using var window = new ExplorerWindow(true);
        using var cancellation = new CancellationTokenSource();
        window.AddAddress(@"Address: C:\Current");
        _beforeTextRead = cancellation.Cancel;
        try
        {
            Assert.IsNull(ExplorerFolderPathReader.Read(window.Handle, window.Tab, null, cancellation.Token));
            Assert.IsTrue(cancellation.IsCancellationRequested, "The read must reach WM_GETTEXT before cancellation.");
        }
        finally { _beforeTextRead = null; }
    }

    [TestMethod]
    public void TruncatedAddressTextNeverBecomesANavigationTarget()
    {
        using var window = new ExplorerWindow(false);
        window.AddAddress(@"Address: C:\" + new string('a', 32768));
        Assert.IsNull(ExplorerFolderPathReader.Read(window.Handle, window.Tab));
    }

    [TestMethod]
    public void AClosedExplorerCannotAnswerWithAReusedCachedDirectory()
    {
        var window = new ExplorerWindow(true);
        window.AddAddress(@"Address: D:\Current");
        var hwnd = window.Handle;
        var tab = window.Tab;
        window.Dispose();
        Assert.IsNull(ExplorerFolderPathReader.Read(hwnd, tab));
    }

    private sealed class ExplorerWindow : IDisposable
    {
        public IntPtr Handle { get; }
        public IntPtr Tab { get; }

        public ExplorerWindow(bool tabbed)
        {
            Assert.IsTrue(Classes.Value);
            // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW, off-screen: visible to Win32 without taking focus.
            Handle = CreateWindow("CabinetWClass", null, IntPtr.Zero, visible: true);
            if (tabbed) Tab = CreateWindow("ShellTabWindowClass", null, Handle, visible: true);
        }

        public void AddAddress(string text, bool visible = true)
        {
            var parent = CreateWindow("Breadcrumb Parent", null, Handle, visible);
            CreateWindow("ToolbarWindow32", text, parent, visible);
        }

        public void Dispose() => DestroyWindow(Handle);
    }

    private static IntPtr CreateWindow(string className, string? text, IntPtr parent, bool visible)
    {
        var style = parent == IntPtr.Zero ? unchecked((int)0x80000000) /* WS_POPUP */ : 0x40000000 /* WS_CHILD */;
        if (visible) style |= 0x10000000;
        var hwnd = CreateWindowEx(0x08000080, className, text, style, -32000, -32000, 1, 1,
            parent, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
        Assert.AreNotEqual(IntPtr.Zero, hwnd, $"Could not create {className}: {Marshal.GetLastWin32Error()}");
        return hwnd;
    }

    private static bool RegisterClasses()
    {
        foreach (var name in new[] { "CabinetWClass", "ShellTabWindowClass", "Breadcrumb Parent", "ToolbarWindow32" })
        {
            var definition = new WindowClass
            {
                Procedure = Marshal.GetFunctionPointerForDelegate(Procedure),
                Instance = GetModuleHandle(null),
                ClassName = name,
            };
            Assert.AreNotEqual((ushort)0, RegisterClass(ref definition), $"Could not register {name}");
        }
        return true;
    }

    private static IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == 0x000D) _beforeTextRead?.Invoke();
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Style;
        public IntPtr Procedure;
        public int ClassExtra, WindowExtra;
        public IntPtr Instance, Icon, Cursor, Background;
        public string? MenuName;
        public string ClassName;
    }

    private delegate IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClass(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int extendedStyle, string className, string? title, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
