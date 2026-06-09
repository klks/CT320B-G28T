using System.Drawing;
using System.Drawing.Drawing2D;
using CT320B.LabelDesigner.Core.Rendering;

namespace CT320B.LabelDesigner.Core.Model.Elements;

/// <summary>The kind of primitive a <see cref="ShapeElement"/> draws.</summary>
public enum ShapeKind
{
    /// <summary>A straight line from the bounds' top-left to its bottom-right corner.</summary>
    Line,
    /// <summary>A rectangle filling the bounds.</summary>
    Box,
    /// <summary>An ellipse inscribed in the bounds.</summary>
    Ellipse,
    /// <summary>A rounded rectangle (see <see cref="ShapeElement.CornerRadiusMm"/>).</summary>
    RoundRect,
    /// <summary>An upward-pointing triangle inscribed in the bounds.</summary>
    Triangle,
    /// <summary>A right triangle with the right angle at the bottom-left.</summary>
    RightTriangle,
    /// <summary>A diamond / rhombus through the bounds' edge midpoints.</summary>
    Diamond,
    /// <summary>A parallelogram (top edge shifted right).</summary>
    Parallelogram,
    /// <summary>A trapezoid (narrower top edge).</summary>
    Trapezoid,
    /// <summary>A plus / cross.</summary>
    Cross,
    /// <summary>A right-pointing block arrow.</summary>
    Arrow,
    /// <summary>A right-pointing chevron.</summary>
    Chevron,
    /// <summary>A regular polygon with <see cref="ShapeElement.Sides"/> sides, inscribed in the bounds.</summary>
    Polygon,
    /// <summary>A star with <see cref="ShapeElement.StarPoints"/> points and
    /// <see cref="ShapeElement.InnerRatio"/> inner radius.</summary>
    Star,
    /// <summary>A filled pie wedge (<see cref="ShapeElement.StartAngleDeg"/>/<see cref="ShapeElement.SweepAngleDeg"/>).</summary>
    Pie,
    /// <summary>An open arc stroke (<see cref="ShapeElement.StartAngleDeg"/>/<see cref="ShapeElement.SweepAngleDeg"/>).</summary>
    Arc,
    /// <summary>A ring / donut (ellipse with an <see cref="ShapeElement.InnerRatio"/> hole).</summary>
    Ring,
}

/// <summary>The outline dash pattern for a shape's stroke.</summary>
public enum StrokeStyle
{
    Solid,
    Dash,
    Dot,
    DashDot,
}

/// <summary>
/// A vector primitive rendered with GDI+ so the on-screen preview and the printed raster come from
/// the same path. Beyond line/box/ellipse/rounded-rect it draws polygonal shapes (triangle, diamond,
/// arrow, …) and parametric ones (regular polygon, star, pie/arc, ring); the parametric kinds read
/// the <see cref="Sides"/>/<see cref="StarPoints"/>/<see cref="InnerRatio"/>/<see cref="StartAngleDeg"/>/
/// <see cref="SweepAngleDeg"/> properties, which are ignored by the kinds that don't use them.
/// </summary>
public sealed class ShapeElement : LabelElement
{
    /// <summary>Which primitive to draw.</summary>
    public ShapeKind Kind { get; set; } = ShapeKind.Box;

    /// <summary>Outline thickness in millimetres (rendered at least 1 px).</summary>
    public float StrokeWidthMm { get; set; } = 0.3f;

    /// <summary>When true the shape is filled with <see cref="FillColor"/> (ignored for
    /// <see cref="ShapeKind.Line"/> and <see cref="ShapeKind.Arc"/>).</summary>
    public bool Filled { get; set; }

    /// <summary>Outline colour.</summary>
    public Color StrokeColor { get; set; } = Color.Black;

    /// <summary>Fill colour (when <see cref="Filled"/>).</summary>
    public Color FillColor { get; set; } = Color.Black;

    /// <summary>Corner radius in millimetres for <see cref="ShapeKind.RoundRect"/>.</summary>
    public float CornerRadiusMm { get; set; } = 2f;

    /// <summary>Number of sides for <see cref="ShapeKind.Polygon"/> (clamped ≥ 3).</summary>
    public int Sides { get; set; } = 6;

    /// <summary>Number of points for <see cref="ShapeKind.Star"/> (clamped ≥ 3).</summary>
    public int StarPoints { get; set; } = 5;

