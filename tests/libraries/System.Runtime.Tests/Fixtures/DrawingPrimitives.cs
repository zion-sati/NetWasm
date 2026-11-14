// From the compiler harness's DrawingPrimitivesCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Drawing;
using System.Numerics;
namespace DrawingFixture;
public static class EntryPoint
{
    static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; } catch (T) { return true; }
    }

    public static int Run(int input)
    {
        // Construction, validation and channel access.
        var custom = Color.FromArgb(10, 20, 30);
        if (custom.A != 255 || custom.R != 10 || custom.G != 20 || custom.B != 30 || custom.ToArgb() != unchecked((int)0xFF0A141E)) return 1;
        if (Color.FromArgb(0x7F112233).A != 0x7F || Color.FromArgb(128, custom).ToArgb() != unchecked((int)0x800A141E)) return 2;
        try { Color.FromArgb(256, 0, 0); return 3; }
        catch (ArgumentException e) { if (!e.Message.Contains("'red'") || !e.Message.Contains("256")) return 4; }
        if (!Throws<ArgumentException>(() => Color.FromArgb(-1, custom))) return 5;

        // Names, flags, equality and formatting.
        if (custom.Name != "ff0a141e" || custom.IsNamedColor || custom.IsKnownColor || custom.IsSystemColor || custom.IsEmpty) return 6;
        if (Color.Red.Name != "Red" || !Color.Red.IsKnownColor || !Color.Red.IsNamedColor || Color.Red.IsSystemColor || Color.Red.ToKnownColor() != KnownColor.Red) return 7;
        if (Color.Red == Color.FromArgb(255, 0, 0) || Color.Red.ToArgb() != Color.FromArgb(255, 0, 0).ToArgb()) return 8;
        if (Color.FromKnownColor(KnownColor.Red) != Color.Red || Color.FromKnownColor(KnownColor.Red).GetHashCode() != Color.Red.GetHashCode()) return 9;
        if (!Color.Empty.IsEmpty || Color.Empty.ToArgb() != 0 || Color.Empty.Name != "0" || Color.Empty.ToString() != "Color [Empty]") return 10;
        if (Color.Red.ToString() != "Color [Red]" || custom.ToString() != "Color [A=255, R=10, G=20, B=30]") return 11;
        var named = Color.FromName("aliceblue");
        if (!named.IsKnownColor || named.Name != "AliceBlue" || named.ToArgb() != Color.AliceBlue.ToArgb()) return 12;
        var unknown = Color.FromName("NoSuchColor");
        if (unknown.IsKnownColor || !unknown.IsNamedColor || unknown.Name != "NoSuchColor" || unknown.ToArgb() != 0 || unknown.ToString() != "Color [NoSuchColor]") return 13;
        if (Color.RebeccaPurple.ToArgb() != unchecked((int)0xFF663399) || Color.Transparent.A != 0) return 14;

        // System colors use the fixed table; hue, saturation and brightness follow upstream.
        var face = SystemColors.ButtonFace;
        if (!face.IsSystemColor || !face.IsKnownColor || face.Name != "ButtonFace" || face.ToArgb() != unchecked((int)0xFFF0F0F0)) return 15;
        if (SystemColors.Window.Name != "Window" || SystemColors.Window != Color.FromKnownColor(KnownColor.Window)) return 16;
        if (custom.GetHue() != 210f || custom.GetSaturation() != 0.5f || custom.GetBrightness() != 40 / 510f) return 17;
        if (Color.Red.GetHue() != 0f || Color.Red.GetSaturation() != 1f || Color.Red.GetBrightness() != 0.5f || Color.White.GetSaturation() != 0f) return 18;

        // ColorTranslator: HTML names and hex forms, OLE and Win32 values.
        if (ColorTranslator.FromHtml("#FF0000").ToArgb() != Color.Red.ToArgb() || ColorTranslator.FromHtml("#0f0").G != 255) return 19;
        if (ColorTranslator.FromHtml("Red") != Color.Red || ColorTranslator.FromHtml("LightGrey").ToArgb() != Color.LightGray.ToArgb() || !ColorTranslator.FromHtml("").IsEmpty) return 20;
        // The legacy HTML table maps buttonface to Control; other names use the named-color table.
        if (ColorTranslator.FromHtml("ButtonFace") != SystemColors.Control || ColorTranslator.FromHtml("menubar") != SystemColors.MenuBar || Color.FromName("buttonface") != SystemColors.ButtonFace) return 21;
        if (ColorTranslator.ToHtml(Color.Red) != "Red" || ColorTranslator.ToHtml(custom) != "#0A141E" || ColorTranslator.ToHtml(Color.Empty) != "") return 22;
        if (ColorTranslator.ToHtml(SystemColors.Window) != "window" || ColorTranslator.ToHtml(Color.LightGray) != "LightGrey") return 23;
        if (ColorTranslator.ToOle(custom) != 0x1E140A || ColorTranslator.ToWin32(custom) != 0x1E140A || ColorTranslator.FromWin32(0x1E140A).ToArgb() != custom.ToArgb()) return 24;
        if (ColorTranslator.FromOle(0x0000FF).ToArgb() != Color.Red.ToArgb() || ColorTranslator.FromOle(unchecked((int)0x8000000F)) != SystemColors.Control) return 25;
        if (!Throws<FormatException>(() => ColorTranslator.FromHtml("#GG0000")) || !Throws<ArgumentException>(() => ColorTranslator.FromHtml("NotAColor"))) return 26;

        // Integer geometry.
        var point = new Point(3, 4);
        point.Offset(1, -1);
        if (point != new Point(4, 3) || point + new Size(1, 1) != new Point(5, 4) || Point.Subtract(point, new Size(4, 3)) != Point.Empty) return 27;
        if (new Point(0x00020001).X != 1 || new Point(0x00020001).Y != 2 || point.ToString() != "{X=4,Y=3}" || (Size)point != new Size(4, 3)) return 28;
        if (Point.Ceiling(new PointF(1.2f, -1.2f)) != new Point(2, -1) || Point.Round(new PointF(2.5f, 3.5f)) != new Point(2, 4) || Point.Truncate(new PointF(1.9f, -1.9f)) != new Point(1, -1)) return 29;
        var size = new Size(5, 6);
        if (size * 2 != new Size(10, 12) || size / 2 != new Size(2, 3) || size.ToString() != "{Width=5, Height=6}" || (Point)size != new Point(5, 6)) return 30;
        var rect = new Rectangle(1, 2, 10, 20);
        if (rect.Right != 11 || rect.Bottom != 22 || !rect.Contains(5, 5) || rect.Contains(11, 5) || !rect.Contains(new Rectangle(2, 3, 2, 2))) return 31;
        if (rect.ToString() != "{X=1,Y=2,Width=10,Height=20}" || Rectangle.FromLTRB(1, 2, 11, 22) != rect || Rectangle.Inflate(rect, 1, 2) != new Rectangle(0, 0, 12, 24)) return 32;
        var other = new Rectangle(5, 10, 10, 20);
        if (Rectangle.Intersect(rect, other) != new Rectangle(5, 10, 6, 12) || Rectangle.Union(rect, other) != new Rectangle(1, 2, 14, 28) || !rect.IntersectsWith(other)) return 33;
        if (!Rectangle.Intersect(rect, new Rectangle(100, 100, 1, 1)).IsEmpty || !Rectangle.Empty.IsEmpty || rect.Location != new Point(1, 2) || rect.Size != new Size(10, 20)) return 34;

        // Floating geometry, conversions and vectors.
        var pointF = new PointF(1.5f, 2.5f);
        if (pointF.ToString() != "{X=1.5, Y=2.5}" || (PointF)new Point(1, 2) != new PointF(1, 2) || pointF + new SizeF(1, 1) != new PointF(2.5f, 3.5f)) return 35;
        if (pointF.ToVector2() != new Vector2(1.5f, 2.5f) || (PointF)new Vector2(3, 4) != new PointF(3, 4)) return 36;
        var sizeF = new SizeF(1.5f, 2.5f);
        if (sizeF.ToString() != "{Width=1.5, Height=2.5}" || sizeF * 2 != new SizeF(3, 5) || sizeF.ToSize() != new Size(1, 2) || Size.Ceiling(sizeF) != new Size(2, 3)) return 37;
        var rectF = new RectangleF(1.5f, 2.5f, 3, 4);
        if (rectF.ToString() != "{X=1.5,Y=2.5,Width=3,Height=4}" || rectF.Right != 4.5f || !rectF.Contains(2, 3) || rectF.ToVector4() != new Vector4(1.5f, 2.5f, 3, 4)) return 38;
        if ((RectangleF)rect != new RectangleF(1, 2, 10, 20) || Rectangle.Round(rectF) != new Rectangle(2, 2, 3, 4) || Rectangle.Truncate(rectF) != new Rectangle(1, 2, 3, 4)) return 39;
        if (RectangleF.Union(rectF, new RectangleF(0, 0, 1, 1)) != new RectangleF(0, 0, 4.5f, 6.5f) || !RectangleF.Inflate(rectF, 1, 1).Contains(rectF)) return 40;
        if (rect.GetHashCode() != new Rectangle(1, 2, 10, 20).GetHashCode() || !rectF.Equals(new RectangleF(1.5f, 2.5f, 3, 4)) || rectF.Equals(rect)) return 41;
        if ((int)KnownColor.RebeccaPurple != 175 || (int)KnownColor.MenuHighlight != 174 || (int)KnownColor.YellowGreen != 167) return 43;
        return input + 1;
    }
}
