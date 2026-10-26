
using System.Numerics;
using System.Runtime.InteropServices;

namespace PixelForge.Core.Imaging;

public static class PixelOperationsSimd {
    // On little-endian machines, a BGRA pixel viewed as a uint has:
    // B in bits 0-7, G in bits 8-15, R in bits 16-23, A in bits 24-31.
    // XOR with this mask inverts B, G and R while preserving alpha.
    private const uint RgbMask = 0x00FF_FFFFu;

    #region Inversion

    /// <summary>
    /// In-place SIMD inversion of BGRA8888 pixels.
    /// Alpha is preserved. Rows may contain padding.
    /// </summary>
    public static void InvertBgra32(Span<byte> pixels, int width, int height, int rowBytes) {
        int rowLength = ValidateInPlaceBuffer(pixels, width, height, rowBytes);

        if (!BitConverter.IsLittleEndian
        ||  rowBytes % 4 != 0 
        ||  !Vector.IsHardwareAccelerated) {
            PixelOperations.InvertBgra32(pixels, width, height, rowBytes);
            return;
        }

        Span<uint> words = MemoryMarshal.Cast<byte, uint>(pixels);
        int strideWords = rowBytes / 4;
        int lanes = Vector<uint>.Count;
        var mask = new Vector<uint>(RgbMask);

        for (int y = 0; y < height; y++) {
            Span<uint> row = words.Slice(y * strideWords, width);

            int x = 0;

            for (; x <= width - lanes; x += lanes) {
                var input = new Vector<uint>(row.Slice(x, lanes));
                (input ^ mask).CopyTo(row.Slice(x));
            }

            // Scalar tail: process pixels not filling a full vector.
            for (; x < width; x++)
                row[x] ^= RgbMask;
        }
    }

    /// <summary>
    /// SIMD inversion into a separate destination buffer.
    /// Source and destination must not overlap.
    /// </summary>
    public static void InvertBgra32(
        ReadOnlySpan<byte> source,
        Span<byte> destination,
        int width,
        int height,
        int sourceRowBytes,
        int destinationRowBytes) {
        
        ValidateBuffers(
            source, destination,
            width, height,
            sourceRowBytes, destinationRowBytes
        );

        if (!BitConverter.IsLittleEndian
        ||  sourceRowBytes % 4 != 0
        ||  destinationRowBytes % 4 != 0
        ||  !Vector.IsHardwareAccelerated) {
            PixelOperations.InvertBgra32(source, destination, width, height, sourceRowBytes, destinationRowBytes);
            return;
        }

        ReadOnlySpan<uint> sourceWords = MemoryMarshal.Cast<byte, uint>(source);
        Span<uint> destinationWords = MemoryMarshal.Cast<byte, uint>(destination);

        int sourceStrideWords = sourceRowBytes / 4;
        int destinationStrideWords = destinationRowBytes / 4;

        int lanes = Vector<uint>.Count;
        var mask = new Vector<uint>(RgbMask);

        for (int y = 0; y < height; y++) {
            ReadOnlySpan<uint> sourceRow = sourceWords.Slice(y * sourceStrideWords, width);
            Span<uint> destinationRow = destinationWords.Slice(y * destinationStrideWords, width);

            int x = 0;

            for (; x <= width - lanes; x += lanes) {
                var input = new Vector<uint>(sourceRow.Slice(x, lanes));
                (input ^ mask).CopyTo(destinationRow.Slice(x));
            }

            for (; x < width; x++)
                destinationRow[x] = sourceRow[x] ^ RgbMask;
        }
    }

    #endregion

    #region 3x3 blur
    
