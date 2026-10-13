using DeskBox.Helpers;

namespace DeskBox.Tests;

public sealed class TrayMenuWindowStyleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(0x00040000)]
    [InlineData(0x080C0008)]
    public void HelperStyleHidesSwitcherEntryAndPreservesUnrelatedFlags(int existing)
    {
        int result = ToolWindowStyle.GetExtendedStyle(existing);
        Assert.NotEqual(0, result & Win32Helper.WS_EX_TOOLWINDOW);
        Assert.Equal(0, result & 0x00040000);
        const int changedFlags = 0x00040000 | Win32Helper.WS_EX_TOOLWINDOW;
        Assert.Equal(existing & ~changedFlags, result & ~changedFlags);
        Assert.Equal(result, ToolWindowStyle.GetExtendedStyle(result));
    }

    [Fact]
    public void StartupUsesAnOwnedMenuHostWithoutTheShellDependentLibraryBackend()
    {
        string tray = File.ReadAllText(TestPaths.FromRepository("src/DeskBox/App.Tray.cs"));
        string host = File.ReadAllText(TestPaths.FromRepository("src/DeskBox/Views/TrayContextMenuHostWindow.cs"));
        Assert.Contains("new TrayContextMenuHostWindow(", tray, StringComparison.Ordinal);
        Assert.DoesNotContain("ContextMenuMode = ContextMenuMode.SecondWindow", tray, StringComparison.Ordinal);
        Assert.DoesNotContain("_trayIcon.ContextFlyout =", tray, StringComparison.Ordinal);
        Assert.Contains("ToolWindowStyle.Apply(_hwnd)", host, StringComparison.Ordinal);
        Assert.DoesNotContain(".IsShownInSwitchers =", host, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay", host, StringComparison.Ordinal);
        Assert.Contains("_menu.ShowAt(", host, StringComparison.Ordinal);
        Assert.Contains("Close();", host, StringComparison.Ordinal);
    }

    [Fact]
    public void OwnedMenuStartupRetryRetainsNativeFallbackAndStopsDuringShutdown()
    {
        string tray = File.ReadAllText(TestPaths.FromRepository("src/DeskBox/App.Tray.cs"));
        int start = tray.IndexOf("private bool TryCreateTrayContextMenuHost()", StringComparison.Ordinal);
        int end = tray.IndexOf("private MenuFlyoutItem CreateTrayCreateWidgetItem", start, StringComparison.Ordinal);
        string initialization = tray[start..end];

        Assert.Contains("IsShuttingDown", initialization, StringComparison.Ordinal);
        Assert.Contains("await Task.Delay(TrayCreationRetryDelay);", initialization, StringComparison.Ordinal);
        Assert.Contains("_trayContextMenuModePolicy.RecordFailure();", initialization, StringComparison.Ordinal);
        Assert.Contains("SwitchTrayContextMenuToNativeMode(trayIcon);", initialization, StringComparison.Ordinal);
        Assert.DoesNotContain("ContextFlyout =", initialization, StringComparison.Ordinal);
    }
}
