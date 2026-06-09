using System.Drawing;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using CT320B.LabelDesigner.Core.Model;
using CT320B.LabelDesigner.Core.Model.Elements;

namespace CT320B.LabelDesigner.Core.Serialization;

/// <summary>The outcome of importing a <c>.ddl</c>: the converted document plus any non-fatal
/// warnings (unmapped element types, barcode fallbacks, missing image sources).</summary>
public sealed record DdlImportResult(LabelDocument Document, IReadOnlyList<string> Warnings);

/// <summary>
/// Imports the original Clabel app's <c>.ddl</c> templates (read-only) into our
/// <see cref="LabelDocument"/> model. A <c>.ddl</c> is XML: a <c>&lt;DLabel&gt;</c> root with a
/// base64 <c>previewimage</c>, a <c>&lt;paper&gt;</c> (label size in mm at 203 dpi), and
/// <c>&lt;drawobj&gt;</c> children discriminated by <c>itemtype</c>:
/// <list type="bullet">
/// <item>5 → text, 1 → line, 2 → rectangle (rounded if radius&gt;0), 3 → ellipse,</item>
/// <item>6 → image, 7 → 1-D barcode, 8 → QR code, 10 → font-icon clip-art (a base64 PNG in
/// <c>fonticon</c>, decoded to an embedded image);</item>
/// <item>4 (a minor shape variant) and 12 (table) are not mapped — they become a placeholder box
/// and add a warning.</item>
/// </list>
/// Clabel positions <c>(l,t,w,h)</c> are the <i>unrotated</i> rectangle and <c>rotate</c> spins it
/// about its <b>top-left corner</b>; our renderer rotates about the <b>centre</b>, so the origin is
/// converted accordingly (a no-op when <c>rotate==0</c>).
/// </summary>
public static class DdlImporter
{
    /// <summary>Folder searched (by file name) when a paper's <c>background</c> path doesn't resolve at its
    /// original location — the bundled <c>Templates\Backgrounds</c> beside the app. Settable for tests.</summary>
    public static string BackgroundsDir { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "Templates", "Backgrounds");

    /// <summary>Imports a <c>.ddl</c> from its XML text.</summary>
    public static DdlImportResult Import(string xml)
    {
        ArgumentException.ThrowIfNullOrEmpty(xml);
        XDocument xdoc = XDocument.Parse(xml);
        return Import(xdoc, name: "");
    }

    /// <summary>Reads and imports a <c>.ddl</c> file; the document name defaults to the file name.</summary>
    public static DdlImportResult ImportFile(string path)
    {
        XDocument xdoc = XDocument.Load(path);
        return Import(xdoc, name: Path.GetFileNameWithoutExtension(path));
    }

    /// <summary>
    /// Decodes the <c>&lt;DLabel previewimage="…"&gt;</c> base64 PNG that Clabel embeds, returning a
    /// detached <see cref="Bitmap"/> — a cheap gallery thumbnail that avoids re-rendering the whole
    /// label. Returns null when the file has no preview or it can't be decoded.
    /// </summary>
    public static Bitmap? TryReadPreviewImage(string path)
    {
        try
        {
            string? b64 = (string?)XDocument.Load(path).Root?.Attribute("previewimage");
            if (string.IsNullOrEmpty(b64)) return null;
            using var ms = new MemoryStream(System.Convert.FromBase64String(b64));
            using var decoded = new Bitmap(ms);
            return new Bitmap(decoded);   // copy so it survives the stream being disposed
        }
        catch (Exception ex) when (ex is IOException or FormatException or ArgumentException or System.Xml.XmlException)
        {
            return null;
        }
    }

    private static DdlImportResult Import(XDocument xdoc, string name)
    {
        var warnings = new List<string>();
        XElement paper = xdoc.Root?.Element("paper")
            ?? throw new FormatException("Not a Clabel .ddl: missing <DLabel>/<paper>.");

        var doc = new LabelDocument
        {
            Name = name,
            WidthMm = F(paper, "w", 30f),
            HeightMm = F(paper, "h", 40f),
            // Match this unit's calibration like the other built-in documents (see SampleDocuments).
            PrintOffsetXMm = -1f,
            PrintOffsetYMm = -1f,
        };

        XElement? objects = paper.Element("labelobjects");
        if (objects is not null)
        {
            foreach (XElement obj in objects.Elements("drawobj"))
            {
                LabelElement? element = Convert(obj, warnings);
                if (element is not null) doc.Elements.Add(element);
            }
        }

        AddPaperBackground(paper, xdoc.Root, doc, warnings);
        return new DdlImportResult(doc, warnings);
    }

