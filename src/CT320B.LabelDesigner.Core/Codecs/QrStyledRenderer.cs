using System.Drawing;
using System.Drawing.Drawing2D;
using CT320B.LabelDesigner.Core.Model.Elements;

namespace CT320B.LabelDesigner.Core.Codecs;

/// <summary>
/// Draws a <see cref="QrMatrix"/> as a styled QR <see cref="Bitmap"/> (Phase 15): square / dot /
/// rounded data modules, optionally restyled finder eyes, and an optional centre logo. Stays pure
/// black/white (the CT320B is 1-bpp) — styling is shape only. The plain Square+Square output is a
/// faithful module grid; the default (unstyled, logo-less) path stays on ZXing for byte-parity.
/// </summary>
public static class QrStyledRenderer
{
    /// <summary>How much larger than the logo the white knockout is (per side ≈ half of this minus 1),
    /// so the logo sits in a clean, obviously-carved quiet zone. Used here and by the ECC-budget warning.</summary>
    public const float LogoKnockoutFactor = 1.40f;

    /// <summary>Renders the matrix into a <paramref name="sidePx"/>-square bitmap with the given styling.</summary>
    public static Bitmap Render(
        QrMatrix matrix, int sidePx, int marginModules,
        QrModuleStyle moduleStyle, QrEyeStyle eyeStyle, Image? logo, int logoScalePercent)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        sidePx = Math.Max(1, sidePx);
        int total = matrix.Size + 2 * Math.Max(0, marginModules);
        float step = (float)sidePx / total;

        var bmp = new Bitmap(sidePx, sidePx);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var black = new SolidBrush(Color.Black);

            float Ox(int mx) => (marginModules + mx) * step;

            // Data modules — always skip the finder cells; the eyes are drawn solid below so a dotted /
            // rounded module style never breaks finder-pattern detection (and stays scannable).
            for (int y = 0; y < matrix.Size; y++)
                for (int x = 0; x < matrix.Size; x++)
                {
                    if (!matrix.Modules[x, y] || matrix.IsFinder(x, y)) continue;
                    DrawModule(g, black, moduleStyle, Ox(x), Ox(y), step);
                }

            // Finder eyes (solid square or rounded — never stylised into separate dots).
            foreach ((int fx, int fy) in matrix.FinderOrigins)
            {
                if (eyeStyle == QrEyeStyle.Rounded) DrawRoundedEye(g, black, Ox(fx), Ox(fy), step);
                else DrawSquareEye(g, black, Ox(fx), Ox(fy), step);
            }

            if (logo is not null) DrawLogo(g, logo, sidePx, logoScalePercent);
        }
        return bmp;
    }

    private static void DrawModule(Graphics g, Brush brush, QrModuleStyle style, float x, float y, float step)
    {
        switch (style)
        {
            case QrModuleStyle.Dots:
            {
                float inset = step * 0.08f;
                g.FillEllipse(brush, x + inset, y + inset, step - 2 * inset, step - 2 * inset);
                break;
            }
            case QrModuleStyle.Rounded:
            {
                using GraphicsPath p = RoundedRect(x, y, step, step, step * 0.30f);
                g.FillPath(brush, p);
                break;
            }
            default:
                // Square: overdraw by ~0.6 px so adjacent modules don't leave anti-aliased seams.
                g.FillRectangle(brush, x, y, step + 0.6f, step + 0.6f);
                break;
        }
    }

    // The standard finder pattern drawn solid: 7×7 black, 5×5 white, 3×3 black (overdraw to avoid seams).
    private static void DrawSquareEye(Graphics g, Brush brush, float x, float y, float step)
    {
        g.FillRectangle(brush, x, y, 7 * step + 0.6f, 7 * step + 0.6f);
        g.FillRectangle(Brushes.White, x + step, y + step, 5 * step, 5 * step);
        g.FillRectangle(brush, x + 2 * step, y + 2 * step, 3 * step + 0.6f, 3 * step + 0.6f);
    }

    // A rounded finder eye: a rounded 7×7 outer ring (outer minus 5×5 hole) + a rounded 3×3 pupil.
    private static void DrawRoundedEye(Graphics g, Brush brush, float x, float y, float step)
    {
        float outer = 7 * step, holeInset = step, hole = 5 * step;
        using (var ring = new GraphicsPath { FillMode = FillMode.Alternate })
        {
            ring.AddPath(RoundedRect(x, y, outer, outer, step * 1.6f), false);
            ring.AddPath(RoundedRect(x + holeInset, y + holeInset, hole, hole, step * 1.1f), false);
            g.FillPath(brush, ring);
        }
        float pupilInset = 2 * step, pupil = 3 * step;
        using GraphicsPath p = RoundedRect(x + pupilInset, y + pupilInset, pupil, pupil, step * 0.8f);
        g.FillPath(brush, p);
    }

    // Carves a clean white quiet zone (a rounded knockout sized to the logo + padding, so it reads as
    // deliberate empty space) and draws the logo aspect-preserved inside it.
    private static void DrawLogo(Graphics g, Image logo, int sidePx, int logoScalePercent)
    {
        float frac = Math.Clamp(logoScalePercent / 100f, 0.08f, 0.4f);
        float box = sidePx * frac;
        float scale = Math.Min(box / logo.Width, box / logo.Height);
        float w = logo.Width * scale, h = logo.Height * scale;
        float cx = sidePx / 2f, cy = sidePx / 2f;

        // Knockout follows the logo's aspect (padding = the extra of LogoKnockoutFactor split per side).
        float kw = w * LogoKnockoutFactor, kh = h * LogoKnockoutFactor;
        using (GraphicsPath knock = RoundedRect(cx - kw / 2f, cy - kh / 2f, kw, kh, Math.Min(kw, kh) * 0.20f))
            g.FillPath(Brushes.White, knock);

        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(logo, cx - w / 2f, cy - h / 2f, w, h);
    }

    private static GraphicsPath RoundedRect(float x, float y, float w, float h, float radius)
    {
        float r = Math.Min(radius, Math.Min(w, h) / 2f);
        var path = new GraphicsPath();
        if (r <= 0.01f) { path.AddRectangle(new RectangleF(x, y, w, h)); return path; }
        float d = r * 2;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
