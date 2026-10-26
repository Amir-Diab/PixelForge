using BenchmarkDotNet.Attributes;
using PixelForge.Core.Imaging;
using PixelForge.Infrastructure.Imaging;

namespace PixelForge.Benchmarks;

public enum ImageCategory {
    Small,
    Medium,
    Large
}

[MemoryDiagnoser]
public class ImageProcessingBenchmarks {

    [Params(ImageCategory.Small, ImageCategory.Medium, ImageCategory.Large)]
    public ImageCategory Category { get; set; }

    private byte[] _sourcePixels = [];
    private byte[] _invertedPixels = [];
    private byte[] _outputPixels = [];

    private int _width;
    private int _height;
    private int _rowBytes;

    [GlobalSetup]
    public void Setup() {
        // This environment variable should point to the "Input Images" directory containing Small/Medium/Large.
        string inputRoot =
            Environment.GetEnvironmentVariable("PIXELFORGE_INPUT_IMAGES") ??
            throw new InvalidOperationException("Set PIXELFORGE_INPUT_IMAGES to the full path of the Input Images directory.");

        string categoryDirectory = Path.Combine(inputRoot, Category.ToString());

        if (!Directory.Exists(categoryDirectory))
            throw new DirectoryNotFoundException($"Image category directory not found: {categoryDirectory}");
        
        string? imagePath = Directory
            .EnumerateFiles(categoryDirectory)
            .Where(IsSupportedImage)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (imagePath is null)
            throw new FileNotFoundException($"No supported images found in {categoryDirectory}");
        
        // Decode once, outside the timed benchmark operation.
        using var image = new SkiaImageFileLoader().Load(imagePath);

        _width = image.Width;
        _height = image.Height;
        _rowBytes = image.RowBytes;

        // Keep a stable copy of the original decoded pixels.
        _sourcePixels = image.PixelBytes.ToArray();

        // Reuse these buffers for every benchmark invocation.
        _invertedPixels = new byte[_sourcePixels.Length];
        _outputPixels = new byte[_sourcePixels.Length];

        // Initialize _invertedPixels
        PixelOperations.InvertBgra32(
            _sourcePixels,
            _invertedPixels,
            _width,
            _height,
            _rowBytes,
            _rowBytes
        );

        Console.WriteLine(
            $"Category: {Category}; " +
            $"Image: {Path.GetFileName(imagePath)}; " +
            $"Dimensions: {_width}x{_height}; " +
            $"Row bytes: {_rowBytes}"
        );
    }

    [Benchmark]
    public byte Invert_Sequential() {
        PixelOperations.InvertBgra32(
            _sourcePixels,
            _invertedPixels,
            _width,
            _height,
            _rowBytes,
            _rowBytes
        );

        // Return a value derived from the output so the result is observable to the benchmark harness.
        return _invertedPixels[
            (_height / 2) * _rowBytes + (_width / 2) * 4
        ];
    }

    [Benchmark]
    public byte Invert_SIMD() {
        PixelOperationsSimd.InvertBgra32(
            _sourcePixels,
            _invertedPixels,
            _width,
            _height,
            _rowBytes,
            _rowBytes
        );

        return _invertedPixels[
            (_height / 2) * _rowBytes + (_width / 2) * 4
        ];
    }

    [Benchmark]
    public byte Invert_ParallelSIMD() {
        PixelOperationsParallelSimd.InvertBgra32(
            _sourcePixels,
            _invertedPixels,
            _width,
            _height,
            _rowBytes,
            _rowBytes
        );

        return _invertedPixels[
            (_height / 2) * _rowBytes + (_width / 2) * 4
        ];
    }

    [Benchmark]
    public byte Blur_Sequential() {
        PixelOperations.Blur3x3Bgra32(
            _invertedPixels,
            _outputPixels,
            _width,
            _height,
            _rowBytes,
            _rowBytes
        );

        return _outputPixels[
            (_height / 2) * _rowBytes + (_width / 2) * 4
        ];
    }

    [Benchmark]
    public byte Blur_SIMD() {
        PixelOperationsSimd.Blur3x3Bgra32(
            _invertedPixels,
            _outputPixels,
            _width,
            _height,
            _rowBytes,
            _rowBytes
        );

        return _outputPixels[
            (_height / 2) * _rowBytes + (_width / 2) * 4
        ];
    }

    [Benchmark]
    public byte Blur_ParallelSIMD() {
        PixelOperationsParallelSimd.Blur3x3Bgra32(
            _invertedPixels,
            _outputPixels,
            _width,
            _height,
            _rowBytes,
            _rowBytes
        );

        return _outputPixels[
            (_height / 2) * _rowBytes + (_width / 2) * 4
        ];
    }

    [Benchmark]
    public byte InvertThenBlur_Sequential() {
        PixelOperations.InvertBgra32(
            _sourcePixels,
            _invertedPixels,
            _width,
            _height,
            _rowBytes,
            _rowBytes
        );

        PixelOperations.Blur3x3Bgra32(
            _invertedPixels,
            _outputPixels,
            _width,
            _height,
            _rowBytes,
            _rowBytes
        );

        return _outputPixels[
            (_height / 2) * _rowBytes + (_width / 2) * 4
        ];
    }


    [Benchmark]
    public byte InvertThenBlur_SIMD() {
        PixelOperationsSimd.InvertBgra32(
            _sourcePixels,
            _invertedPixels,
            _width,
            _height,
            _rowBytes,
            _rowBytes
        );

        PixelOperationsSimd.Blur3x3Bgra32(
            _invertedPixels,
            _outputPixels,
            _width,
            _height,
            _rowBytes,
            _rowBytes
        );

        return _outputPixels[
            (_height / 2) * _rowBytes + (_width / 2) * 4
        ];
    }


    [Benchmark]
    public byte InvertThenBlur_ParallelSIMD() {
        PixelOperationsParallelSimd.InvertBgra32(
            _sourcePixels,
            _invertedPixels,
            _width,
            _height,
            _rowBytes,
            _rowBytes
        );

        PixelOperationsParallelSimd.Blur3x3Bgra32(
            _invertedPixels,
            _outputPixels,
            _width,
            _height,
            _rowBytes,
            _rowBytes
        );

        return _outputPixels[
            (_height / 2) * _rowBytes + (_width / 2) * 4
        ];
    }

    private static bool IsSupportedImage(string path) {
        return Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp";
    }
}