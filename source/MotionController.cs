using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AnnaDesktopPet
{
    internal sealed class MotionFrame
    {
        public readonly BitmapSource Image;
        public readonly byte[] Pixels;
        public int Width { get { return Image.PixelWidth; } }
        public int Height { get { return Image.PixelHeight; } }
        public MotionFrame(BitmapSource source)
        {
            Image = source.Format==PixelFormats.Bgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0); Image.Freeze();
            Pixels = new byte[Width * Height * 4]; Image.CopyPixels(Pixels, Width * 4, 0);
        }
        public Int32Rect SubjectBounds()
        {
            int left = Width, top = Height, right = -1, bottom = -1;
            for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
                if (Pixels[(y * Width + x) * 4 + 3] >= 32)
                { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
            if (right < left) throw new InvalidDataException("角色姿态没有可见像素。");
            return new Int32Rect(left, top, right - left + 1, bottom - top + 1);
        }
    }

}
