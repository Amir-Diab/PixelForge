
using PixelForge.Core.Imaging;
using Xunit;

namespace PixelForge.Tests.Imaging;

public sealed class PixelOperationsTests {
    private const byte DestinationSentinel = 0xCD;

    // ---------------------------------------------------------
    // Known expected output
    // ---------------------------------------------------------

    [Fact]
    public void Invert_InPlace_InvertsRgbAndPreservesAlpha() {
        byte[] pixels =
        [
            10, 20, 30, 40,
            100, 150, 200, 255
        ];

        byte[] expected =
        [
            245, 235, 225, 40,
            155, 105, 55, 255
        ];

        PixelOperations.InvertBgra32(pixels, width: 2, height: 1, rowBytes: 8);
        Assert.Equal(expected, pixels);
    }

    [Fact]
    public void Blur_ConstantOpaqueImage_RemainsUnchanged() {
        const int width = 3;
        const int height = 3;
        const int rowBytes = width * 4;

        byte[] source = new byte[height * rowBytes];

        for (int i = 0; i < source.Length; i += 4) {
            source[i] = 20;       // Blue
            source[i + 1] = 50;   // Green
            source[i + 2] = 80;   // Red
            source[i + 3] = 255;  // Alpha
        }

        byte[] destination = new byte[source.Length];

        PixelOperations.Blur3x3Bgra32(source, destination, width, height, rowBytes, rowBytes);
        Assert.Equal(source, destination);
    }

    [Fact]
    public void Blur_FullyTransparentPixels_ProducesTransparentBlack() {
        const int width = 3;
        const int height = 3;
        const int rowBytes = width * 4;

        byte[] source = CreateSource(width, height, rowBytes, seed: 1);

        // Give every pixel zero alpha but arbitrary hidden RGB values.
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                source[y * rowBytes + x * 4 + 3] = 0;
        
        byte[] destination = new byte[source.Length];

        PixelOperations.Blur3x3Bgra32(source, destination, width, height, rowBytes, rowBytes);
        Assert.All(destination, value => Assert.Equal((byte)0, value));
    }

    [Fact]
    public void Blur_ClampsImageBorders() {
        // A 2x2 grayscale image, with every pixel opaque:
        //
        //   0   90
        // 180  255
        //
        // The top-left 3x3 neighborhood contains:
        // 0 four times, 90 twice, 180 twice, 255 once.
        //
        // Average = 795 / 9 = 88.333..., rounded to 88.

        byte[] source =
        [
            0, 0, 0, 255,
            90, 90, 90, 255,
            180, 180, 180, 255,
            255, 255, 255, 255
        ];

        byte[] destination = new byte[source.Length];

        PixelOperations.Blur3x3Bgra32(
            source, destination,
            width: 2, height: 2,
            sourceRowBytes: 8,
            destinationRowBytes: 8
        );

        Assert.Equal(
            new byte[] { 88, 88, 88, 255 },
            destination.AsSpan(0, 4).ToArray()
        );
    }

