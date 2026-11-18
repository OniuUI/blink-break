using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace BlinkBreak
{
    static class Draw
    {
        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Max(0.1f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Color color, RectangleF r, float radius)
        {
            using (var path = RoundRect(r, radius))
            using (var b = new SolidBrush(color))
                g.FillPath(b, path);
        }
    }

    static class EyeArt
    {
        // open: 1 = fully open, 0 = closed
        public static void Draw(Graphics g, float cx, float cy, float W, float open, bool lashes, Color line)
        {
            float half = W / 2f;
            float d = W * 0.30f;                       // bezier control offset -> eye height ~0.45 W
            float lowerCtl = cy + d;
            float upperCtl = lowerCtl - 2f * d * open;  // closed: upper lid lies on the lower lid

            var L = new PointF(cx - half, cy);
            var R = new PointF(cx + half, cy);
            var u1 = new PointF(cx - half * 0.45f, upperCtl);
            var u2 = new PointF(cx + half * 0.45f, upperCtl);
            var l1 = new PointF(cx + half * 0.45f, lowerCtl);
            var l2 = new PointF(cx - half * 0.45f, lowerCtl);

            float upperWidth = Math.Max(1.2f, W * 0.035f);
            float lowerWidth = Math.Max(1f, W * 0.02f);

            if (open > 0.02f)
            {
                using (var eye = new GraphicsPath())
                {
                    eye.AddBezier(L, u1, u2, R);
                    eye.AddBezier(R, l1, l2, L);
                    eye.CloseFigure();

                    using (var sclera = new SolidBrush(Color.FromArgb(240, 244, 250)))
                        g.FillPath(sclera, eye);

                    GraphicsState state = g.Save();
                    g.SetClip(eye, CombineMode.Intersect);

                    float r = W * 0.17f;
                    var iris = new RectangleF(cx - r, cy - r, 2 * r, 2 * r);
                    using (var gp = new GraphicsPath())
                    {
                        gp.AddEllipse(iris);
                        using (var pb = new PathGradientBrush(gp))
                        {
                            pb.CenterPoint = new PointF(cx, cy);
                            pb.CenterColor = Color.FromArgb(125, 195, 255);
                            pb.SurroundColors = new[] { Color.FromArgb(30, 90, 195) };
                            g.FillEllipse(pb, iris);
                        }
                    }
                    using (var ring = new Pen(Color.FromArgb(20, 55, 130), Math.Max(1f, W * 0.012f)))
                        g.DrawEllipse(ring, iris);

                    float pr = r * 0.42f;
                    using (var b = new SolidBrush(Color.FromArgb(12, 18, 35)))
                        g.FillEllipse(b, cx - pr, cy - pr, 2 * pr, 2 * pr);

                    float hr = r * 0.2f;
                    using (var b = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
                        g.FillEllipse(b, cx - r * 0.38f - hr, cy - r * 0.38f - hr, 2 * hr, 2 * hr);
                    float hr2 = r * 0.09f;
                    using (var b = new SolidBrush(Color.FromArgb(180, 255, 255, 255)))
                        g.FillEllipse(b, cx + r * 0.3f - hr2, cy + r * 0.28f - hr2, 2 * hr2, 2 * hr2);

                    g.Restore(state);
                }

                using (var pen = new Pen(Color.FromArgb(160, line), lowerWidth))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    g.DrawBezier(pen, R, l1, l2, L);
                }
            }

            using (var pen = new Pen(line, upperWidth))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                g.DrawBezier(pen, L, u1, u2, R);
            }

            if (lashes)
            {
                float dir = 2f * open - 1f;             // lashes point up when open, down when closed
                float len = W * 0.07f;
                using (var pen = new Pen(line, upperWidth * 0.8f))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    for (int i = 0; i < 5; i++)
                    {
                        float t = 0.22f + i * 0.14f;
                        PointF p = Bezier(L, u1, u2, R, t);
                        PointF tan = BezierTangent(L, u1, u2, R, t);
                        float tl = (float)Math.Sqrt(tan.X * tan.X + tan.Y * tan.Y);
                        if (tl < 0.0001f) continue;
                        float tx = tan.X / tl, ty = tan.Y / tl;
                        float nx = ty, ny = -tx;          // normal on the "upper" side
                        float splay = (t - 0.5f) * 0.9f;
                        var end = new PointF(p.X + nx * len * dir + tx * len * splay,
                                             p.Y + ny * len * dir + ty * len * splay);
                        if (Math.Abs(dir) > 0.05f) g.DrawLine(pen, p, end);
                    }
                }
            }
        }

        static PointF Bezier(PointF a, PointF b, PointF c, PointF d, float t)
        {
            float mt = 1 - t;
            float x = mt * mt * mt * a.X + 3 * mt * mt * t * b.X + 3 * mt * t * t * c.X + t * t * t * d.X;
            float y = mt * mt * mt * a.Y + 3 * mt * mt * t * b.Y + 3 * mt * t * t * c.Y + t * t * t * d.Y;
            return new PointF(x, y);
        }

        static PointF BezierTangent(PointF a, PointF b, PointF c, PointF d, float t)
        {
            float mt = 1 - t;
            float x = 3 * mt * mt * (b.X - a.X) + 6 * mt * t * (c.X - b.X) + 3 * t * t * (d.X - c.X);
            float y = 3 * mt * mt * (b.Y - a.Y) + 6 * mt * t * (c.Y - b.Y) + 3 * t * t * (d.Y - c.Y);
            return new PointF(x, y);
        }
    }

    static class IconArt
    {
        public static Bitmap Render(int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                var rect = new RectangleF(0, 0, size, size);
                float rad = size * 0.22f;
                using (var path = new GraphicsPath())
                {
                    float d = rad * 2;
                    path.AddArc(rect.X, rect.Y, d, d, 180, 90);
                    path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                    path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                    path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                    path.CloseFigure();
                    using (var b = new LinearGradientBrush(rect, Color.FromArgb(72, 152, 255), Color.FromArgb(28, 70, 180), 90f))
                        g.FillPath(b, path);
                }
                EyeArt.Draw(g, size / 2f, size / 2f + (size >= 48 ? size * 0.04f : 0), size * 0.84f, 1f, size >= 48, Color.FromArgb(14, 30, 80));
            }
            return bmp;
        }

        // Multi-resolution .ico with PNG-compressed entries (supported since Windows Vista).
        public static void ExportIco(string path)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
            var images = new List<byte[]>();
            foreach (int s in sizes)
                using (var bmp = Render(s))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    images.Add(ms.ToArray());
                }

            using (var fs = File.Create(path))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((short)0);
                w.Write((short)1);
                w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                    w.Write(dim);
                    w.Write(dim);
                    w.Write((byte)0);
                    w.Write((byte)0);
                    w.Write((short)1);
                    w.Write((short)32);
                    w.Write(images[i].Length);
                    w.Write(offset);
                    offset += images[i].Length;
                }
                foreach (var img in images) w.Write(img);
            }
        }
    }

    static class DropArt
    {
        public static GraphicsPath Path(float cx, float cy, float h)
        {
            float r = h * 0.34f;
            float top = cy - h / 2f;
            float circleY = cy + h / 2f - r;
            var p = new GraphicsPath();
            p.AddBezier(cx, top, cx + r * 0.25f, top + h * 0.22f, cx + r, circleY - r * 0.75f, cx + r, circleY);
            p.AddArc(cx - r, circleY - r, 2 * r, 2 * r, 0, 180);
            p.AddBezier(cx - r, circleY, cx - r, circleY - r * 0.75f, cx - r * 0.25f, top + h * 0.22f, cx, top);
            p.CloseFigure();
            return p;
        }

        // fill: 0..1 water level inside the drop (1 = full)
        public static void Draw(Graphics g, float cx, float cy, float h, float fill)
        {
            fill = Math.Max(0f, Math.Min(1f, fill));
            using (var path = Path(cx, cy, h))
            {
                using (var b = new SolidBrush(Color.FromArgb(34, 52, 86))) g.FillPath(b, path);

                GraphicsState state = g.Save();
                g.SetClip(path, CombineMode.Intersect);
                float top = cy - h / 2f;
                float level = top + h * (1f - fill);
                var water = new RectangleF(cx - h, level, 2 * h, cy + h / 2f - level + 1);
                if (water.Height > 0.5f)
                    using (var b = new LinearGradientBrush(new RectangleF(cx - h, top, 2 * h, h + 1),
                                                           Color.FromArgb(120, 214, 255), Color.FromArgb(30, 120, 220), 90f))
                        g.FillRectangle(b, water);
                g.Restore(state);

                using (var pen = new Pen(Color.FromArgb(150, 205, 240), Math.Max(1f, h * 0.05f))) g.DrawPath(pen, path);
                float hr = h * 0.09f;
                using (var b = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
                    g.FillEllipse(b, cx - h * 0.17f - hr, cy + h * 0.08f - hr, 2 * hr, 2 * hr * 1.4f);
            }
        }
    }
}
