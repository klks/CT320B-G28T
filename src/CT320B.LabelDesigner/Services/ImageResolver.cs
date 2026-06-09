using System.Drawing;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using CT320B.LabelDesigner.Core.Model;
using CT320B.LabelDesigner.Core.Model.Elements;

namespace CT320B.LabelDesigner.Services;

/// <summary>
/// Downloads and embeds images a document references only by URL (e.g. a <c>.ddl</c> paper background
/// hosted on a CDN) so they render on the canvas and the label becomes self-contained. Best-effort: a
/// failed download just leaves the element's placeholder. Rendering itself never touches the network —
/// only this does, off the UI thread, when a document is opened.
///
/// Downloads are cached to disk (keyed by URL under <see cref="AppPaths.ImageCacheDir"/>), so the same
/// CDN image isn't re-fetched the next time a template using it is opened.
/// </summary>
internal static class ImageResolver
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>Resolves every unresolved URL image in <paramref name="doc"/>. The downloads/cache reads
    /// run off the UI thread; <paramref name="onResolved"/> fires on the awaiting (UI) context after each
    /// success.</summary>
    public static async Task ResolveAsync(LabelDocument doc, Action onResolved)
    {
        foreach (ImageElement img in doc.Elements.OfType<ImageElement>().Where(NeedsResolve).ToList())
        {
            byte[]? data = await GetBytes(img.SourceUrl!).ConfigureAwait(true);
            if (data is null) continue;
            // The download is async; the user may have deleted/replaced this element meanwhile. Don't
            // resurrect an element that's no longer in the document.
            if (!doc.Elements.Contains(img)) continue;
            img.ImageData = data;
            img.SourceUrl = null;   // embedded now → drop the URL so it never re-resolves
            onResolved();
        }
    }

    private static bool NeedsResolve(ImageElement i) =>
        (i.ImageData is null || i.ImageData.Length == 0)
        && i.SourceUrl is { Length: > 0 } u
        && (u.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || u.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    // Cache-first: serve from the local cache if present, otherwise download and store it.
    private static async Task<byte[]?> GetBytes(string url)
    {
        string cache = CachePath(url);
        try
        {
            if (File.Exists(cache))
            {
                byte[] cached = await File.ReadAllBytesAsync(cache).ConfigureAwait(true);
                if (DecodesAsImage(cached)) return cached;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* fall through to download */ }

        byte[]? data = await TryDownload(url).ConfigureAwait(true);
        if (data is null) return null;

        try
        {
            Directory.CreateDirectory(AppPaths.ImageCacheDir);
            await File.WriteAllBytesAsync(cache, data).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* cache write is best-effort */ }

        return data;
    }

    private static async Task<byte[]?> TryDownload(string url)
    {
        try
        {
            byte[] data = await Http.GetByteArrayAsync(url).ConfigureAwait(true);
            return DecodesAsImage(data) ? data : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    private static bool DecodesAsImage(byte[] data)
    {
        try
        {
            using var ms = new MemoryStream(data);
            using var _ = new Bitmap(ms);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.ExternalException)
        {
            return false;
        }
    }

    private static string CachePath(string url)
    {
        string name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        return Path.Combine(AppPaths.ImageCacheDir, name + ".img");
    }
}
