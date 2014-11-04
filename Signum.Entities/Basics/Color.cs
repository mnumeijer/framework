﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Linq.Expressions;
using System.Drawing;
using Signum.Utilities;

namespace Signum.Entities.Basics
{
    [Serializable]
    public class ColorDN : EmbeddedEntity
    {
        public ColorDN()
        {
        }

        public static ColorDN FromARGB(byte a, byte r, byte g, byte b)
        {
            return new ColorDN { Argb = a << 0x18 | r << 0x10 | g << 0x8 | b };
        }

        public static ColorDN FromARGB(byte a, int rgb)
        {
            return new ColorDN { Argb = a << 0x18 | rgb };
        }

        public static ColorDN FromARGB(int argb)
        {
            return new ColorDN { Argb = argb };
        }

        public static ColorDN FromRGBHex(string htmlColor)
        {
            return ColorDN.FromARGB(ColorTranslator.FromHtml(htmlColor).ToArgb());
        }

        int argb;
        public int Argb
        {
            get { return argb; }
            set { SetToStr(ref argb, value); }
        }

        [HiddenProperty]
        public byte A
        {
            get { return (byte)((argb >> 0x18) & 0xff); }
        }

        [HiddenProperty]
        public byte R
        {
            get { return (byte)((argb >> 0x10) & 0xff); }
        }

        [HiddenProperty]
        public byte G
        {
            get { return (byte)((argb >> 0x8) & 0xff); }
        }

        [HiddenProperty]
        public byte B
        {
            get { return (byte)(argb & 0xff); }
        }

        public Color ToColor()
        {
            return Color.FromArgb(argb); 
        }

        public string RGBHex()
        {
            return "#" + R.ToString("X2") + G.ToString("X2") + B.ToString("X2");
        }

        public string ARGBHex()
        {
            return "#" + A.ToString("X2") + R.ToString("X2") + G.ToString("X2") + B.ToString("X2");
        }

        public string RGBAExpression()
        {
            return  "rgb({0:X2}, {1:X2}, {2:X2}, {3})".Formato(R, G, B, (A / 255.0));
        }

        public override string ToString()
        {
            return "#" + Argb.ToString("X8");
        }
    }
}
