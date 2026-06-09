using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text.Json.Serialization;
using CT320B.LabelDesigner.Core.Rendering;

namespace CT320B.LabelDesigner.Core.Model.Elements;

/// <summary>How an image is scaled into its bounds.</summary>
public enum ImageFit
{
    /// <summary>Scale to fill the bounds exactly (may distort aspect ratio).</summary>
    Stretch,
    /// <summary>Scale to fit inside the bounds, preserving aspect (letterboxed).</summary>
    Contain,
    /// <summary>Scale to cover the bounds, preserving aspect (cropped to bounds).</summary>
    Fill,
}

/// <summary>
/// A bitmap image drawn into the element's bounds per <see cref="Fit"/>. The source is either
/// <see cref="ImageData"/> (embedded bytes — used for clip-art so the label stays self-contained and
/// portable) or, failing that, <see cref="FilePath"/>. The decoded bitmap is cached; a missing/
/// unreadable source renders a placeholder.
/// </summary>
public sealed class ImageElement : LabelElement
{
    private string? _filePath;
    private byte[]? _imageData;

    [JsonIgnore] private Bitmap? _cache;
    [JsonIgnore] private string? _cacheKey;
    [JsonIgnore] private byte[]? _cacheData;

    [JsonIgnore] private Bitmap? _ditherCache;
    [JsonIgnore] private (int w, int h, int mode, int thr, ImageFit fit, object? src) _ditherKey;

    /// <summary>Path to the source image file (used when <see cref="ImageData"/> is empty).</summary>
    public string? FilePath
    {
        get => _filePath;
        set => _filePath = value;
    }

    /// <summary>Embedded image bytes (PNG/BMP/JPG…), serialized as base64. Takes priority over
    /// <see cref="FilePath"/>; used for bundled clip-art so saved labels carry the image with them.</summary>
    public byte[]? ImageData
    {
        get => _imageData;
        set => _imageData = value;
    }

    /// <summary>Optional source URL for an image whose pixels aren't embedded yet (e.g. a <c>.ddl</c>
    /// paper background hosted on a CDN). The app downloads and embeds it into <see cref="ImageData"/>
    /// when the document is opened; rendering itself never fetches the network.</summary>
    public string? SourceUrl { get; set; }

    /// <summary>How the image is scaled into the bounds.</summary>
    public ImageFit Fit { get; set; } = ImageFit.Contain;

    /// <summary>How the image is reduced to black &amp; white for the 1-bpp printer (default
    /// <see cref="ImageDither.None"/> = draw as-is). Use Floyd–Steinberg for photos/gradients.</summary>
    public ImageDither Dither { get; set; } = ImageDither.None;

    /// <summary>Black/white cut (0–255) used by <see cref="Dither"/> (ignored when None).</summary>
    public int Threshold { get; set; } = 128;

    public override void Render(Graphics g, RenderContext ctx)
    {
        float x = ctx.MmToPx(XMm), y = ctx.MmToPx(YMm);
        float w = ctx.MmToPx(WidthMm), h = ctx.MmToPx(HeightMm);
        if (w < 1 || h < 1) return;
        var bounds = new RectangleF(x, y, w, h);

        Bitmap? img = LoadCached();
        if (img is null)
        {
            ElementPlaceholder.Draw(g, bounds, "IMAGE");
            return;
        }

        if (Dither != ImageDither.None)
        {
            DrawDithered(g, img, bounds);
            return;
        }

        RectangleF dest = Fit switch
        {
            ImageFit.Stretch => bounds,
            ImageFit.Fill => CoverRect(bounds, img.Width, img.Height),
            _ => ContainRect(bounds, img.Width, img.Height),
        };

        if (Fit == ImageFit.Fill)
        {
            GraphicsState state = g.Save();
            g.SetClip(bounds);
            g.DrawImage(img, dest);
            g.Restore(state);
        }
        else
        {
            g.DrawImage(img, dest);
        }
    }

