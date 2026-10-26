# PixelForge

**A .NET image-processing project for exploring performance, memory behavior, SIMD, and parallelism.**

PixelForge implements a small image-processing pipeline and compares alternative implementations using automated tests and [BenchmarkDotNet](https://benchmarkdotnet.org/). The goal is to measure how implementation choices affect correctness, per-image processing time, and end-to-end batch throughput—not to build a full image editor.

## What it does

PixelForge loads image files, decodes them to a consistent BGRA 32-bit pixel layout, applies RGB inversion followed by an alpha-aware 3×3 box blur, and saves the results.

The solution includes:

- **Scalar implementations** used as the reference for correctness comparisons.
- **SIMD implementations** using `System.Numerics.Vector<T>` where applicable.
- **Parallel-SIMD implementations** that distribute independent image rows across worker threads and vectorize work within each row.
- **Batch processing** of image files with configurable file-level concurrency.
- **Image I/O** implemented with SkiaSharp behind application-facing abstractions.
- **Automated tests** for known outputs, alpha handling, border behavior, row stride, small dimensions, vector tails, and equivalence between implementations.
- **BenchmarkDotNet suites** for individual pixel operations, the combined inversion-and-blur pipeline, and end-to-end image batches.

## Solution structure

```text
PixelForge/
├── src/
│   ├── PixelForge.Core/           # Pixel formats and processing algorithms
│   ├── PixelForge.Application/    # Application contracts and use cases
│   ├── PixelForge.Infrastructure/ # SkiaSharp and file I/O implementations
│   └── PixelForge.Cli/            # Command-line entry point
├── tests/
│   └── PixelForge.Tests/          # Automated correctness tests
├── benchmarks/
│   └── PixelForge.Benchmarks/     # BenchmarkDotNet benchmarks
├── Input Images/                  # Add your own images; image files are not included
└── Output Images/                 # Generated output; created as needed
```

The pixel algorithms are separated from image decoding and encoding, allowing the processing implementations to be benchmarked without including file I/O in their microbenchmarks. The scalar implementation is the reference against which optimized implementations are tested.

## Requirements

- Windows (the environment used for the published measurements)
- .NET 10 SDK
- Visual Studio or another .NET-compatible development environment
- Your own input images, placed in the expected folder structure (see below)

The current image-loading and saving implementation uses SkiaSharp. Supported formats depend on the codecs and encoders available in the installed SkiaSharp packages; the project is configured for common formats such as PNG, JPEG, and WebP.

## Get started

### 1. Clone the repository

```powershell
git clone https://github.com/Amir-Diab/PixelForge.git
cd PixelForge
```


### 2. Add your own input images

PixelForge does not include or redistribute the images used for the published benchmark results. To run the application or benchmarks, provide your own images in these folders (create them if they do not already exist):

```text
Input Images/
├── Small/
├── Medium/
└── Large/
```

The category names are folder labels, not strict dimension requirements. The original benchmark run used 20 images per category with approximate target resolutions of 640 × 426 for Small, 1920 × 1280 for Medium, and 2400 × 1600 for Large. Actual image dimensions varied—for example, an image in Large could be 2400 × 1509 rather than exactly 2400 × 1600. PixelForge uses each file's actual dimensions when processing it.

Use supported image files such as PNG, JPEG, or WebP. The microbenchmark suite selects one supported image from each category (the first supported filename alphabetically), while the end-to-end suite processes all supported images in each category. The published end-to-end results used 20 images per category. If you use a different number of files, image sizes, formats, or content, expect different timings; the results are most useful for comparing implementations on the same machine and input set.

The original benchmark inputs were fully opaque photographs sourced from Unsplash and are not included in this repository. As a result, the real-image performance measurements do not represent workloads with meaningful transparency variation. Synthetic automated tests cover transparent and partially transparent pixels for correctness, but transparency-specific performance was not measured. Use images you have permission to use.

The image files are not required to build the solution or run the synthetic unit tests; they are required to run the image-processing microbenchmarks and end-to-end benchmarks. Output directories are created by the application and do not need to be prepared or committed.

### 3. Build and test

Run these commands from the solution root:

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

### 4. Configure the input directory

The CLI and benchmark setup use the `PIXELFORGE_INPUT_IMAGES` environment variable to locate the `Input Images` directory. From the repository root in PowerShell:

```powershell
$env:PIXELFORGE_INPUT_IMAGES = (Resolve-Path ".\Input Images").Path
```

The end-to-end benchmarks write generated images under `Output Images\BenchmarkDotNet` by default. You can override that root with:

```powershell
$env:PIXELFORGE_BENCHMARK_OUTPUT = Join-Path (Get-Location) "Output Images\BenchmarkDotNet"
```

The application creates output directories as needed. If the application is launched from a different working directory, set `PIXELFORGE_INPUT_IMAGES` to the absolute path of the dataset directory as shown above.

## Algorithms and correctness

### RGB inversion

The inversion operation computes `255 - channel` for the Blue, Green, and Red channels while preserving Alpha. It has scalar, SIMD, and parallel-SIMD implementations.

### 3×3 box blur

Each output pixel is calculated from a 3×3 neighborhood using clamp-to-edge border handling. RGB values are weighted by alpha during accumulation so hidden RGB values in fully transparent pixels do not contribute to the visible result. The implementations aim to preserve the same rounding, border, and alpha behavior.

### Tests

The automated test suite uses deterministic synthetic buffers to cover expected pixel values and edge cases independently of the external image dataset. It compares complete output buffers from optimized implementations against the scalar reference, including cases involving row padding, small images, and transparent pixels.

Run tests with:

```powershell
dotnet test tests/PixelForge.Tests/PixelForge.Tests.csproj -c Release
```

## Benchmarking

PixelForge has two distinct benchmark suites.

### Pixel-operation microbenchmarks

These benchmarks measure the pixel-processing methods with decoded pixels and reusable working buffers prepared outside the timed operation. They compare sequential scalar, SIMD, and parallel-SIMD variants for inversion, blur, and the combined inversion-then-blur operation.

The current microbenchmark setup selects one supported image from each category (the first supported filename in alphabetical order). The category names refer to approximate resolution groups; the actual dimensions of the selected file can differ from the nominal size.

Run the microbenchmarks from the repository root after setting `PIXELFORGE_INPUT_IMAGES`:

```powershell
dotnet run `
  --project ".\benchmarks\PixelForge.Benchmarks\PixelForge.Benchmarks.csproj" `
  -c Release `
  -- --filter "*ImageProcessingBenchmarks*"
```

### End-to-end batch benchmarks

These benchmarks process all supported image files present in the selected category. They include image decoding, working-buffer allocation, pixel processing, encoding, and writing output files. The published benchmark run used 20 images per category. The variants compare the sequential scalar baseline, sequential SIMD, parallel file processing with SIMD, and parallel file processing with parallel-SIMD operations.

Run the end-to-end benchmarks with:

```powershell
dotnet run `
  --project ".\benchmarks\PixelForge.Benchmarks\PixelForge.Benchmarks.csproj" `
  -c Release `
  -- --filter "*EndToEndBenchmarks*"
```

BenchmarkDotNet writes reports under `BenchmarkDotNet.Artifacts/`. Generated reports and output images are not required in the source repository; this README summarizes the recorded measurements below.

## Benchmark results

The results below are taken from the supplied BenchmarkDotNet reports. Each table represents one benchmark run; speedups are calculated from mean times within the same report and category. These values are measurements from one computer and one dataset, not universal performance guarantees.

### Test environment

- Windows 11 (10.0.26200.9711, 25H2)
- 13th Gen Intel Core i7-13700HX, 2.10 GHz, 16 physical cores and 24 logical processors
- .NET SDK 10.0.401; .NET runtime 10.0.12
- BenchmarkDotNet 0.15.8
- x64 RyuJIT, x86-64-v3

### Pixel-operation microbenchmarks

Mean execution time per operation. The speedup columns compare each implementation with the sequential scalar implementation for that operation and category. `Parallel-SIMD allocations` reports managed bytes allocated per operation by that variant.

| Category | Operation | Sequential | SIMD | Parallel-SIMD | SIMD speedup | Parallel-SIMD speedup | Parallel-SIMD allocated/op |
|---|---|---:|---:|---:|---:|---:|---:|
| Small | Invert | 332.75 µs | 35.78 µs | 17.80 µs | 9.30× | 18.69× | 6,006 B |
| Small | Blur 3×3 | 3,995.26 µs | 1,380.40 µs | 306.57 µs | 2.89× | 13.03× | 6,094 B |
| Small | Invert + blur | 4,316.28 µs | 1,424.19 µs | 311.41 µs | 3.03× | 13.86× | 9,691 B |
| Medium | Invert | 3,041.70 µs | 425.17 µs | 87.50 µs | 7.15× | 34.76× | 5,620 B |
| Medium | Blur 3×3 | 35,716.09 µs | 12,177.93 µs | 1,790.31 µs | 2.93× | 19.95× | 6,765 B |
| Medium | Invert + blur | 39,121.69 µs | 13,075.22 µs | 1,962.80 µs | 2.99× | 19.93× | 11,428 B |
| Large | Invert | 4,787.52 µs | 896.31 µs | 149.00 µs | 5.34× | 32.13× | 5,540 B |
| Large | Blur 3×3 | 56,119.95 µs | 18,999.72 µs | 2,852.67 µs | 2.95× | 19.67× | 6,759 B |
| Large | Invert + blur | 60,922.02 µs | 20,642.28 µs | 3,205.12 µs | 2.95× | 19.01× | 12,868 B |

Observations from this run:

- SIMD reduced the combined inversion-and-blur time by about 66% compared with sequential scalar processing.
- Parallel-SIMD was about 13.9× faster than sequential scalar for the combined Small operation, and about 19–20× faster for Medium and Large.
- Parallel-SIMD methods allocated several kilobytes per operation, consistent with additional parallel-loop and runtime overhead. The sequential and SIMD methods reported no managed allocations per operation.

### End-to-end batch benchmarks

Each invocation processes all supported images in the selected category. The published measurements used 20 images per category. Mean time includes file decoding, buffer allocation, both pixel operations, encoding, and output writes. Speedup is relative to `SequentialScalar` in the same category. `Allocated/batch` is BenchmarkDotNet's managed allocation measurement for the batch; it is not peak process memory.

| Category | Variant | Mean time per 20-image batch | Speedup | Allocated/batch |
|---|---|---:|---:|---:|
| Small | Sequential scalar | 243.63 ms | 1.00× | 20.98 MB |
| Small | Sequential SIMD | 183.55 ms | 1.33× | 20.98 MB |
| Small | Parallel files + SIMD | 53.69 ms | 4.54× | 20.99 MB |
| Small | Parallel files + parallel-SIMD | 50.14 ms | 4.86× | 21.08 MB |
| Medium | Sequential scalar | 1,915.26 ms | 1.00× | 187.46 MB |
| Medium | Sequential SIMD | 1,409.96 ms | 1.36× | 187.46 MB |
| Medium | Parallel files + SIMD | 404.47 ms | 4.74× | 187.45 MB |
| Medium | Parallel files + parallel-SIMD | 375.15 ms | 5.11× | 187.54 MB |
| Large | Sequential scalar | 2,970.82 ms | 1.00× | 292.82 MB |
| Large | Sequential SIMD | 2,185.33 ms | 1.36× | 292.82 MB |
| Large | Parallel files + SIMD | 625.65 ms | 4.75× | 292.81 MB |
| Large | Parallel files + parallel-SIMD | 576.86 ms | 5.15× | 292.91 MB |

Observations from this run:

- Sequential SIMD reduced end-to-end batch time by about 25–26% compared with sequential scalar processing.
- Parallel file processing with SIMD achieved about 4.5–4.8× speedup across the three categories.
- Adding parallel-SIMD processing within each file improved on parallel-file SIMD by a further 6.6–7.8% in these tests.
- Managed allocations per batch were similar across variants. This metric does not show peak working-set memory, so it should not be used as a substitute for a separate process-memory measurement.

### Interpreting the results

The pixel-operation microbenchmarks isolate computation from file decoding and encoding. The end-to-end measurements include those I/O and allocation costs and process a batch of files, so the two tables answer different questions. Actual results depend on the CPU, runtime, memory behavior, image dimensions and content, encoder/decoder behavior, and concurrency settings.

The original benchmark inputs were fully opaque photos, so the published performance results do not represent workloads with meaningful alpha variation. The synthetic unit tests cover transparent-pixel correctness, but transparency-specific performance was not measured with the real-image dataset.

## License and attribution


PixelForge does not bundle the input images used for the reported benchmark results. Anyone running the project should supply images they have permission to use. Third-party images remain subject to their own applicable terms and are not covered by the PixelForge source-code license.
