using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace Tapedeck.Controls;

/// <summary>
/// Shows a local image file in an <see cref="Image"/> without blocking the UI: the file is decoded in the
/// background at the width the view needs and kept in a memory cache. Scrolling a long table then only
/// swaps cached bitmaps, instead of reading and decoding a JPEG for every row that comes into view.
/// <code>&lt;Image controls:ImageLoader.DecodeWidth="144" controls:ImageLoader.Path="{Binding ThumbPath}" /&gt;</code>
/// </summary>
public sealed class ImageLoader : AvaloniaObject
{
    // twice the 72 px table cell for sharp output on high-DPI screens
    private const int DEFAULT_WIDTH = 144;

    // about 1 500 table thumbnails, or a few dozen large previews
    private const long MAX_CACHE_BYTES = 64L * 1024 * 1024;

    // all fields are only used on the UI thread; the decoding itself runs on the thread pool
    private static readonly Dictionary<(string Path, int Width), LinkedListNode<CacheEntry>> CACHE = [];
    private static readonly LinkedList<CacheEntry> RECENT = [];
    private static readonly Dictionary<(string Path, int Width), Task<Bitmap?>> LOADING = [];
    private static long CACHED_BYTES;

    /// <summary>The image file to show; <c>null</c> or a missing file leaves the image empty.</summary>
    public static readonly AttachedProperty<string?> PathProperty =
        AvaloniaProperty.RegisterAttached<ImageLoader, Image, string?>("Path");

    /// <summary>Width in pixels to decode the file at; larger images are scaled down while decoding.</summary>
    public static readonly AttachedProperty<int> DecodeWidthProperty =
        AvaloniaProperty.RegisterAttached<ImageLoader, Image, int>("DecodeWidth", DEFAULT_WIDTH);

    static ImageLoader()
    {
        PathProperty.Changed.AddClassHandler<Image>((image, _) => Show(image));
        DecodeWidthProperty.Changed.AddClassHandler<Image>((image, _) => Show(image));
    }

    public static string? GetPath(Image element) => element.GetValue(PathProperty);

    public static void SetPath(Image element, string? value) => element.SetValue(PathProperty, value);

    public static int GetDecodeWidth(Image element) => element.GetValue(DecodeWidthProperty);

    public static void SetDecodeWidth(Image element, int value) => element.SetValue(DecodeWidthProperty, value);

    private static void Show(Image image)
    {
        if (GetPath(image) is not { Length: > 0 } path)
        {
            image.Source = null;
            return;
        }

        var key = (path, GetDecodeWidth(image));
        if (CACHE.TryGetValue(key, out var node))
        {
            RECENT.Remove(node);
            RECENT.AddFirst(node);
            image.Source = node.Value.Bitmap;
            return;
        }

        // a recycled row must not show the image of the track it showed before
        image.Source = null;
        _ = ShowWhenLoadedAsync(image, key);
    }

    private static async Task ShowWhenLoadedAsync(Image image, (string Path, int Width) key)
    {
        if (!LOADING.TryGetValue(key, out var loading))
        {
            loading = Task.Run(() => Decode(key.Path, key.Width));
            LOADING[key] = loading;
        }

        var bitmap = await loading;
        if (LOADING.Remove(key) && bitmap is not null)
            Remember(key, bitmap);

        // meanwhile the row may show another track
        if (bitmap is not null && GetPath(image) == key.Path && GetDecodeWidth(image) == key.Width)
            image.Source = bitmap;
    }

    private static Bitmap? Decode(string path, int width)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            using var stream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, width);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // a broken or locked file just shows the placeholder
            return null;
        }
    }

    private static void Remember((string Path, int Width) key, Bitmap bitmap)
    {
        var size = (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4;
        CACHE[key] = RECENT.AddFirst(new CacheEntry(key, bitmap, size));
        CACHED_BYTES += size;

        // evicted bitmaps are not disposed: an image may still show them, the GC frees them once it does not
        while (CACHED_BYTES > MAX_CACHE_BYTES && RECENT.Last is { } oldest && oldest != RECENT.First)
        {
            RECENT.RemoveLast();
            CACHE.Remove(oldest.Value.Key);
            CACHED_BYTES -= oldest.Value.Bytes;
        }
    }

    private sealed record CacheEntry((string Path, int Width) Key, Bitmap Bitmap, long Bytes);
}