    // Renders the (fit) image into a bounds-sized buffer, reduces it to black/white with the chosen
    // dither, and blits it 1:1 (nearest-neighbour) so the dots stay crisp on screen and on the print
    // raster. Cached by output size + mode + threshold + fit + source identity.
    private void DrawDithered(Graphics g, Bitmap img, RectangleF bounds)
    {
        int w = Math.Max(1, (int)Math.Round(bounds.Width));
        int h = Math.Max(1, (int)Math.Round(bounds.Height));
        object? src = _cacheData ?? (object?)_cacheKey;
        var key = (w, h, (int)Dither, Threshold, Fit, src);
        if (_ditherCache is null || _ditherKey != key)
        {
            _ditherCache?.Dispose();
            _ditherCache = BuildDithered(img, w, h);
            _ditherKey = key;
        }

        InterpolationMode oldI = g.InterpolationMode;
        PixelOffsetMode oldP = g.PixelOffsetMode;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_ditherCache, bounds);
        g.InterpolationMode = oldI;
        g.PixelOffsetMode = oldP;
    }

    private Bitmap BuildDithered(Bitmap img, int w, int h)
    {
        var proc = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var pg = Graphics.FromImage(proc))
        {
            pg.Clear(Color.White);
            pg.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var inner = new RectangleF(0, 0, w, h);
            RectangleF dest = Fit switch
            {
                ImageFit.Stretch => inner,
                ImageFit.Fill => CoverRect(inner, img.Width, img.Height),
                _ => ContainRect(inner, img.Width, img.Height),
            };
            if (Fit == ImageFit.Fill) pg.SetClip(inner);
            pg.DrawImage(img, dest);
        }
        Dithering.Apply(proc, Dither, Threshold);
        return proc;
    }

    private Bitmap? LoadCached()
    {
        // Embedded bytes take priority (clip-art); fall back to the file path.
        if (_imageData is { Length: > 0 })
        {
            if (ReferenceEquals(_cacheData, _imageData) && _cache is not null) return _cache;
            _cache?.Dispose();
            try
            {
                using var ms = new MemoryStream(_imageData);
                using var loaded = new Bitmap(ms);
                _cache = new Bitmap(loaded);
                _cacheData = _imageData;
                _cacheKey = null;
            }
            catch (Exception) { _cache = null; _cacheData = null; }
            return _cache;
        }

        if (string.IsNullOrEmpty(_filePath) || !File.Exists(_filePath))
        {
            _cache?.Dispose();
            _cache = null;
            _cacheKey = null;
            _cacheData = null;
            return null;
        }
        if (_cacheKey == _filePath && _cacheData is null && _cache is not null) return _cache;

        _cache?.Dispose();
        try
        {
            // Load via a stream + copy so we don't keep the file locked.
            using var fs = File.OpenRead(_filePath);
            using var loaded = new Bitmap(fs);
            _cache = new Bitmap(loaded);
            _cacheKey = _filePath;
            _cacheData = null;
        }
        catch (Exception)
        {
            _cache = null;
            _cacheKey = null;
        }
        return _cache;
    }

    private static RectangleF ContainRect(RectangleF bounds, int iw, int ih)
    {
        float scale = Math.Min(bounds.Width / iw, bounds.Height / ih);
        float w = iw * scale, h = ih * scale;
        return new RectangleF(bounds.X + (bounds.Width - w) / 2f, bounds.Y + (bounds.Height - h) / 2f, w, h);
    }

    private static RectangleF CoverRect(RectangleF bounds, int iw, int ih)
    {
        float scale = Math.Max(bounds.Width / iw, bounds.Height / ih);
        float w = iw * scale, h = ih * scale;
        return new RectangleF(bounds.X + (bounds.Width - w) / 2f, bounds.Y + (bounds.Height - h) / 2f, w, h);
    }
}