    /// <summary>Inner-radius fraction (0–1) for <see cref="ShapeKind.Star"/> and the hole of
    /// <see cref="ShapeKind.Ring"/>.</summary>
    public float InnerRatio { get; set; } = 0.5f;

    /// <summary>Start angle in degrees (clockwise from 3 o'clock) for <see cref="ShapeKind.Pie"/> /
    /// <see cref="ShapeKind.Arc"/>.</summary>
    public float StartAngleDeg { get; set; } = 0f;

    /// <summary>Swept angle in degrees for <see cref="ShapeKind.Pie"/> / <see cref="ShapeKind.Arc"/>.</summary>
    public float SweepAngleDeg { get; set; } = 270f;

    /// <summary>Dash pattern of the outline stroke.</summary>
    public StrokeStyle StrokeStyle { get; set; } = StrokeStyle.Solid;

    public override void Render(Graphics g, RenderContext ctx)
    {
        float x = ctx.MmToPx(XMm), y = ctx.MmToPx(YMm);
        float w = ctx.MmToPx(WidthMm), h = ctx.MmToPx(HeightMm);
        float strokePx = Math.Max(1f, ctx.MmToPx(StrokeWidthMm));

        using var pen = new Pen(StrokeColor, strokePx) { DashStyle = MapDash(StrokeStyle) };
        using var brush = new SolidBrush(FillColor);

        switch (Kind)
        {
            case ShapeKind.Line:
                g.DrawLine(pen, x, y, x + w, y + h);
                break;

            case ShapeKind.Box:
                if (Filled) g.FillRectangle(brush, x, y, w, h);
                g.DrawRectangle(pen, x, y, w, h);
                break;

            case ShapeKind.Ellipse:
                if (Filled) g.FillEllipse(brush, x, y, w, h);
                g.DrawEllipse(pen, x, y, w, h);
                break;

            case ShapeKind.RoundRect:
                using (GraphicsPath path = RoundedRect(x, y, w, h, ctx.MmToPx(CornerRadiusMm)))
                    FillAndStroke(g, brush, pen, path);
                break;

            case ShapeKind.Pie:
                using (var path = new GraphicsPath())
                {
                    path.AddPie(x, y, w, h, StartAngleDeg, SweepAngleDeg);
                    FillAndStroke(g, brush, pen, path);
                }
                break;

            case ShapeKind.Arc:
                // Open stroke only — never filled.
                if (w > 0 && h > 0) g.DrawArc(pen, x, y, w, h, StartAngleDeg, SweepAngleDeg);
                break;

            case ShapeKind.Ring:
                using (GraphicsPath path = RingPath(x, y, w, h, InnerRatio))
                    FillAndStroke(g, brush, pen, path);
                break;

            default:   // every polygonal kind (Triangle … Star)
                using (GraphicsPath path = PolygonalPath(Kind, x, y, w, h))
                    FillAndStroke(g, brush, pen, path);
                break;
        }
    }

    private void FillAndStroke(Graphics g, Brush brush, Pen pen, GraphicsPath path)
    {
        if (Filled) g.FillPath(brush, path);
        g.DrawPath(pen, path);
    }

    private static DashStyle MapDash(StrokeStyle style) => style switch
    {
        StrokeStyle.Dash => DashStyle.Dash,
        StrokeStyle.Dot => DashStyle.Dot,
        StrokeStyle.DashDot => DashStyle.DashDot,
        _ => DashStyle.Solid,
    };

    // Builds the closed outline for the polygonal kinds within the (x,y,w,h) box.
    private GraphicsPath PolygonalPath(ShapeKind kind, float x, float y, float w, float h)
    {
        var path = new GraphicsPath();
        path.AddPolygon(kind switch
        {
            ShapeKind.Triangle => [P(x + w / 2, y), P(x + w, y + h), P(x, y + h)],
            ShapeKind.RightTriangle => [P(x, y), P(x, y + h), P(x + w, y + h)],
            ShapeKind.Diamond => [P(x + w / 2, y), P(x + w, y + h / 2), P(x + w / 2, y + h), P(x, y + h / 2)],
            ShapeKind.Parallelogram => Parallelogram(x, y, w, h),
            ShapeKind.Trapezoid => Trapezoid(x, y, w, h),
            ShapeKind.Cross => Cross(x, y, w, h),
            ShapeKind.Arrow => Arrow(x, y, w, h),
            ShapeKind.Chevron => Chevron(x, y, w, h),
            ShapeKind.Star => Star(x, y, w, h, Math.Max(3, StarPoints), Math.Clamp(InnerRatio, 0.05f, 0.95f)),
            _ => Regular(x, y, w, h, Math.Max(3, Sides)),   // Polygon
        });
        return path;
    }

