﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChessTracking.Utils
{
    static class BitmapExtenstions
    {
        public static byte[] ImageToByteArray(this System.Drawing.Image image)
        {
            using (var ms = new MemoryStream())
            {
                image.Save(ms, ImageFormat.Bmp);
                return ms.ToArray();
            }
        }

        public static Bitmap HorizontalFlip(this Bitmap bm)
        {
            bm.RotateFlip(RotateFlipType.RotateNoneFlipX);
            return bm;
        }

        public static double CustomBrightness(this Color c)
        {
            return Math.Sqrt(
                c.R * c.R * .241 +
                c.G * c.G * .691 +
                c.B * c.B * .068) / 255f;
        }
    }
}