    // ---------------------------------------------------------
    // Inversion: separate source and destination buffers
    // ---------------------------------------------------------

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(7, 5)]
    [InlineData(17, 9)]
    [InlineData(31, 11)]
    public void InversionVariants_MatchScalar(int width, int height) {
        int sourceRowBytes = width * 4 + 8;
        int destinationRowBytes = width * 4 + 12;

        byte[] source = CreateSource(width, height, sourceRowBytes, seed: 42);
        byte[] expected = CreateDestination(height, destinationRowBytes);
        byte[] simd = CreateDestination(height, destinationRowBytes);
        byte[] parallelSimd = CreateDestination(height, destinationRowBytes);

        PixelOperations.InvertBgra32(
            source, expected,
            width, height,
            sourceRowBytes, destinationRowBytes
        );

        PixelOperationsSimd.InvertBgra32(
            source, simd,
            width, height,
            sourceRowBytes, destinationRowBytes
        );

        PixelOperationsParallelSimd.InvertBgra32(
            source, parallelSimd,
            width, height,
            sourceRowBytes, destinationRowBytes,
            maxDegreeOfParallelism: 2
        );

        Assert.Equal(expected, simd);
        Assert.Equal(expected, parallelSimd);
    }

    // ---------------------------------------------------------
    // Inversion: in-place overload
    // ---------------------------------------------------------

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(7, 5)]
    [InlineData(17, 9)]
    public void InPlaceInversionVariants_MatchScalar(int width, int height) {
        int rowBytes = width * 4 + 8;

        byte[] original = CreateSource(width, height, rowBytes, seed: 101);
        byte[] expected = original.ToArray();
        byte[] simd = original.ToArray();
        byte[] parallelSimd = original.ToArray();

        PixelOperations.InvertBgra32(expected, width, height, rowBytes);
        PixelOperationsSimd.InvertBgra32(simd, width, height, rowBytes);
        PixelOperationsParallelSimd.InvertBgra32(parallelSimd, width, height, rowBytes, maxDegreeOfParallelism: 2);

        Assert.Equal(expected, simd);
        Assert.Equal(expected, parallelSimd);
    }

    // ---------------------------------------------------------
    // Blur variants
    // ---------------------------------------------------------

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(7, 5)]
    [InlineData(17, 9)]
    [InlineData(31, 11)]
    public void BlurVariants_MatchScalar(int width, int height) {
        int sourceRowBytes = width * 4 + 8;
        int destinationRowBytes = width * 4 + 12;

        byte[] source = CreateSource(width, height, sourceRowBytes, seed: 123);
        byte[] expected = CreateDestination(height, destinationRowBytes);
        byte[] simd = CreateDestination(height, destinationRowBytes);
        byte[] parallelSimd = CreateDestination(height, destinationRowBytes);

        PixelOperations.Blur3x3Bgra32(
            source, expected,
            width, height,
            sourceRowBytes, destinationRowBytes
        );

        PixelOperationsSimd.Blur3x3Bgra32(
            source, simd,
            width, height,
            sourceRowBytes, destinationRowBytes
        );

        PixelOperationsParallelSimd.Blur3x3Bgra32(
            source, parallelSimd,
            width, height,
            sourceRowBytes, destinationRowBytes,
            maxDegreeOfParallelism: 2
        );

        Assert.Equal(expected, simd);
        Assert.Equal(expected, parallelSimd);
    }

    // ---------------------------------------------------------
    // Combined inversion + blur pipeline
    // ---------------------------------------------------------

    [Theory]
    [InlineData(1, 1)]
    [InlineData(7, 5)]
    [InlineData(17, 9)]
    [InlineData(31, 11)]
    public void CombinedPipelineVariants_MatchScalar(int width, int height) {
        int rowBytes = width * 4 + 8;

        byte[] source = CreateSource(width, height, rowBytes, seed: 987);
        byte[] scalarIntermediate = CreateDestination(height, rowBytes);
        byte[] scalarOutput = CreateDestination(height, rowBytes);
        byte[] simdIntermediate = CreateDestination(height, rowBytes);
        byte[] simdOutput = CreateDestination(height, rowBytes);
        byte[] parallelIntermediate = CreateDestination(height, rowBytes);
        byte[] parallelOutput = CreateDestination(height, rowBytes);

        // Scalar reference.
        PixelOperations.InvertBgra32(
            source, scalarIntermediate,
            width, height, rowBytes, rowBytes
        );

        PixelOperations.Blur3x3Bgra32(
            scalarIntermediate, scalarOutput,
            width, height, rowBytes, rowBytes
        );

        // SIMD.
        PixelOperationsSimd.InvertBgra32(
            source, simdIntermediate,
            width, height, rowBytes, rowBytes
        );

        PixelOperationsSimd.Blur3x3Bgra32(
            simdIntermediate, simdOutput,
            width, height, rowBytes, rowBytes
        );

        // Parallel SIMD.
        PixelOperationsParallelSimd.InvertBgra32(
            source, parallelIntermediate,
            width, height, rowBytes, rowBytes,
            maxDegreeOfParallelism: 2
        );

        PixelOperationsParallelSimd.Blur3x3Bgra32(
            parallelIntermediate, parallelOutput,
            width, height, rowBytes, rowBytes,
            maxDegreeOfParallelism: 2
        );

        Assert.Equal(scalarOutput, simdOutput);
        Assert.Equal(scalarOutput, parallelOutput);
    }

    // ---------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------

    private static byte[] CreateSource(int width, int height, int rowBytes, int seed) {
        byte[] pixels = new byte[checked(height * rowBytes)];

        // Deterministic pseudo-random RGB and padding bytes.
        new Random(seed).NextBytes(pixels);

        // Ensure the data contains transparent, partially transparent and opaque pixels.
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                int index = y * rowBytes + x * 4;

                pixels[index + 3] = ((x + 2 * y) % 4) switch {
                    0 => 0,
                    1 => 64,
                    2 => 128,
                    _ => 255
                };
            }
        }

        return pixels;
    }

    private static byte[] CreateDestination(int height, int rowBytes) {
        byte[] destination = new byte[checked(height * rowBytes)];
        // Makes it easy to detect writes into row padding.
        Array.Fill(destination, DestinationSentinel);
        return destination;
    }
}