    private static PointF P(float x, float y) => new(x, y);

    private static PointF[] Parallelogram(float x, float y, float w, float h)
    {
        float s = w * 0.25f;
        return [P(x + s, y), P(x + w, y), P(x + w - s, y + h), P(x, y + h)];
    }

    private static PointF[] Trapezoid(float x, float y, float w, float h)
    {
        float s = w * 0.2f;
        return [P(x + s, y), P(x + w - s, y), P(x + w, y + h), P(x, y + h)];
    }

    private static PointF[] Cross(float x, float y, float w, float h)
    {
        float t = Math.Min(w, h) * 0.34f;          // arm thickness
        float lx = x + (w - t) / 2f, rx = x + (w + t) / 2f;
        float ty = y + (h - t) / 2f, by = y + (h + t) / 2f;
        return
        [
            P(lx, y), P(rx, y), P(rx, ty), P(x + w, ty), P(x + w, by), P(rx, by),
            P(rx, y + h), P(lx, y + h), P(lx, by), P(x, by), P(x, ty), P(lx, ty),
        ];
    }

    private static PointF[] Arrow(float x, float y, float w, float h)
    {
        float shaftTop = y + h * 0.25f, shaftBot = y + h * 0.75f, neck = x + w * 0.6f;
        return
        [
            P(x, shaftTop), P(neck, shaftTop), P(neck, y),
            P(x + w, y + h / 2f), P(neck, y + h), P(neck, shaftBot), P(x, shaftBot),
        ];
    }

    private static PointF[] Chevron(float x, float y, float w, float h)
    {
        float tip = x + w, neck = x + w * 0.6f, notch = x + w * 0.4f;
        return [P(x, y), P(neck, y), P(tip, y + h / 2f), P(neck, y + h), P(x, y + h), P(notch, y + h / 2f)];
    }

    private static PointF[] Regular(float x, float y, float w, float h, int sides)
    {
        float cx = x + w / 2f, cy = y + h / 2f, rx = w / 2f, ry = h / 2f;
        var pts = new PointF[sides];
        for (int i = 0; i < sides; i++)
        {
            double a = -Math.PI / 2 + i * 2 * Math.PI / sides;   // start at the top
            pts[i] = new PointF(cx + rx * (float)Math.Cos(a), cy + ry * (float)Math.Sin(a));
        }
        return pts;
    }

    private static PointF[] Star(float x, float y, float w, float h, int points, float innerRatio)
    {
        float cx = x + w / 2f, cy = y + h / 2f, rx = w / 2f, ry = h / 2f;
        var pts = new PointF[points * 2];
        for (int i = 0; i < points * 2; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI / points;      // alternate outer/inner, start at top
            float fr = (i % 2 == 0) ? 1f : innerRatio;
            pts[i] = new PointF(cx + rx * fr * (float)Math.Cos(a), cy + ry * fr * (float)Math.Sin(a));
        }
        return pts;
    }

    private static GraphicsPath RingPath(float x, float y, float w, float h, float innerRatio)
    {
        float r = Math.Clamp(innerRatio, 0.05f, 0.95f);
        float iw = w * r, ih = h * r;
        var path = new GraphicsPath(FillMode.Alternate);
        path.AddEllipse(x, y, w, h);
        path.AddEllipse(x + (w - iw) / 2f, y + (h - ih) / 2f, iw, ih);   // hole (even-odd)
        return path;
    }

    private static GraphicsPath RoundedRect(float x, float y, float w, float h, float radius)
    {
        float r = Math.Max(0f, Math.Min(radius, Math.Min(w, h) / 2f));
        var path = new GraphicsPath();
        if (r <= 0f)
        {
            path.AddRectangle(new RectangleF(x, y, w, h));
            return path;
        }
        float d = r * 2f;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
