namespace DeskBox.Tests;

/// <summary>
/// Pins the long-hidden icon release wiring (audit 2026-10-03 §7.3): item
/// models kept every LRU-evicted icon bitmap pinned for their lifetime, so a
/// large grid held tens of MB the icon cache believed it released. The fix
/// nulls only icons outside the render window — unrendered items are in no
/// ItemsSource, so the clear is visually inert at any moment, the rendered
/// page still paints instantly on reveal, and the tail rehydrates through
/// the existing scroll-driven hydration triggers.
/// </summary>
public sealed class FileItemIconLongHiddenReleaseContractTests
{
    [Fact]
    public void ViewModel_ReleasesOnlyIconsOutsideTheRenderWindow()
    {
        string hydration = ReadRepositoryFile(
            "src/DeskBox/ViewModels/WidgetViewModel.ItemHydration.cs");

        Assert.Contains(
            "internal int ReleaseUnrenderedItemIcons()",
            hydration,
            StringComparison.Ordinal);
        // The rendered set is the keep-list: members of RenderedItems must
        // keep their icons.
        Assert.Contains(
            "var rendered = new HashSet<WidgetItem>(RenderedItems);",
            hydration,
            StringComparison.Ordinal);
        Assert.Contains(
            "item.Icon is not null && !rendered.Contains(item)",
            hydration,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FileAdapter_ReleasesUnrenderedIconsAtTheModelLevelOnly()
    {
        string adapter = ReadRepositoryFile(
            "src/DeskBox/Controls/WidgetContents/FileWidgetContentAdapter.cs");

        // The call goes straight to the view model: the file surface must not
        // grow a long-hidden teardown path (pinned by
        // WidgetVisualActivityContractTests).
        Assert.Contains(
            "public override void OnWindowLongHidden()",
            adapter,
            StringComparison.Ordinal);
        Assert.Contains(
            "ViewModel.ReleaseUnrenderedItemIcons();",
            adapter,
            StringComparison.Ordinal);

        string surface = ReadRepositoryFile(
            "src/DeskBox/Controls/WidgetContents/FileSurfaceContent.xaml.cs");
        Assert.DoesNotContain(
            "OnWindowLongHidden()",
            surface,
            StringComparison.Ordinal);
    }

    [Fact]
    public void GroupContentParking_NotifiesLongHiddenSoParkedMembersReleaseToo()
    {
        // TryRetainGroupContent parks an inactive member while the host is
        // still visible; that call is the parked member's only long-hidden
        // notification, so losing it would keep parked tails pinned.
        string switching = ReadRepositoryFile(
            "src/DeskBox/Views/ContentWidgetWindow.ContentSwitching.cs");

        Assert.Contains(
            "content.OnWindowLongHidden();",
            switching,
            StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath) =>
        File.ReadAllText(TestPaths.FromRepository(relativePath));
}