    /// <summary>
    /// Imports the paper's pre-printed background (the label-stock artwork) as a non-printing image behind
    /// all content, so it shows in the editor as a guide (it isn't sent to the printer). Source priority:
    /// a local <c>background</c> file (bytes embedded so the document stays portable) → an http <c>bgurl</c>
    /// (the app downloads + embeds it on open) → the embedded <c>previewimage</c>. The preview is a render of
    /// the whole label, so it's used only when there are no drawobjs — otherwise it would double the content.
    /// </summary>
    private static void AddPaperBackground(XElement paper, XElement? root, LabelDocument doc, List<string> warnings)
    {
        string local = S(paper, "background", "");
        string url = S(paper, "bgurl", "");

        // Embed bytes from the original local file, else from the bundled Backgrounds folder (matched by
        // file name) when the original path doesn't resolve on this machine.
        byte[]? bytes = local.Length > 0 && File.Exists(local) ? TryReadFile(local) : null;
        bytes ??= ResolveFromBackgroundsFolder(local, url);

        ImageElement? bg = null;
        if (bytes is { Length: > 0 })
            bg = new ImageElement { Name = "Background", ImageData = bytes, Fit = ImageFit.Stretch };
        else if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            bg = new ImageElement { Name = "Background", SourceUrl = url, Fit = ImageFit.Stretch };
        else if (doc.Elements.Count == 0
                 && (string?)root?.Attribute("previewimage") is { Length: > 0 } preview
                 && TryFromBase64(preview) is { Length: > 0 } previewBytes)
            bg = new ImageElement { Name = "Background", ImageData = previewBytes, Fit = ImageFit.Stretch };

        if (bg is null)
        {
            if (local.Length > 0) warnings.Add($"Paper background '{local}' not found — skipped.");
            return;
        }

        bg.BoundsMm = new RectangleF(0, 0, doc.WidthMm, doc.HeightMm);
        bg.ZOrder = doc.Elements.Count == 0 ? 0 : doc.Elements.Min(e => e.ZOrder) - 1;   // behind all content
        bg.Printable = false;   // pre-printed on the stock — an editor guide, not sent to the printer
        doc.Elements.Add(bg);
    }

