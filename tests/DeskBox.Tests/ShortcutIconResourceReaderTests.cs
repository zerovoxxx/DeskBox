using System.Buffers.Binary;
using System.Drawing;
using System.Reflection;
using System.Text;
using DeskBox.Helpers;

namespace DeskBox.Tests;

public sealed class ShortcutIconResourceReaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"DeskBox-shortcut-icon-{Guid.NewGuid():N}");

    public ShortcutIconResourceReaderTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData("icons/custom.ico", -238, true)]
    [InlineData("ParadoxIcon", 0, true)]
    [InlineData("icons/custom.dll", 3, false)]
    public void PersistedIcon_PreservesResourcePathAndSignedIndex(string path, int index, bool unicode)
    {
        var resource = ShortcutIconResourceReader.TryParse(CreateLink(path, index, unicode));
        Assert.NotNull(resource);
        Assert.Equal(path, resource.Value.Path);
        Assert.Equal(index, resource.Value.Index);
    }

    [Fact]
    public void EnvironmentIconBlock_TakesPrecedenceOverPreviouslyExpandedPath()
    {
        var resource = ShortcutIconResourceReader.TryParse(CreateLink(
            @"C:\OldMachine\Hyper-V\SnapInAbout.dll", -1, true,
            @"%ProgramFiles%\Hyper-V\SnapInAbout.dll"));
        Assert.Equal(@"%ProgramFiles%\Hyper-V\SnapInAbout.dll", resource?.Path);
        Assert.Equal(-1, resource?.Index);
    }

    [Fact]
    public void MalformedLink_RejectsTruncatedStringsAndOutOfRangeBlocks()
    {
        byte[] data = CreateLink("icon.ico", 0, true);
        Assert.Null(ShortcutIconResourceReader.TryParse(data.AsSpan(0, 60)));
        Assert.Null(ShortcutIconResourceReader.TryParse(data.AsSpan(0, data.Length - 8)));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20), 0x82);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(76), uint.MaxValue);
        Assert.Null(ShortcutIconResourceReader.TryParse(data));
        data[0] = 0;
        Assert.Null(ShortcutIconResourceReader.TryParse(data));
    }

    [Fact]
    public void ExplicitResource_BypassesIncompleteShellMetadataCachedAtLogon()
    {
        string iconPath = Path.Combine(_directory, "ParadoxIcon");
        File.Copy(TestPaths.FromRepository("src/DeskBox/Assets/deskbox.ico"), iconPath);
        string shortcutPath = Path.Combine(_directory, "launcher.lnk");
        File.WriteAllBytes(shortcutPath, CreateLink(iconPath, 0, true));
        var fileInfo = new FileInfo(shortcutPath);
        var partialMetadata = new ShortcutInfo("wrong-target.exe", "", "", "", "", 0);
        Type cacheEntryType = typeof(ShortcutHelper).GetNestedType("StoredShortcutCacheEntry", BindingFlags.NonPublic)!;
        object entry = Activator.CreateInstance(cacheEntryType, fileInfo.Length,
            fileInfo.LastWriteTimeUtc.Ticks, partialMetadata, Environment.TickCount64)!;
        typeof(ShortcutHelper).GetMethod("StoreStoredMetadataCacheEntry", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [shortcutPath, entry]);
        try
        {
            object source = Resolve(shortcutPath);
            Assert.Equal(iconPath, Property<string>(source, "Path"));
            Assert.True(Property<bool>(source, "UsesExplicitIconIndex"));
            Assert.False(Property<bool>(source, "IsTransientFallback"));
        }
        finally
        {
            ShortcutHelper.InvalidateStoredMetadataCache(shortcutPath);
        }
    }

    [Fact]
    public void MissingExplicitResource_RemainsRetryableAndRecoversWithoutChangingLink()
    {
        string iconPath = Path.Combine(_directory, "unavailable.ico");
        string shortcutPath = Path.Combine(_directory, "Hyper-V Manager.lnk");
        File.WriteAllBytes(shortcutPath, CreateLink(iconPath, 0, true));
        byte[] originalLink = File.ReadAllBytes(shortcutPath);
        Assert.True(Property<bool>(Resolve(shortcutPath), "IsTransientFallback"));

        File.Copy(TestPaths.FromRepository("src/DeskBox/Assets/deskbox.ico"), iconPath);
        object recovered = Resolve(shortcutPath);
        Assert.Equal(iconPath, Property<string>(recovered, "Path"));
        Assert.True(Property<bool>(recovered, "UsesExplicitIconIndex"));
        Assert.False(Property<bool>(recovered, "IsTransientFallback"));
        Assert.Equal(originalLink, File.ReadAllBytes(shortcutPath));
    }

    [Fact]
    public void ExtensionlessInstallerIcon_DecodesTheSameArtworkAsIcoFile()
    {
        string icoPath = TestPaths.FromRepository("src/DeskBox/Assets/deskbox.ico");
        string extensionlessPath = Path.Combine(_directory, "ParadoxIcon");
        File.Copy(icoPath, extensionlessPath);
        byte[] expected = Assert.IsType<byte[]>(IconHelper.TryLoadIconFileBytes(icoPath));
        byte[] actual = Assert.IsType<byte[]>(IconHelper.TryLoadIconFileBytes(extensionlessPath));
        Assert.Equal(expected, actual);
        using var stream = new MemoryStream(actual);
        using var bitmap = new Bitmap(stream);
        Assert.True(bitmap.Width > 0 && bitmap.Height > 0);
    }

    [Fact]
    public void IndexedResourceFailure_DoesNotReturnGenericFileTypeIcon()
    {
        string iconPath = Path.Combine(_directory, "invalid-resource.dll");
        File.WriteAllText(iconPath, "This file has no icon resources.");
        object source = CreateSource(iconPath, 0);
        Assert.Null(typeof(IconHelper).GetMethod("LoadIconBytes", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [source, false]));
    }

    [Fact]
    public void ExtensionlessResourceCacheKey_TracksIndexAndResourceModification()
    {
        string iconPath = Path.Combine(_directory, "ParadoxIcon");
        File.WriteAllBytes(iconPath, [1]);
        string shortcutPath = Path.Combine(_directory, "launcher.lnk");
        File.WriteAllBytes(shortcutPath, CreateLink(iconPath, 0, true));
        string first = CacheKey(shortcutPath, CreateSource(iconPath, 0));
        Assert.NotEqual(first, CacheKey(shortcutPath, CreateSource(iconPath, -5)));
        File.WriteAllBytes(iconPath, [1, 2]);
        Assert.NotEqual(first, CacheKey(shortcutPath, CreateSource(iconPath, 0)));
    }

    [Fact]
    public void TimedOutShortcutResolution_IsTransientInsteadOfAValidCachedPage()
    {
        object fallback = typeof(IconHelper).GetMethod("CreateFallbackIconSource", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [Path.Combine(_directory, "launcher.lnk")])!;
        Assert.True(Property<bool>(fallback, "IsTransientFallback"));
        object ordinaryFile = typeof(IconHelper).GetMethod("CreateFallbackIconSource", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [Path.Combine(_directory, "document.txt")])!;
        Assert.False(Property<bool>(ordinaryFile, "IsTransientFallback"));
    }

    private static object Resolve(string path) => typeof(IconHelper)
        .GetMethod("ResolveIconSource", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [path, true])!;

    private static T Property<T>(object value, string name) => (T)value.GetType().GetProperty(name)!.GetValue(value)!;

    private static object CreateSource(string path, int index) => Activator.CreateInstance(
        typeof(IconHelper).GetNestedType("IconSource", BindingFlags.NonPublic)!, path, index, true, false)!;

    private static string CacheKey(string path, object source) => (string)typeof(IconHelper)
        .GetMethod("BuildCacheKey", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [path, source])!;

    private static byte[] CreateLink(string path, int index, bool unicode, string? environmentPath = null)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        byte[] header = new byte[76];
        BinaryPrimitives.WriteUInt32LittleEndian(header, 76);
        new Guid("00021401-0000-0000-c000-000000000046").TryWriteBytes(header.AsSpan(4, 16));
        // Include the preceding StringData fields so the icon is not assumed
        // to begin immediately after the header.
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20),
            (uint)(0x7c | (unicode ? 0x80 : 0) | (environmentPath is null ? 0 : 0x4000)));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(56), index);
        writer.Write(header);
        foreach (string text in new[] { "description", "target.exe", ".", "--argument", path })
        {
            writer.Write((ushort)text.Length);
            writer.Write((unicode ? Encoding.Unicode : Encoding.ASCII).GetBytes(text));
        }
        if (environmentPath is not null)
        {
            byte[] block = new byte[0x314];
            BinaryPrimitives.WriteUInt32LittleEndian(block, 0x314);
            BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(4), 0xa0000007);
            Encoding.Unicode.GetBytes(environmentPath).CopyTo(block, 268);
            writer.Write(block);
        }
        writer.Write(0u);
        return stream.ToArray();
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
