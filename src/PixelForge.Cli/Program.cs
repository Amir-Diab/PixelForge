using PixelForge.Application.Imaging;
using PixelForge.Core.Imaging;
using PixelForge.Infrastructure.Imaging;
using System.Buffers;
using System.Diagnostics;

var inputDirectory = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "Input Images"));
var outputDirectory = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "Output Images"));

if (!Directory.Exists(inputDirectory))
    throw new DirectoryNotFoundException($"Input directory not found: {inputDirectory}");

IImageFileLoader imageFileLoader = new SkiaImageFileLoader();
Stopwatch stopwatch = Stopwatch.StartNew();

Parallel.ForEach(Directory.EnumerateFiles(inputDirectory, "*.*", SearchOption.AllDirectories).Where(IsSupportedImage), ProcessImage);
Console.WriteLine($"Processed all images in {stopwatch.Elapsed.TotalMilliseconds:F0} milliseconds.");

void ProcessImage(string imageFilePath) {
    var outputFilePath = imageFilePath.Replace(inputDirectory, outputDirectory);
    
    using SkiaImageDocument image = (SkiaImageDocument)imageFileLoader.Load(imageFilePath);
    Span<byte> pixels = image.PixelBytes;
    
    PixelOperationsSimd.InvertBgra32(image.PixelBytes, image.Width, image.Height, image.RowBytes);
    byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(pixels.Length);

    try {
        Span<byte> destination = rentedBuffer.AsSpan(0, pixels.Length);
        PixelOperationsSimd.Blur3x3Bgra32(pixels, destination, image.Width, image.Height, image.RowBytes, image.RowBytes);
        SkiaImageDocument.Save(destination, image.Width, image.Height, image.RowBytes, image.Info, image.OriginalFormat, outputFilePath);
    } finally {
        ArrayPool<byte>.Shared.Return(rentedBuffer);
    }
}

static bool IsSupportedImage(string path) {
    return Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp";
}