    private static byte[]? TryReadFile(string path)
    {
        try { return File.ReadAllBytes(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    // Looks for a background by file name in the bundled Backgrounds folder: an exact name match wins,
    // otherwise a same-stem match (any extension). The candidate name comes from the `background` path,
    // or the `bgurl`'s last segment when there's no local path.
    private static byte[]? ResolveFromBackgroundsFolder(string local, string url)
    {
        string name = local.Length > 0 ? Path.GetFileName(local) : UrlFileName(url);
        if (name.Length == 0 || !Directory.Exists(BackgroundsDir)) return null;

        string stem = Path.GetFileNameWithoutExtension(name);
        string? hit = null;
        foreach (string file in Directory.EnumerateFiles(BackgroundsDir))
        {
            string fileName = Path.GetFileName(file);
            if (string.Equals(fileName, name, StringComparison.OrdinalIgnoreCase)) { hit = file; break; }
            if (hit is null && string.Equals(Path.GetFileNameWithoutExtension(fileName), stem, StringComparison.OrdinalIgnoreCase))
                hit = file;
        }
        return hit is null ? null : TryReadFile(hit);
    }

    private static string UrlFileName(string url)
    {
        if (url.Length == 0) return "";
        int slash = url.LastIndexOfAny(['/', '\\']);
        string name = slash >= 0 ? url[(slash + 1)..] : url;
        int query = name.IndexOf('?');
        return query >= 0 ? name[..query] : name;
    }

    private static LabelElement? Convert(XElement obj, List<string> warnings)
    {
        int itemType = I(obj, "itemtype", -1);
        LabelElement element = itemType switch
        {
            5 => Text(obj),
            1 => Shape(obj, ShapeKind.Line),
            2 => Rectangle(obj),
            3 => Shape(obj, ShapeKind.Ellipse),
            6 => Image(obj, warnings),
            7 => Barcode(obj, warnings),
            8 => Qr(obj),
            10 => FontIcon(obj, warnings),
            12 => PlaceholderBox(obj, "table", warnings),
            _ => PlaceholderBox(obj, $"itemtype {itemType}", warnings),
        };
        ApplyCommon(obj, element);
        // Clabel stores a line by its start point + length + angle (linestartx/linestarty/linelength/
        // linedegree), and its (l,t,w,h) box anchor is unreliable (it can sit at the line's right end,
        // pushing our box-diagonal draw off the page). Recompute the geometry from the endpoints.
        if (itemType == 1 && obj.Attribute("linelength") is not null)
            FixLineGeometry(obj, element);
        return element;
    }

    // Rebuilds a line's bounds from its endpoints. Our ShapeElement(Line) draws the box's top-left →
    // bottom-right diagonal; a line whose actual diagonal is top-right → bottom-left is flagged FlipH so
    // it mirrors to match. Axis-aligned (horizontal/vertical) lines are unaffected by the flip.
    private static void FixLineGeometry(XElement obj, LabelElement el)
    {
        float sx = F(obj, "linestartx", el.XMm), sy = F(obj, "linestarty", el.YMm);
        float len = F(obj, "linelength", el.WidthMm), deg = F(obj, "linedegree", 0f);
        double rad = deg * Math.PI / 180.0;
        float ex = sx + len * (float)Math.Cos(rad), ey = sy + len * (float)Math.Sin(rad);

        el.BoundsMm = RectangleF.FromLTRB(Math.Min(sx, ex), Math.Min(sy, ey), Math.Max(sx, ex), Math.Max(sy, ey));
        el.Rotation = 0f;
        el.FlipH = (ex < sx) ^ (ey < sy);   // opposite-sign deltas ⇒ the other diagonal
    }

    // --- element builders ---

    private static TextElement Text(XElement obj) => new()
    {
        Name = "Text",
        Text = TextValue(obj),
        FontFamily = S(obj, "fontfamily", "Segoe UI"),
        FontSizePt = F(obj, "fontsize", 10f),
        Bold = B(obj, "fontbold"),
        Italic = B(obj, "fontitalic"),
        Alignment = Align(I(obj, "alignment", 0)),
        Wrap = true,
    };

    private static BarcodeElement Barcode(XElement obj, List<string> warnings) => new()
    {
        Name = "Barcode",
        Data = TextValue(obj),
        Symbology = Symbology(S(obj, "barcodetype", "CODE_128"), warnings),
        // textposition: 0 = none, 1 = above, 2 = below.
        ShowText = I(obj, "textposition", 0) != 0,
    };

    private static QrElement Qr(XElement obj) => new()
    {
        Name = "QR",
        Data = TextValue(obj),
        ErrorCorrection = Ecc(S(obj, "level", "M")),
    };

    private static ShapeElement Rectangle(XElement obj)
    {
        float radius = F(obj, "radius", 0f);
        ShapeElement s = Shape(obj, radius > 0f ? ShapeKind.RoundRect : ShapeKind.Box);
        s.CornerRadiusMm = radius;
        s.Filled = I(obj, "pattern", 0) != 0;
        return s;
    }

    private static ShapeElement Shape(XElement obj, ShapeKind kind) => new()
    {
        Name = kind.ToString(),
        Kind = kind,
        StrokeWidthMm = F(obj, "linewidth", 0.2f),
        StrokeColor = System.Drawing.Color.Black,
        Filled = kind != ShapeKind.Line && I(obj, "pattern", 0) != 0,
        FillColor = System.Drawing.Color.Black,
    };

    // itemtype 10 = "font-icon": clip-art stored as a base64 PNG in the 'fonticon' attribute. Decode it
    // into an embedded image (printed in black like the rest of the content).
    private static LabelElement FontIcon(XElement obj, List<string> warnings)
    {
        string b64 = S(obj, "fonticon", "");
        if (b64.Length > 0 && TryFromBase64(b64) is { Length: > 0 } bytes)
            return new ImageElement { Name = "Icon", ImageData = bytes, Fit = ImageFit.Stretch };
        return PlaceholderBox(obj, "clip-art (font-icon)", warnings);
    }

    private static byte[]? TryFromBase64(string b64)
    {
        try { return System.Convert.FromBase64String(b64); }
        catch (FormatException) { return null; }
    }


    private static LabelElement Image(XElement obj, List<string> warnings)
    {
        ImageFit fit = B(obj, "fill") ? ImageFit.Stretch : ImageFit.Contain;

        // Embedded pixels (base64 PNG) take priority — the common case for these templates.
        string b64 = S(obj, "base64", "");
        if (b64.Length > 0 && TryFromBase64(b64) is { Length: > 0 } bytes)
            return new ImageElement { Name = "Image", ImageData = bytes, Fit = fit };

        string local = S(obj, "imgpath", "");
        if (local.Length == 0) local = S(obj, "image", "");   // some files store the local path in 'image'
        string url = S(obj, "imgurl", "");
        if (local.Length > 0 && File.Exists(local)) return new ImageElement { Name = "Image", FilePath = local, Fit = fit };
        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return new ImageElement { Name = "Image", SourceUrl = url, Fit = fit };   // app downloads + embeds on open
        if (local.Length > 0) return new ImageElement { Name = "Image", FilePath = local, Fit = fit };
        return PlaceholderBox(obj, "embedded image (no resolvable source)", warnings);
    }

    private static ShapeElement PlaceholderBox(XElement obj, string what, List<string> warnings)
    {
        warnings.Add($"Unsupported {what} at ({F(obj, "l", 0f):0.#},{F(obj, "t", 0f):0.#}) mm — imported as an empty box.");
        return new ShapeElement { Name = "Unsupported", Kind = ShapeKind.Box, StrokeWidthMm = 0.2f, StrokeColor = System.Drawing.Color.Gray };
    }

    /// <summary>Applies the geometry/rotation/z-order/flags shared by every <c>drawobj</c>.</summary>
    private static void ApplyCommon(XElement obj, LabelElement element)
    {
        float l = F(obj, "l", 0f), t = F(obj, "t", 0f);
        float w = F(obj, "w", 1f), h = F(obj, "h", 1f);
        float rotate = F(obj, "rotate", 0f);

        (float x, float y) = TopLeftRotationToCentre(l, t, w, h, rotate);
        element.BoundsMm = new System.Drawing.RectangleF(x, y, w, h);
        element.Rotation = rotate;
        element.ZOrder = I(obj, "zvalue", 0);
        element.Locked = B(obj, "lock");
        element.FlipH = B(obj, "hormirror");
    }

    /// <summary>
    /// Clabel rotates the unrotated rectangle at <c>(l,t)</c> about its top-left corner; we rotate
    /// about the centre. Returns the top-left our model must store so the centred rotation lands the
    /// box in the same place (identity when <paramref name="deg"/> is 0).
    /// </summary>
    internal static (float X, float Y) TopLeftRotationToCentre(float l, float t, float w, float h, float deg)
    {
        double rad = deg * Math.PI / 180.0;
        double cos = Math.Cos(rad), sin = Math.Sin(rad);
        double hw = w / 2.0, hh = h / 2.0;
        double rx = hw * cos - hh * sin;   // half-extent vector rotated about (l,t)
        double ry = hw * sin + hh * cos;
        return ((float)(l + rx - hw), (float)(t + ry - hh));
    }

    // --- enum maps ---

    private static TextAlignment Align(int a) => a switch
    {
        1 => TextAlignment.Center,
        2 => TextAlignment.Right,
        _ => TextAlignment.Left,
    };

    private static QrErrorCorrection Ecc(string level) => level.ToUpperInvariant() switch
    {
        "L" => QrErrorCorrection.L,
        "Q" => QrErrorCorrection.Q,
        "H" => QrErrorCorrection.H,
        _ => QrErrorCorrection.M,
    };

    private static BarcodeSymbology Symbology(string type, List<string> warnings) =>
        type.ToUpperInvariant() switch
        {
            "CODE_128" => BarcodeSymbology.Code128,
            "CODE_39" => BarcodeSymbology.Code39,
            "CODE_93" => BarcodeSymbology.Code93,
            "EAN_13" => BarcodeSymbology.Ean13,
            "EAN_8" => BarcodeSymbology.Ean8,
            "UPC_A" => BarcodeSymbology.UpcA,
            "UPC_E" => BarcodeSymbology.UpcE,
            "ITF" => BarcodeSymbology.Itf,
            "CODABAR" => BarcodeSymbology.Codabar,
            "MSI" => BarcodeSymbology.Msi,
            "PLESSEY" => BarcodeSymbology.Plessey,
            "AZTEC" => BarcodeSymbology.Aztec,
            "GS1_128" or "GS1-128" or "EAN_128" => BarcodeSymbology.Gs1_128,
            _ => Fallback(type, warnings),
        };

    private static BarcodeSymbology Fallback(string type, List<string> warnings)
    {
        warnings.Add($"Unknown barcode type '{type}' — imported as Code128.");
        return BarcodeSymbology.Code128;
    }

    // --- XML attribute helpers (invariant-culture numbers) ---

    private static string TextValue(XElement obj)
    {
        IEnumerable<string> values = obj.Element("textlist")?.Elements("text")
            .Select(t => (string?)t.Attribute("value") ?? "") ?? [];
        return string.Join("\n", values.Where(v => v.Length > 0));
    }

    private static string S(XElement e, string name, string fallback) =>
        (string?)e.Attribute(name) is { Length: > 0 } v ? v : fallback;

    private static float F(XElement e, string name, float fallback) =>
        float.TryParse((string?)e.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out float v)
            ? v : fallback;

    private static int I(XElement e, string name, int fallback) =>
        int.TryParse((string?)e.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
            ? v : fallback;

    private static bool B(XElement e, string name) =>
        string.Equals((string?)e.Attribute(name), "true", StringComparison.OrdinalIgnoreCase);
}
