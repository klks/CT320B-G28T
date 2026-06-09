using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Text;

namespace CT320B.LabelDesigner.Controls;

/// <summary>
/// The "About" view — a 90s demoscene <i>cracktro</i> with a Star-Wars-style perspective text crawl: a
/// parallax starfield, sine-wave colour-cycling "copper" bars and a bouncing rainbow title up top, and a
/// yellow paragraph crawl that scrolls up and recedes toward a vanishing point. One timer drives it all,
/// and it only runs while visible (<see cref="Running"/>). Pure GDI+; no external assets.
/// </summary>
public sealed class AboutView : UserControl
{
    private const string Title = "CT320B LABEL DESIGNER";

    private const int CrawlWidth = 720;   // logical width the crawl text wraps to (px)
    private const float CrawlLineSpacing = 1.45f;   // line height as a multiple of the font height
    private const string CrawlText =
        "A long time ago, in a label printer far, far away....\n\n" +
        "It is a period of creative freedom. Rebel designers, striking from a hidden workshop, have built " +
        "the ultimate thermal-label studio.\n\n" +
        "Its arsenal: styled QR codes, variable-data batch printing, sixteen shapes, fifteen barcodes, free " +
        "rotation and six languages.\n\n" +
        "Pursued by bloated proprietary drivers, the heroes wrote everything in 100% pure C#.\n\n" +
        "Greetings to all thermal-sticker enjoyers across the galaxy. May your prints be ever crisp....\n\n" +
        "Crafted with Claude.";

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };   // ~30 fps
    private readonly Random _rng = new(1990);
    // 3-D starfield flying toward the viewer: (x,y) ∈ [-1,1] direction, z = depth (near 0 = at the camera),
    // v = approach speed; (ox,oy) is the previous screen position for the warp streak (NaN = none).
    private const int Stars = 170;
    private readonly float[] _stx = new float[Stars];
    private readonly float[] _sty = new float[Stars];
    private readonly float[] _stz = new float[Stars];
    private readonly float[] _stv = new float[Stars];
    private readonly float[] _stox = new float[Stars];
    private readonly float[] _stoy = new float[Stars];
    private int _frame;

    private readonly Font _titleFont = new("Consolas", 34f, FontStyle.Bold);
    private readonly Font _subFont = new("Consolas", 10f, FontStyle.Bold);
    private readonly Font _crawlFont = new("Arial", 26f, FontStyle.Bold);
    private Bitmap? _crawl;       // pre-rendered crawl paragraph (yellow on transparent)
    private int _crawlH;
    private float _crawlPos;      // current source-row at the bottom (near) edge; grows over time

    public AboutView()
    {
        DoubleBuffered = true;
        BackColor = Color.Black;
        for (int i = 0; i < Stars; i++) { Spawn(i); _stz[i] = 0.05f + (float)_rng.NextDouble() * 0.95f; }
        _timer.Tick += (_, _) => { _frame++; StepStars(); _crawlPos += 1.1f; Invalidate(); };
    }

    // (Re)launches star i at a far depth with a random direction and approach speed.
    private void Spawn(int i)
    {
        _stx[i] = (float)(_rng.NextDouble() * 2 - 1);
        _sty[i] = (float)(_rng.NextDouble() * 2 - 1);
        _stz[i] = 1f;
        _stv[i] = 0.004f + (float)_rng.NextDouble() * 0.014f;
        _stox[i] = float.NaN;
    }

    /// <summary>Starts/stops the animation. Set true only while the About tab is showing <i>and</i> the
    /// window has focus; setting false stops the timer and releases the pre-rendered crawl bitmap (it is
    /// rebuilt lazily on the next paint), so nothing is held while the view is idle.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Running
    {
        set
        {
            if (value)
            {
                _timer.Start();
            }
            else
            {
                _timer.Stop();
                _crawl?.Dispose();
                _crawl = null;   // EnsureCrawl() rebuilds it when shown again
            }
        }
    }

    private void StepStars()
    {
        int w = Math.Max(2, ClientSize.Width), h = Math.Max(2, ClientSize.Height);
        float cx = w / 2f, cy = h / 2f, scale = w * 0.16f;
        for (int i = 0; i < Stars; i++)
        {
            _stz[i] -= _stv[i];
            if (_stz[i] < 0.04f) { Spawn(i); continue; }
            float k = scale / _stz[i];
            float sx = cx + _stx[i] * k, sy = cy + _sty[i] * k;
            if (sx < -20 || sx > w + 20 || sy < -20 || sy > h + 20) Spawn(i);   // flew past the edge
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        int w = ClientSize.Width, h = ClientSize.Height;
        g.Clear(Color.Black);

        DrawCopperBars(g, w, h);
        DrawStars(g, w, h);
        DrawSineTitle(g, w, h);
        DrawSubtitle(g, w, h);
        DrawCrawl(g, w, h);
    }

    // Copper bars kept to the top band so they sit behind the title, not over the crawl below.
    private void DrawCopperBars(Graphics g, int w, int h)
    {
        const int bars = 5, band = 26;
        for (int k = 0; k < bars; k++)
        {
            float cy = h * 0.13f + (float)Math.Sin(_frame * 0.03 + k * 1.05) * h * 0.10f;
            var rect = new RectangleF(0, cy - band / 2f, w, band);
            if (rect.Height <= 0) continue;
            Color c = Hsv(_frame * 1.5 + k * 60, 0.85, 1);
            using var brush = new LinearGradientBrush(
                new RectangleF(0, rect.Y, 1, rect.Height),
                Color.FromArgb(0, c), c, LinearGradientMode.Vertical)
            { WrapMode = WrapMode.TileFlipXY };
            brush.SetBlendTriangularShape(0.5f);   // brightest in the middle of the band
            g.FillRectangle(brush, rect);
        }
    }

    private void DrawStars(Graphics g, int w, int h)
    {
        float cx = w / 2f, cy = h / 2f, scale = w * 0.16f;
        for (int i = 0; i < Stars; i++)
        {
            float k = scale / _stz[i];
            float sx = cx + _stx[i] * k, sy = cy + _sty[i] * k;
            if (sx < 0 || sx > w || sy < 0 || sy > h) { _stox[i] = float.NaN; continue; }

            int br = Math.Min(255, 110 + (int)((1f - _stz[i]) * 160f));   // nearer = brighter
            float sz = Math.Clamp((1f - _stz[i]) * 4f, 1f, 4f);          // nearer = bigger
            // Warp streak from the previous position toward the camera (skipped right after a respawn).
            if (!float.IsNaN(_stox[i]))
            {
                using var pen = new Pen(Color.FromArgb(br / 2, br, br, 255), 1f);
                g.DrawLine(pen, _stox[i], _stoy[i], sx, sy);
            }
            using var b = new SolidBrush(Color.FromArgb(br, br, 255));
            g.FillEllipse(b, sx - sz / 2f, sy - sz / 2f, sz, sz);
            _stox[i] = sx;
            _stoy[i] = sy;
        }
    }

    private void DrawSineTitle(Graphics g, int w, int h)
    {
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        float[] widths = new float[Title.Length];
        float total = 0;
        for (int i = 0; i < Title.Length; i++)
        {
            widths[i] = g.MeasureString(Title[i].ToString(), _titleFont).Width - 4f;
            total += widths[i];
        }
        float x = (w - total) / 2f, baseY = h * 0.12f;
        for (int i = 0; i < Title.Length; i++)
        {
            float y = baseY + (float)Math.Sin(_frame * 0.10 + i * 0.45) * 18f;
            string ch = Title[i].ToString();
            g.DrawString(ch, _titleFont, Brushes.Black, x + 2, y + 2);                 // shadow
            using var b = new SolidBrush(Hsv(_frame * 4 + i * 16, 1, 1));
            g.DrawString(ch, _titleFont, b, x, y);
            x += widths[i];
        }
    }

    private void DrawSubtitle(Graphics g, int w, int h)
    {
        const string sub = "- a thermal-label design studio -";
        SizeF s = g.MeasureString(sub, _subFont);
        using var b = new SolidBrush(Color.FromArgb(180, 200, 220));
        g.DrawString(sub, _subFont, b, (w - s.Width) / 2f, h * 0.12f + 54f);
    }

    // The Star-Wars-style crawl: pre-rendered yellow paragraphs drawn scanline-by-scanline with a
    // perspective mapping — rows near the bottom are full-width (near), rows further up are scaled down
    // and source-compressed (receding to the vanishing point), with a fade into the distance.
    private void DrawCrawl(Graphics g, int w, int h)
    {
        EnsureCrawl();
        if (_crawl is null) return;

        float topY = h * 0.32f, bottomY = h * 0.99f;
        float span = bottomY - topY;
        if (span <= 1) return;
        float focal = span * 0.30f;                 // smaller = steeper perspective (higher angle of attack)
        float screenW = w * 0.84f;                  // crawl content width at the near (bottom) edge
        float cx = w / 2f;

        // Loop once the whole paragraph has receded past the top.
        float maxPos = _crawlH + span + span * span / (2f * focal);
        if (_crawlPos > maxPos) _crawlPos = 0f;

        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        for (float dy = bottomY; dy >= topY; dy -= 2f)
        {
            float d = bottomY - dy;                                  // 0 at bottom (near) → span at top (far)
            float scale = focal / (focal + d);                      // perspective width factor
            float sourceY = _crawlPos - (d + d * d / (2f * focal)); // compressed toward the top
            if (sourceY < 0 || sourceY >= _crawlH) continue;
            float destW = screenW * scale;
            var dest = new RectangleF(cx - destW / 2f, dy, destW, 2f);
            var src = new RectangleF(0, sourceY, CrawlWidth, 2f);
            g.DrawImage(_crawl, dest, src, GraphicsUnit.Pixel);
        }

        // Fade the far text into the distance near the vanishing point.
        using var fade = new LinearGradientBrush(
            new RectangleF(0, topY - 1, 1, span * 0.34f),
            Color.Black, Color.FromArgb(0, 0, 0, 0), LinearGradientMode.Vertical);
        g.FillRectangle(fade, 0, topY - 1, w, span * 0.34f);
    }

    private void EnsureCrawl()
    {
        if (_crawl is not null) return;
        using var measureBmp = new Bitmap(1, 1);
        using var mg = Graphics.FromImage(measureBmp);
        mg.TextRenderingHint = TextRenderingHint.AntiAlias;

        List<string> lines = WrapLines(mg, CrawlText, CrawlWidth - 24);
        float lineH = _crawlFont.GetHeight(mg) * CrawlLineSpacing;
        int hc = Math.Max(1, (int)Math.Ceiling(lines.Count * lineH) + 12);

        var bmp = new Bitmap(CrawlWidth, hc);
        using (var gg = Graphics.FromImage(bmp))
        {
            gg.Clear(Color.Transparent);
            gg.TextRenderingHint = TextRenderingHint.AntiAlias;
            using var yellow = new SolidBrush(Color.FromArgb(255, 232, 60));
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Length == 0) continue;   // blank line = paragraph gap
                float lw = gg.MeasureString(lines[i], _crawlFont).Width;
                gg.DrawString(lines[i], _crawlFont, yellow, (CrawlWidth - lw) / 2f, 6 + i * lineH);
            }
        }
        _crawl = bmp;
        _crawlH = hc;
    }

    // Word-wraps the crawl text to a pixel width, treating "\n" as a hard break (an empty entry = blank line).
    private List<string> WrapLines(Graphics g, string text, float width)
    {
        var lines = new List<string>();
        foreach (string para in text.Split('\n'))
        {
            if (para.Length == 0) { lines.Add(""); continue; }
            var line = new StringBuilder();
            foreach (string word in para.Split(' '))
            {
                string trial = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && g.MeasureString(trial, _crawlFont).Width > width)
                {
                    lines.Add(line.ToString());
                    line.Clear().Append(word);
                }
                else
                {
                    if (line.Length > 0) line.Append(' ');
                    line.Append(word);
                }
            }
            if (line.Length > 0) lines.Add(line.ToString());
        }
        return lines;
    }

    private static Color Hsv(double hue, double sat, double val)
    {
        hue = ((hue % 360) + 360) % 360;
        int hi = (int)(hue / 60) % 6;
        double f = hue / 60 - Math.Floor(hue / 60);
        int v = (int)(val * 255), p = (int)(val * (1 - sat) * 255);
        int q = (int)(val * (1 - f * sat) * 255), t = (int)(val * (1 - (1 - f) * sat) * 255);
        return hi switch
        {
            0 => Color.FromArgb(v, t, p),
            1 => Color.FromArgb(q, v, p),
            2 => Color.FromArgb(p, v, t),
            3 => Color.FromArgb(p, q, v),
            4 => Color.FromArgb(t, p, v),
            _ => Color.FromArgb(v, p, q),
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _titleFont.Dispose();
            _subFont.Dispose();
            _crawlFont.Dispose();
            _crawl?.Dispose();
        }
        base.Dispose(disposing);
    }
}
