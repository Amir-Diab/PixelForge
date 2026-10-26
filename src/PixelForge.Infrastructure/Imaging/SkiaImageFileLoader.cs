using PixelForge.Application.Imaging;
using SkiaSharp;

namespace PixelForge.Infrastructure.Imaging;

public sealed class SkiaImageFileLoader : IImageFileLoader {

    public IImageDocument Load(string path) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
            throw new FileNotFoundException("The input image file was not found.", path);
        
        using var codec = SKCodec.Create(path)
            ?? throw new InvalidDataException($"The file could not be decoded as an image: {path}");

        // Detect the actual format from the file contents,
        // rather than trusting the file extension.
        var originalFormat = codec.EncodedFormat;
        var sourceInfo = codec.Info;

        if(originalFormat is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Webp))
            throw new NotSupportedException($"The image format {originalFormat} is not supported by the application.");

        // Normalize decoded pixels to one format understood
        // by our processing algorithms.
        var targetInfo = new SKImageInfo(
            sourceInfo.Width,
            sourceInfo.Height,
            SKColorType.Bgra8888,
            SKAlphaType.Unpremul,
            sourceInfo.ColorSpace
        );
        
        var bitmap = SKBitmap.Decode(codec, targetInfo)
            ?? throw new InvalidDataException($"Failed to decode image pixels: {path}");

        return new SkiaImageDocument(bitmap, originalFormat);
    }
}