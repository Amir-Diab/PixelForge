using PixelForge.Application.Imaging;
using PixelForge.Core.Imaging;
using SkiaSharp;

namespace PixelForge.Infrastructure.Imaging;

public sealed class SkiaImageDocument : IImageDocument {
    private SKBitmap? _bitmap;
    public readonly SKEncodedImageFormat OriginalFormat;

    public SkiaImageDocument(SKBitmap bitmap, SKEncodedImageFormat originalFormat) {
        ArgumentNullException.ThrowIfNull(bitmap);
        _bitmap = bitmap;
        OriginalFormat = originalFormat;
    }

    public int Width => GetBitmap().Width;

    public int Height => GetBitmap().Height;

    // Actual bytes per row, including any row padding.
    public int RowBytes => GetBitmap().RowBytes;

    public SKImageInfo Info => GetBitmap().Info;

    public ImagePixelFormat PixelFormat => ImagePixelFormat.Bgra8888Unpremultiplied;

    /// <summary>
    /// Wraps the bitmap's existing pixel memory.
    /// No managed byte-array copy is made here.
    /// </summary>
    public Span<byte> PixelBytes => GetBitmap().GetPixelSpan();

    public void Save(string path, int lossyQuality = 100) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        
        if (lossyQuality is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(lossyQuality), "Quality must be between 0 and 100.");

        if (Path.GetDirectoryName(path) is string parentDirectory)
            Directory.CreateDirectory(parentDirectory);

        var bitmap = GetBitmap();
        
        using var output = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None
        );

        if (!bitmap.Encode(output, OriginalFormat, lossyQuality))
            throw new NotSupportedException(
                $"The image encoder could not save this image in its original format: {OriginalFormat}."
            );
    }

    public static unsafe void Save(
        ReadOnlySpan<byte> pixels,
        int width,
        int height,
        int rowBytes,
        SKImageInfo info,
        SKEncodedImageFormat imageFormat,
        string path,
        int lossyQuality = 100) {

        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if(Path.GetDirectoryName(path) is string parentDirectory)
            Directory.CreateDirectory(parentDirectory);
        
        int rowLength = checked(width * 4);

        if (rowBytes < rowLength)
            throw new ArgumentOutOfRangeException(nameof(rowBytes));

        int requiredLength = checked((height - 1) * rowBytes + rowLength);

        if (pixels.Length < requiredLength)
            throw new ArgumentException("Pixel buffer is too small.");

        if (lossyQuality is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(lossyQuality));

        using var output = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None
        );

        // Keep the memory pinned for the entire encoding operation.
        fixed (byte* pixelPointer = pixels) {
            using var pixmap = new SKPixmap(
                info,
                (IntPtr)pixelPointer,
                rowBytes);

            if (!pixmap.Encode(output, imageFormat, lossyQuality))
                throw new NotSupportedException($"Could not encode image as {imageFormat}.");
        }
    }

    private SKBitmap GetBitmap() {
        return _bitmap ?? throw new ObjectDisposedException(nameof(SkiaImageDocument));
    }

    public void Dispose() {
        _bitmap?.Dispose();
        _bitmap = null;
    }
}