using System;

// draws the dashboard's pictures in code: rounded panels, gauge segments, bars, dots and the
// steering wheel. no image files needed, and no Unity types in here, HudSkin turns these into Unity textures.
// definitely made it harder to myself here but i wanted a challenge i guess
// every shape is drawn from its "signed distance" (how far a pixel is from the shape's edge,
// negative inside). blending over the last pixel of the edge gives smooth, anti-aliased edges.
public static class HudArt
{
    // an RGBA picture, row 0 at the TOP (HudSkin flips it for Unity, which starts at the bottom).
    public sealed class Img
    {
        public readonly int W, H;
        public readonly float[] r, g, b, a;
        public Img(int w, int h)
        {
            W = w; H = h;
            r = new float[w * h]; g = new float[w * h]; b = new float[w * h]; a = new float[w * h];
        }

        // paint a color over pixel i with this much coverage (0..1), like a layer in an image editor.
        public void Over(int i, float cr, float cg, float cb, float ca, float coverage)
        {
            float sa = ca * Clamp01(coverage);
            if (sa <= 0f) return;
            float da = a[i], oa = sa + da * (1f - sa);
            r[i] = (cr * sa + r[i] * da * (1f - sa)) / oa;
            g[i] = (cg * sa + g[i] * da * (1f - sa)) / oa;
            b[i] = (cb * sa + b[i] * da * (1f - sa)) / oa;
            a[i] = oa;
        }

        // fill a shape given by its distance function (in pixels, from the pixel's center).
        public void Fill(Func<float, float, float> distance, Rgba c)
        {
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    Over(y * W + x, c.r, c.g, c.b, c.a, 0.5f - distance(x + 0.5f, y + 0.5f));
        }

        // a band just inside the shape's edge, `width` pixels thick.
        public void Edge(Func<float, float, float> distance, float width, Rgba c)
        {
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float d = distance(x + 0.5f, y + 0.5f);
                    float inside = Clamp01(0.5f - d), deeper = Clamp01(0.5f - (d + width));
                    Over(y * W + x, c.r, c.g, c.b, c.a, inside - deeper);
                }
        }

