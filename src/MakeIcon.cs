using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

class MakeIcon
{
    static void Main()
    {
        string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string png = Path.Combine(user, @"Downloads\1_nobg.png");
        if (!File.Exists(png))
            png = Path.Combine(user, @"Downloads\1.jpg");

        string ico = Path.Combine(user, @".gemini\antigravity\scratch\aura.ico");

        if (!File.Exists(png)) return;

        using (Image src = Image.FromFile(png))
        using (Bitmap bmp = new Bitmap(128, 128, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                float scale = Math.Min(128f / src.Width, 128f / src.Height);
                int nw = (int)(src.Width * scale);
                int nh = (int)(src.Height * scale);
                g.DrawImage(src, (128 - nw) / 2, (128 - nh) / 2, nw, nh);
            }

            using (MemoryStream ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                byte[] raw = ms.ToArray();

                using (FileStream fs = new FileStream(ico, FileMode.Create))
                using (BinaryWriter bw = new BinaryWriter(fs))
                {
                    bw.Write((short)0); // Reserved
                    bw.Write((short)1); // Type 1 (ICO)
                    bw.Write((short)1); // Count 1
                    bw.Write((byte)128); // Width
                    bw.Write((byte)128); // Height
                    bw.Write((byte)0); // Colors
                    bw.Write((byte)0); // Reserved
                    bw.Write((short)1); // Planes
                    bw.Write((short)32); // BitCount
                    bw.Write((int)raw.Length); // Size
                    bw.Write((int)22); // Offset
                    bw.Write(raw);
                }
            }
        }
    }
}
