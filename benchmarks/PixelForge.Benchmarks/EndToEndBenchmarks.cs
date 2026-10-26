
using BenchmarkDotNet.Attributes;
using PixelForge.Core.Imaging;
using PixelForge.Infrastructure.Imaging;
using PixelForge.Application.Imaging;
using System.Threading.Tasks;

namespace PixelForge.Benchmarks;

[MemoryDiagnoser]
public class EndToEndBenchmarks {
    
    [Params(ImageCategory.Small, ImageCategory.Medium, ImageCategory.Large)]
    public ImageCategory Category { get; set; }

    private const int FileParallelism = 4;
    private const int PerImageParallelism = 2;

    private string _inputCategoryDirectory = "";
    private string _outputRoot = "";
    private string[] _inputPaths = [];

    private readonly SkiaImageFileLoader _loader = new();

    private enum ProcessingVariant {
        SequentialScalar,
        SequentialSIMD,
        ParallelFilesSIMD,
        ParallelFilesParallelSIMD
    }

    [GlobalSetup]
    public void Setup() {
        string inputRoot =
            Environment.GetEnvironmentVariable("PIXELFORGE_INPUT_IMAGES")
            ?? throw new InvalidOperationException("Set PIXELFORGE_INPUT_IMAGES to your Input Images directory.");

        inputRoot = Path.GetFullPath(inputRoot);
        
        _inputCategoryDirectory = Path.Combine(inputRoot, Category.ToString());

        if (!Directory.Exists(_inputCategoryDirectory))
            throw new DirectoryNotFoundException($"Input category directory not found: " + _inputCategoryDirectory);
        
        string? configuredOutputRoot = Environment.GetEnvironmentVariable("PIXELFORGE_BENCHMARK_OUTPUT");

        _outputRoot = Path.GetFullPath(
            configuredOutputRoot ?? 
            Path.Combine(
                Directory.GetParent(inputRoot)!.FullName, "Output Images", "BenchmarkDotNet"
            )
        );

        _inputPaths = Directory
            .EnumerateFiles(
                _inputCategoryDirectory,
                "*",
                SearchOption.AllDirectories)
            .Where(IsSupportedImage)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (_inputPaths.Length == 0)
            throw new InvalidOperationException($"No supported images found in " + _inputCategoryDirectory);
        
        // Pre-create separate output directories for each variant.
        // This keeps directory creation out of the timed operation.
        foreach (ProcessingVariant variant in Enum.GetValues<ProcessingVariant>()) {
            string variantRoot = GetVariantOutputRoot(variant);

            foreach (string inputPath in _inputPaths) {
                string relativePath = Path.GetRelativePath(_inputCategoryDirectory, inputPath);
                string outputPath = Path.Combine(variantRoot, relativePath);
                string outputDirectory = Path.GetDirectoryName(outputPath)!;
                Directory.CreateDirectory(outputDirectory);
            }
        }

        Console.WriteLine(
            $"Category: {Category}; " +
            $"Images per batch: {_inputPaths.Length}; " +
            $"Input: {_inputCategoryDirectory}; " +
            $"Output: {_outputRoot}"
        );
    }

    // Baseline: everything is sequential and uses scalar algorithms.
    [Benchmark(Baseline = true)]
    public int SequentialScalar() {
        return ProcessBatch(ProcessingVariant.SequentialScalar, parallelFiles: false);
    }

    // Sequential files with SIMD pixel operations.
    [Benchmark]
    public int SequentialSIMD() {
        return ProcessBatch(ProcessingVariant.SequentialSIMD, parallelFiles: false);
    }

    // Multiple images processed concurrently, using SIMD per image.
    [Benchmark]
    public int ParallelFilesSIMD() {
        return ProcessBatch(ProcessingVariant.ParallelFilesSIMD, parallelFiles: true);
    }

    // Multiple images concurrently, with parallel SIMD operations inside each image.
    [Benchmark]
    public int ParallelFilesParallelSIMD() {
        return ProcessBatch(ProcessingVariant.ParallelFilesParallelSIMD, parallelFiles: true);
    }

    private int ProcessBatch(ProcessingVariant variant, bool parallelFiles) {
        if (parallelFiles) {
            Parallel.ForEach(
                _inputPaths,
                new ParallelOptions {
                    MaxDegreeOfParallelism = FileParallelism
                },
                inputPath => ProcessOneImage(inputPath, variant)
            );
        } else {
            foreach (string inputPath in _inputPaths)
                ProcessOneImage(inputPath, variant);
        }
        
        // One benchmark invocation represents one complete batch.
        return _inputPaths.Length;
    }

    private void ProcessOneImage(string inputPath, ProcessingVariant variant) {
        using SkiaImageDocument image = (SkiaImageDocument)_loader.Load(inputPath);

        Span<byte> pixels = image.PixelBytes;
        byte[] destination = new byte[pixels.Length];

        int width = image.Width;
        int height = image.Height;
        int rowBytes = image.RowBytes;

        switch (variant) {
            case ProcessingVariant.SequentialScalar:

                PixelOperations.InvertBgra32(pixels, width, height, rowBytes);
                PixelOperations.Blur3x3Bgra32(pixels, destination, width, height, rowBytes, rowBytes);

                break;

            case ProcessingVariant.SequentialSIMD:
            case ProcessingVariant.ParallelFilesSIMD:

                PixelOperationsSimd.InvertBgra32(pixels, width, height, rowBytes);
                PixelOperationsSimd.Blur3x3Bgra32(pixels, destination, width, height, rowBytes, rowBytes);

                break;

            case ProcessingVariant.ParallelFilesParallelSIMD:

                PixelOperationsParallelSimd.InvertBgra32(pixels, width, height, rowBytes, maxDegreeOfParallelism: PerImageParallelism);
                PixelOperationsParallelSimd.Blur3x3Bgra32(pixels, destination, width, height, rowBytes, rowBytes, maxDegreeOfParallelism: PerImageParallelism);

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(variant));
        }

        // Output names preserve the input's relative paths and extensions.
        string relativePath = Path.GetRelativePath(_inputCategoryDirectory, inputPath);
        string outputPath = Path.Combine(GetVariantOutputRoot(variant), relativePath);
        SkiaImageDocument.Save(destination, width, height, rowBytes, image.Info, image.OriginalFormat, outputPath);
    }

    private string GetVariantOutputRoot(ProcessingVariant variant) {
        return Path.Combine(_outputRoot, Category.ToString(), variant.ToString());
    }

    private static bool IsSupportedImage(string path) {
        return Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp";
    }
}
