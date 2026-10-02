using System.Buffers.Binary;
using System.Text;

namespace DeskBox.Helpers;

/// <summary>
/// Reads the icon resource persisted in a .lnk without activating Shell COM.
/// Only the MS-SHLLINK icon fields are interpreted; other shortcut behavior
/// continues to use the existing Shell/native metadata backend.
/// </summary>
internal static class ShortcutIconResourceReader
{
    internal readonly record struct IconResource(string Path, int Index);
    private const int HeaderSize = 0x4c;
    private const int MaximumFileSize = 1024 * 1024;
    private const uint IconEnvironmentSignature = 0xa0000007;
    private static readonly Guid ShellLinkClassId = new("00021401-0000-0000-c000-000000000046");

    internal static IconResource? TryRead(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length < HeaderSize || stream.Length > MaximumFileSize)
            {
                return null;
            }

            byte[] data = new byte[(int)stream.Length];
            stream.ReadExactly(data);
            return TryParse(data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    internal static IconResource? TryParse(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize || data.Length > MaximumFileSize ||
            BinaryPrimitives.ReadUInt32LittleEndian(data) != HeaderSize ||
            new Guid(data.Slice(4, 16)) != ShellLinkClassId)
        {
            return null;
        }

        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(20, 4));
        int index = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(56, 4));
        int offset = HeaderSize;
        if ((flags & 1) != 0)
        {
            if (data.Length - offset < 2) return null;
            int idListSize = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
            offset += 2;
            if (!Skip(data, ref offset, idListSize)) return null;
        }

        if ((flags & 2) != 0)
        {
            if (data.Length - offset < 4) return null;
            uint linkInfoSize = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
            if (linkInfoSize < 0x1c || linkInfoSize > int.MaxValue ||
                !Skip(data, ref offset, (int)linkInfoSize)) return null;
        }

        bool unicode = (flags & 0x80) != 0;
        string? iconLocation = null;
        for (uint stringFlag = 4; stringFlag <= 0x40; stringFlag <<= 1)
        {
            if ((flags & stringFlag) == 0) continue;
            if (data.Length - offset < 2) return null;
            int characters = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
            offset += 2;
            int byteCount = characters * (unicode ? 2 : 1);
            if (byteCount > data.Length - offset) return null;
            if (stringFlag == 0x40)
            {
                iconLocation = Decode(data.Slice(offset, byteCount), unicode);
            }
            offset += byteCount;
        }

        // The environment block retains variables even when StringData holds
        // a previously expanded path, which can belong to another machine.
        bool foundEnvironmentIcon = false;
        while (data.Length - offset >= 4)
        {
            uint blockSize = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
            if (blockSize == 0) break;
            if (blockSize < 8 || blockSize > data.Length - offset) return null;
            uint signature = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 4, 4));
            if ((flags & 0x4000) != 0 && signature == IconEnvironmentSignature)
            {
                if (blockSize != 0x314) return null;
                foundEnvironmentIcon = true;
                string expandedLocation = Encoding.Unicode.GetString(data.Slice(offset + 268, 520))
                    .Split('\0')[0];
                if (string.IsNullOrWhiteSpace(expandedLocation))
                {
                    expandedLocation = Decode(data.Slice(offset + 8, 260), unicode: false)?.Split('\0')[0] ?? "";
                }
                if (!string.IsNullOrWhiteSpace(expandedLocation)) iconLocation = expandedLocation;
            }
            offset += (int)blockSize;
        }

        if ((flags & 0x4000) != 0 && !foundEnvironmentIcon) return null;
        return string.IsNullOrWhiteSpace(iconLocation) ? null : new IconResource(iconLocation, index);
    }

    private static bool Skip(ReadOnlySpan<byte> data, ref int offset, int count)
    {
        if (count > data.Length - offset) return false;
        offset += count;
        return true;
    }

    private static string? Decode(ReadOnlySpan<byte> data, bool unicode)
    {
        if (unicode) return Encoding.Unicode.GetString(data);
        // Non-ASCII ANSI paths require the system code page. Leave those to
        // the existing Shell reader rather than guessing their encoding.
        foreach (byte value in data)
        {
            if (value > 0x7f) return null;
        }
        return Encoding.ASCII.GetString(data);
    }
}
