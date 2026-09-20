using Avalonia.Controls;

namespace Frpm.Tray;

internal static class TrayIconImage
{
    public static WindowIcon Create()
    {
        const int size = 64;
        const int imageOffset = 22;
        const int bitmapHeaderSize = 40;
        const int xorBitmapSize = size * size * 4;
        const int andBitmapSize = size * size / 8;

        var stream = new MemoryStream(imageOffset + bitmapHeaderSize + xorBitmapSize + andBitmapSize);
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write((byte)size);
        writer.Write((byte)size);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(bitmapHeaderSize + xorBitmapSize + andBitmapSize);
        writer.Write(imageOffset);

        writer.Write(bitmapHeaderSize);
        writer.Write(size);
        writer.Write(size * 2);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0);
        writer.Write(xorBitmapSize);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        for (var y = size - 1; y >= 0; y--)
        {
            for (var x = 0; x < size; x++)
            {
                var (red, green, blue, alpha) = PixelAt(x, y);
                writer.Write(blue);
                writer.Write(green);
                writer.Write(red);
                writer.Write(alpha);
            }
        }

        writer.Write(new byte[andBitmapSize]);
        stream.Position = 0;
        return new WindowIcon(stream);
    }

    private static (byte Red, byte Green, byte Blue, byte Alpha) PixelAt(int x, int y)
    {
        var roundedCorner = (x < 16 && y < 16 && DistanceSquared(x, y, 16, 16) > 256)
            || (x > 47 && y < 16 && DistanceSquared(x, y, 47, 16) > 256)
            || (x < 16 && y > 47 && DistanceSquared(x, y, 16, 47) > 256)
            || (x > 47 && y > 47 && DistanceSquared(x, y, 47, 47) > 256);

        if (roundedCorner)
        {
            return ((byte)0, (byte)0, (byte)0, (byte)0);
        }

        var node = InCircle(x, y, 20, 20, 5) || InCircle(x, y, 44, 20, 5)
            || InCircle(x, y, 20, 44, 5) || InCircle(x, y, 44, 44, 5);
        var center = InCircle(x, y, 32, 32, 6);
        var link = Math.Abs(x - y) <= 2 && x >= 20 && x <= 44
            || Math.Abs((x + y) - 64) <= 2 && x >= 20 && x <= 44;

        if (center || link)
        {
            return ((byte)255, (byte)255, (byte)255, (byte)255);
        }

        return node ? ((byte)191, (byte)199, (byte)255, (byte)255) : ((byte)84, (byte)104, (byte)255, (byte)255);
    }

    private static bool InCircle(int x, int y, int centerX, int centerY, int radius) =>
        DistanceSquared(x, y, centerX, centerY) <= radius * radius;

    private static int DistanceSquared(int x, int y, int centerX, int centerY) =>
        (x - centerX) * (x - centerX) + (y - centerY) * (y - centerY);
}