        public byte[] ToBytesBottomUp()
        {
            var bytes = new byte[W * H * 4];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int src = y * W + x, dst = ((H - 1 - y) * W + x) * 4;
                    bytes[dst] = ToByte(r[src]); bytes[dst + 1] = ToByte(g[src]);
                    bytes[dst + 2] = ToByte(b[src]); bytes[dst + 3] = ToByte(a[src]);
                }
            return bytes;
        }
    }

    public struct Rgba
    {
        public float r, g, b, a;
        public Rgba(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Rgba Hex(int rgb, float a = 1f) =>
            new Rgba(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, a);
    }

    public static readonly Rgba White = new Rgba(1, 1, 1);

    // rounded box centered at (cx, cy), half size (hx, hy), corner radius rad.
    public static float RoundBox(float px, float py, float cx, float cy, float hx, float hy, float rad)
    {
        float qx = Math.Abs(px - cx) - (hx - rad), qy = Math.Abs(py - cy) - (hy - rad);
        float ox = Math.Max(qx, 0f), oy = Math.Max(qy, 0f);
        return (float)Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0f) - rad;
    }

    public static float Circle(float px, float py, float cx, float cy, float rad) =>
        (float)Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy)) - rad;

    // a line from (ax, ay) to (bx, by) with round ends, rad thick on each side.
    public static float Capsule(float px, float py, float ax, float ay, float bx, float by, float rad)
    {
        float pax = px - ax, pay = py - ay, bax = bx - ax, bay = by - ay;
        float h = Clamp01((pax * bax + pay * bay) / (bax * bax + bay * bay));
        float dx = pax - bax * h, dy = pay - bay * h;
        return (float)Math.Sqrt(dx * dx + dy * dy) - rad;
    }

    // a rounded panel for 9-slicing: corners stay round however big it's stretched.
    public static Img Panel(int size, float radius, Rgba fill, Rgba border, float borderWidth)
    {
        var img = new Img(size, size);
        float h = size / 2f - 0.5f;
        Func<float, float, float> shape = (x, y) => RoundBox(x, y, size / 2f, size / 2f, h, h, radius);
        img.Fill(shape, fill);
        if (borderWidth > 0f) img.Edge(shape, borderWidth, border);
        return img;
    }

    // white capsule standing up, for the throttle and brake bars (tinted when drawn).
    public static Img Pill(int w, int h)
    {
        var img = new Img(w, h);
        float rad = w / 2f - 0.5f;
        img.Fill((x, y) => Capsule(x, y, w / 2f, rad + 0.5f, w / 2f, h - rad - 0.5f, rad), White);
        return img;
    }

    // one segment of the speed arc: a small white rounded bar.
    public static Img Segment(int w, int h, float radius)
    {
        var img = new Img(w, h);
        img.Fill((x, y) => RoundBox(x, y, w / 2f, h / 2f, w / 2f - 0.5f, h / 2f - 0.5f, radius), White);
        return img;
    }

    public static Img Disc(int size)
    {
        var img = new Img(size, size);
        img.Fill((x, y) => Circle(x, y, size / 2f, size / 2f, size / 2f - 1f), White);
        return img;
    }

    // soft round glow, brightest in the middle.
    public static Img Glow(int size)
    {
        var img = new Img(size, size);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = (Circle(x + 0.5f, y + 0.5f, size / 2f, size / 2f, 0f)) / (size / 2f);
                float k = Clamp01(1f - d);
                img.Over(y * size + x, 1, 1, 1, 1, k * k);
            }
        return img;
    }

    // a steering wheel seen from the driver's seat (took inspo fromm googling fsae steering wheel): two grips, a center display,
    // four colored buttons, and a gold mark at 12 o'clock so you can see how far it's turned.
    public static Img SteeringWheel(int size)
    {
        var img = new Img(size, size);
        float s = size / 256f, c = size / 2f;
        // everything below is laid out on a 256 px wheel and scaled.
        // the parts are blended with a smooth union, so there are no notches where they meet.
        Func<float, float, float> body = (x, y) =>
        {
            float u = (x - c) / s, v = (y - c) / s;
            float d = RoundBox(u, v, 0, 4, 74, 48, 22);                               // center plate
            d = SmoothMin(d, Capsule(u, v, -90, -40, -90, 48, 24), 16);             // left grip
            d = SmoothMin(d, Capsule(u, v, 90, -40, 90, 48, 24), 16);               // right grip
            d = SmoothMin(d, RoundBox(u, v, 0, -44, 92, 14, 12), 10);               // top bar
            return d * s;
        };
        Func<float, float, float> grips = (x, y) =>
        {
            float u = (x - c) / s, v = (y - c) / s;
            return Math.Min(Capsule(u, v, -92, -30, -92, 44, 19), Capsule(u, v, 92, -30, 92, 44, 19)) * s;
        };
        Func<float, float, float> screen = (x, y) => RoundBox((x - c) / s, (y - c) / s, 0, 0, 44, 24, 6) * s;
        Func<float, float, float> mark = (x, y) => RoundBox((x - c) / s, (y - c) / s, 0, -52, 9, 8, 3) * s;

        img.Fill(body, Rgba.Hex(0x2A2D34));                 // carbon
        img.Edge(body, 2.2f * s, Rgba.Hex(0x6B717E));        // light rim so it reads on dark backgrounds
        img.Fill(grips, Rgba.Hex(0x141518));                 // rubber grips
        img.Edge(grips, 1.5f * s, Rgba.Hex(0x3A3D45));
        img.Fill(screen, Rgba.Hex(0x0B0D11));                // dash display
        img.Edge(screen, 1.5f * s, Rgba.Hex(0xFDC700, 0.8f));
        img.Fill(mark, Rgba.Hex(0xFDC700));                  // 12 o'clock mark

        // four buttons under the display, the way FS wheels have them
        int[] colors = { 0xE5484D, 0x3E8EF7, 0xFDC700, 0x30C46C };
        for (int i = 0; i < 4; i++)
        {
            float bx = -39 + i * 26;
            img.Fill((x, y) => Circle((x - c) / s, (y - c) / s, bx, 36, 6.5f) * s, Rgba.Hex(colors[i]));
        }
        return img;
    }

    // union of two shapes with a rounded fillet k pixels wide where they meet.
    static float SmoothMin(float a, float b, float k)
    {
        float h = Clamp01(0.5f + 0.5f * (b - a) / k);
        return b + (a - b) * h - k * h * (1f - h);
    }

    static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    static byte ToByte(float v) => (byte)Math.Round(Clamp01(v) * 255f);
}
