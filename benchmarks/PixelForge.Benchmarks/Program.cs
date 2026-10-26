using BenchmarkDotNet.Running;
using PixelForge.Benchmarks;

BenchmarkSwitcher
    .FromAssembly(typeof(ImageProcessingBenchmarks).Assembly)
    .Run(args);