    /// <summary>
    /// SIMD-assisted alpha-aware 3x3 box blur.
    ///
    /// Uses clamp-to-edge borders, unpremultiplied BGRA input,
    /// and the same integer rounding as the scalar baseline.
    /// Source and destination must not overlap.
    ///
    /// SIMD vectorizes the neighborhood sums and alpha-weighted
    /// products. Final per-pixel integer divisions remain scalar.
    /// </summary>
    public static void Blur3x3Bgra32(
        ReadOnlySpan<byte> source,
        Span<byte> destination,
        int width,
        int height,
        int sourceRowBytes,
        int destinationRowBytes) {

        ValidateBuffers(
            source, destination,
            width, height,
            sourceRowBytes, destinationRowBytes
        );

        int lanes = Vector<uint>.Count;

        // Packed BGRA word processing requires little-endian layout,
        // four-byte-aligned row strides and a useful vector-sized
        // interior region. Otherwise use the existing scalar baseline.
        if (!BitConverter.IsLittleEndian
        ||  sourceRowBytes % 4 != 0
        ||  destinationRowBytes % 4 != 0
        ||  !Vector.IsHardwareAccelerated 
        ||  width < lanes + 2
        ||  height < 3) {

            PixelOperations.Blur3x3Bgra32(
                source, destination,
                width, height,
                sourceRowBytes, destinationRowBytes
            );
            return;
        }

        ReadOnlySpan<uint> src = MemoryMarshal.Cast<byte, uint>(source);
        Span<uint> dst = MemoryMarshal.Cast<byte, uint>(destination);

        int srcStride = sourceRowBytes / 4;
        int dstStride = destinationRowBytes / 4;

        var channelMask = new Vector<uint>(0xFFu);

        // Temporary lanes for transferring SIMD sums to the
        // scalar finalization stage. Stack allocation is tiny.
        Span<uint> alphaSums = stackalloc uint[Vector<uint>.Count];
        Span<uint> blueSums = stackalloc uint[Vector<uint>.Count];
        Span<uint> greenSums = stackalloc uint[Vector<uint>.Count];
        Span<uint> redSums = stackalloc uint[Vector<uint>.Count];

        for (int y = 0; y < height; y++) {
            // Top and bottom rows use the scalar implementation
            // to handle clamped vertical borders.
            if (y == 0 || y == height - 1) {
                for (int x_ = 0; x_ < width; x_++) {
                    BlurPixelScalar(
                        source, destination,
                        width, height,
                        sourceRowBytes, destinationRowBytes,
                        x_, y);
                }

                continue;
            }

            // The leftmost pixel is a horizontal border.
            BlurPixelScalar(
                source, destination,
                width, height,
                sourceRowBytes, destinationRowBytes,
                0, y);

            int x = 1;

            // Process blocks of adjacent interior pixels.
            // Each vector lane represents one packed BGRA pixel.
            for (; x + lanes <= width - 1; x += lanes) {
                Vector<uint> sumAlpha = Vector<uint>.Zero;
                Vector<uint> sumBluePremultiplied = Vector<uint>.Zero;
                Vector<uint> sumGreenPremultiplied = Vector<uint>.Zero;
                Vector<uint> sumRedPremultiplied = Vector<uint>.Zero;
                
                for (int dy = -1; dy <= 1; dy++) {
                    int rowOffset = (y + dy) * srcStride;

                    for (int dx = -1; dx <= 1; dx++) {
                        var packed = new Vector<uint>(
                            src.Slice(
                                rowOffset + x + dx,
                                lanes));

                        Vector<uint> blue = packed & channelMask;
                        Vector<uint> green =
                            (packed >> 8) & channelMask;
                        Vector<uint> red =
                            (packed >> 16) & channelMask;
                        Vector<uint> alpha = packed >> 24;

                        sumAlpha += alpha;

                        sumBluePremultiplied += blue * alpha;
                        sumGreenPremultiplied += green * alpha;
                        sumRedPremultiplied += red * alpha;
                    }
                }

                // The sums are exact integer values. Copy them out
                // so the original integer division/rounding behavior
                // can be retained for each output pixel.
                sumAlpha.CopyTo(alphaSums);
                sumBluePremultiplied.CopyTo(blueSums);
                sumGreenPremultiplied.CopyTo(greenSums);
                sumRedPremultiplied.CopyTo(redSums);

                int destinationIndex = y * dstStride + x;

                for (int lane = 0; lane < lanes; lane++) {
                    uint alphaSum = alphaSums[lane];
                    uint outputAlpha = (alphaSum + 4u) / 9u;

                    if (alphaSum == 0 || outputAlpha == 0) {
                        // Fully transparent output: RGB is zero.
                        dst[destinationIndex + lane] =
                            outputAlpha << 24;

                        continue;
                    }

                    uint blue =
                        (blueSums[lane] + alphaSum / 2u) / alphaSum;

                    uint green =
                        (greenSums[lane] + alphaSum / 2u) / alphaSum;

                    uint red =
                        (redSums[lane] + alphaSum / 2u) / alphaSum;

                    dst[destinationIndex + lane] =
                        blue |
                        (green << 8) |
                        (red << 16) |
                        (outputAlpha << 24);
                }
            }

            // Remaining interior pixels that do not fill a vector.
            for (; x < width - 1; x++) {
                BlurPixelScalar(
                    source, destination,
                    width, height,
                    sourceRowBytes, destinationRowBytes,
                    x, y);
            }

            // Rightmost pixel is a horizontal border.
            BlurPixelScalar(
                source, destination,
                width, height,
                sourceRowBytes, destinationRowBytes,
                width - 1, y);
        }
    }
    
    #endregion

    #region Scalar helpers

    private static void BlurPixelScalar(
        ReadOnlySpan<byte> source,
        Span<byte> destination,
        int width,
        int height,
        int sourceRowBytes,
        int destinationRowBytes,
        int x,
        int y) {

        int sumAlpha = 0;
        int sumBluePremultiplied = 0;
        int sumGreenPremultiplied = 0;
        int sumRedPremultiplied = 0;

        for (int dy = -1; dy <= 1; dy++) {
            int sampleY = Math.Clamp(y + dy, 0, height - 1);
            int sourceRowOffset = sampleY * sourceRowBytes;

            for (int dx = -1; dx <= 1; dx++) {
                int sampleX = Math.Clamp(x + dx, 0, width - 1);
                int sourceIndex =
                    sourceRowOffset + sampleX * 4;

                int blue = source[sourceIndex];
                int green = source[sourceIndex + 1];
                int red = source[sourceIndex + 2];
                int alpha = source[sourceIndex + 3];

                sumAlpha += alpha;
                sumBluePremultiplied += blue * alpha;
                sumGreenPremultiplied += green * alpha;
                sumRedPremultiplied += red * alpha;
            }
        }

        int outputAlpha = (sumAlpha + 4) / 9;
        int destinationIndex = y * destinationRowBytes + x * 4;

        destination[destinationIndex + 3] = (byte)outputAlpha;

        if (sumAlpha == 0 || outputAlpha == 0) {
            destination[destinationIndex] = 0;
            destination[destinationIndex + 1] = 0;
            destination[destinationIndex + 2] = 0;
            return;
        }

        destination[destinationIndex] =
            (byte)((sumBluePremultiplied + sumAlpha / 2) / sumAlpha);

        destination[destinationIndex + 1] =
            (byte)((sumGreenPremultiplied + sumAlpha / 2) / sumAlpha);

        destination[destinationIndex + 2] =
            (byte)((sumRedPremultiplied + sumAlpha / 2) / sumAlpha);
    }

    private static int ValidateInPlaceBuffer(Span<byte> pixels, int width, int height, int rowBytes) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        int rowLength = checked(width * 4);

        if (rowBytes < rowLength) 
            throw new ArgumentOutOfRangeException(nameof(rowBytes));

        int requiredLength = checked((height - 1) * rowBytes + rowLength);

        if (pixels.Length < requiredLength)
            throw new ArgumentException("Pixel buffer is too small.", nameof(pixels));

        return rowLength;
    }

    private static void ValidateBuffers(
        ReadOnlySpan<byte> source,
        Span<byte> destination,
        int width,
        int height,
        int sourceRowBytes,
        int destinationRowBytes) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        int rowLength = checked(width * 4);

        if (sourceRowBytes < rowLength) 
            throw new ArgumentOutOfRangeException(nameof(sourceRowBytes));

        if (destinationRowBytes < rowLength)
            throw new ArgumentOutOfRangeException(nameof(destinationRowBytes));

        int requiredSourceLength = checked((height - 1) * sourceRowBytes + rowLength);
        int requiredDestinationLength = checked((height - 1) * destinationRowBytes + rowLength);

        if (source.Length < requiredSourceLength)
            throw new ArgumentException("Source buffer is too small.", nameof(source));
        
        if (destination.Length < requiredDestinationLength)
            throw new ArgumentException("Destination buffer is too small.", nameof(destination));
        
        if (source.Overlaps(destination))
            throw new ArgumentException("Source and destination must not overlap.", nameof(destination));
    }

    #endregion
}
