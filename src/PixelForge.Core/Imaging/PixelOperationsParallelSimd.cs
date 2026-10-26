
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace PixelForge.Core.Imaging;

public static class PixelOperationsParallelSimd {
    private const uint RgbMask = 0x00FF_FFFFu;
    private const int BytesPerPixel = 4;

    #region Inversion: in-place

    public static unsafe void InvertBgra32(
        Span<byte> pixels,
        int width,
        int height,
        int rowBytes,
        int maxDegreeOfParallelism = -1) {

        int rowLength = ValidateInPlace(pixels, width, height, rowBytes);

        ValidateDegree(maxDegreeOfParallelism);

        fixed (byte* pointer = pixels) {
            nint address = (nint)pointer;
            Action<int> rowAction = y => InvertInPlaceRow(address, y, width, rowLength, rowBytes);
            ForRows(height, maxDegreeOfParallelism, rowAction);
        }
    }

    private static unsafe void InvertInPlaceRow(
        nint address,
        int y,
        int width,
        int rowLength,
        int rowBytes) {

        byte* rowPointer = (byte*)address + y * rowBytes;
        Span<byte> row = new(rowPointer, rowLength);

        if (BitConverter.IsLittleEndian 
        &&  rowBytes % BytesPerPixel == 0 
        &&  Vector.IsHardwareAccelerated) {

            Span<uint> words = MemoryMarshal.Cast<byte, uint>(row);
            int lanes = Vector<uint>.Count;
            var mask = new Vector<uint>(RgbMask);

            int x = 0;

            for (; x <= width - lanes; x += lanes) {
                var input = new Vector<uint>(words.Slice(x, lanes));
                (input ^ mask).CopyTo(words.Slice(x));
            }

            for (; x < width; x++)
                words[x] ^= RgbMask;

            return;
        }

        // Portable scalar fallback.
        for (int x = 0; x < rowLength; x += BytesPerPixel) {
            row[x] = (byte)(255 - row[x]);
            row[x + 1] = (byte)(255 - row[x + 1]);
            row[x + 2] = (byte)(255 - row[x + 2]);
            // Preserve alpha.
        }
    }

    #endregion

    #region Inversion: source to destination

    public static unsafe void InvertBgra32(
        ReadOnlySpan<byte> source,
        Span<byte> destination,
        int width,
        int height,
        int sourceRowBytes,
        int destinationRowBytes,
        int maxDegreeOfParallelism = -1) {

        ValidateBuffers(
            source, destination, width, height,
            sourceRowBytes, destinationRowBytes
        );

        ValidateDegree(maxDegreeOfParallelism);

        fixed (byte* sourcePointer = source)
        fixed (byte* destinationPointer = destination) {
            nint sourceAddress = (nint)sourcePointer;
            nint destinationAddress = (nint)destinationPointer;

            Action<int> rowAction = y =>
                InvertCopyRow(
                    sourceAddress,
                    destinationAddress,
                    y,
                    width,
                    sourceRowBytes,
                    destinationRowBytes
                );

            ForRows(height, maxDegreeOfParallelism, rowAction);
        }
    }

    private static unsafe void InvertCopyRow(
        nint sourceAddress,
        nint destinationAddress,
        int y,
        int width,
        int sourceRowBytes,
        int destinationRowBytes) {

        int rowLength = checked(width * BytesPerPixel);

        byte* srcPointer = (byte*)sourceAddress + y * sourceRowBytes;
        byte* dstPointer = (byte*)destinationAddress + y * destinationRowBytes;

        ReadOnlySpan<byte> sourceRow = new(srcPointer, rowLength);
        Span<byte> destinationRow = new(dstPointer, rowLength);

        if (BitConverter.IsLittleEndian 
        &&  sourceRowBytes % BytesPerPixel == 0 
        &&  destinationRowBytes % BytesPerPixel == 0 
        &&  Vector.IsHardwareAccelerated) {

            ReadOnlySpan<uint> src = MemoryMarshal.Cast<byte, uint>(sourceRow);
            Span<uint> dst = MemoryMarshal.Cast<byte, uint>(destinationRow);

            int lanes = Vector<uint>.Count;
            var mask = new Vector<uint>(RgbMask);

            int x = 0;

            for (; x <= width - lanes; x += lanes) {
                var input = new Vector<uint>(src.Slice(x, lanes));
                (input ^ mask).CopyTo(dst.Slice(x));
            }

            for (; x < width; x++)
                dst[x] = src[x] ^ RgbMask;

            return;
        }

        for (int x = 0; x < rowLength; x += BytesPerPixel) {
            destinationRow[x] = (byte)(255 - sourceRow[x]);
            destinationRow[x + 1] = (byte)(255 - sourceRow[x + 1]);
            destinationRow[x + 2] = (byte)(255 - sourceRow[x + 2]);
            destinationRow[x + 3] = sourceRow[x + 3];
        }
    }

    #endregion

    #region Blur: parallel rows + SIMD interiors

    public static unsafe void Blur3x3Bgra32(
        ReadOnlySpan<byte> source,
        Span<byte> destination,
        int width,
        int height,
        int sourceRowBytes,
        int destinationRowBytes,
        int maxDegreeOfParallelism = -1) {

        ValidateBuffers(
            source, destination, width, height,
            sourceRowBytes, destinationRowBytes
        );

        ValidateDegree(maxDegreeOfParallelism);

        fixed (byte* sourcePointer = source)
        fixed (byte* destinationPointer = destination) {

            nint sourceAddress = (nint)sourcePointer;
            nint destinationAddress = (nint)destinationPointer;

            int sourceLength = source.Length;
            int destinationLength = destination.Length;

            Action<int> rowAction = y =>
                BlurRowFromAddress(
                    sourceAddress,
                    sourceLength,
                    destinationAddress,
                    destinationLength,
                    width,
                    height,
                    sourceRowBytes,
                    destinationRowBytes,
                    y
                );

            ForRows(height, maxDegreeOfParallelism, rowAction);
        }
    }

    private static unsafe void BlurRowFromAddress(
        nint sourceAddress,
        int sourceLength,
        nint destinationAddress,
        int destinationLength,
        int width,
        int height,
        int sourceRowBytes,
        int destinationRowBytes,
        int y) {

        ReadOnlySpan<byte> source = new((void*)sourceAddress, sourceLength);
        Span<byte> destination = new((void*)destinationAddress, destinationLength);

        BlurRow(
            source, destination,
            width, height,
            sourceRowBytes, destinationRowBytes,
            y
        );
    }

    private static void BlurRow(
        ReadOnlySpan<byte> source,
        Span<byte> destination,
        int width,
        int height,
        int sourceRowBytes,
        int destinationRowBytes,
        int y) {

        int lanes = Vector<uint>.Count;

        bool useVector =
            BitConverter.IsLittleEndian &&
            Vector.IsHardwareAccelerated &&
            sourceRowBytes % BytesPerPixel == 0 &&
            destinationRowBytes % BytesPerPixel == 0 &&
            width >= lanes + 2;

        // If the row is a top/bottom border, or the row is too
        // narrow for a vectorized interior, process it scalarly.
        if (!useVector || y == 0 || y == height - 1) {
            for (int x_ = 0; x_ < width; x_++) {
                BlurPixelScalar(
                    source, destination,
                    width, height,
                    sourceRowBytes, destinationRowBytes,
                    x_, y
                );
            }

            return;
        }

        ReadOnlySpan<uint> src = MemoryMarshal.Cast<byte, uint>(source);
        Span<uint> dst = MemoryMarshal.Cast<byte, uint>(destination);

        int srcStride = sourceRowBytes / BytesPerPixel;
        int dstStride = destinationRowBytes / BytesPerPixel;

        // Horizontal left border.
        BlurPixelScalar(
            source, destination,
            width, height,
            sourceRowBytes, destinationRowBytes,
            0, y
        );

        var channelMask = new Vector<uint>(0xFFu);

        Span<uint> alphaSums = stackalloc uint[lanes];
        Span<uint> blueSums = stackalloc uint[lanes];
        Span<uint> greenSums = stackalloc uint[lanes];
        Span<uint> redSums = stackalloc uint[lanes];

        int x = 1;

        // Each lane computes one output pixel. Neighborhoods are
        // read from the source, and each row has only one writer.
        for (; x + lanes <= width - 1; x += lanes) {
            Vector<uint> sumAlpha = Vector<uint>.Zero;
            Vector<uint> sumBlue = Vector<uint>.Zero;
            Vector<uint> sumGreen = Vector<uint>.Zero;
            Vector<uint> sumRed = Vector<uint>.Zero;

            for (int dy = -1; dy <= 1; dy++) {
                int rowOffset = (y + dy) * srcStride;

                for (int dx = -1; dx <= 1; dx++) {
                    var packed = new Vector<uint>(src.Slice(rowOffset + x + dx, lanes));

                    Vector<uint> b = packed & channelMask;
                    Vector<uint> g = (packed >> 8) & channelMask;
                    Vector<uint> r = (packed >> 16) & channelMask;
                    Vector<uint> a = packed >> 24;

                    sumAlpha += a;
                    sumBlue += b * a;
                    sumGreen += g * a;
                    sumRed += r * a;
                }
            }

            // Use integer scalar division for the final output so
            // its rounding matches the original scalar algorithm.
            sumAlpha.CopyTo(alphaSums);
            sumBlue.CopyTo(blueSums);
            sumGreen.CopyTo(greenSums);
            sumRed.CopyTo(redSums);

            int dstIndex = y * dstStride + x;

            for (int lane = 0; lane < lanes; lane++) {
                uint alphaSum = alphaSums[lane];
                uint outputAlpha = (alphaSum + 4u) / 9u;

                if (alphaSum == 0 || outputAlpha == 0) {
                    dst[dstIndex + lane] = outputAlpha << 24;
                    continue;
                }

                uint blue = (blueSums[lane] + alphaSum / 2u) / alphaSum;
                uint green = (greenSums[lane] + alphaSum / 2u) / alphaSum;
                uint red = (redSums[lane] + alphaSum / 2u) / alphaSum;

                dst[dstIndex + lane] =
                    blue |
                    (green << 8) |
                    (red << 16) |
                    (outputAlpha << 24);
            }
        }

        // Scalar remainder and horizontal right border.
        for (; x < width - 1; x++)
            BlurPixelScalar(
                source, destination,
                width, height,
                sourceRowBytes, destinationRowBytes,
                x, y
            );
        
        BlurPixelScalar(
            source, destination,
            width, height,
            sourceRowBytes, destinationRowBytes,
            width - 1, y
        );
    }

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
        int sumBlue = 0;
        int sumGreen = 0;
        int sumRed = 0;

        for (int dy = -1; dy <= 1; dy++) {
            int sampleY = Math.Clamp(y + dy, 0, height - 1);
            int sourceRowOffset = sampleY * sourceRowBytes;

            for (int dx = -1; dx <= 1; dx++) {
                int sampleX = Math.Clamp(x + dx, 0, width - 1);
                int sourceIndex =
                    sourceRowOffset + sampleX * BytesPerPixel;

                int b = source[sourceIndex];
                int g = source[sourceIndex + 1];
                int r = source[sourceIndex + 2];
                int a = source[sourceIndex + 3];

                sumAlpha += a;
                sumBlue += b * a;
                sumGreen += g * a;
                sumRed += r * a;
            }
        }

        int outputAlpha = (sumAlpha + 4) / 9;
        int destinationIndex = y * destinationRowBytes + x * BytesPerPixel;

        destination[destinationIndex + 3] = (byte)outputAlpha;

        if (sumAlpha == 0 || outputAlpha == 0) {
            destination[destinationIndex] = 0;
            destination[destinationIndex + 1] = 0;
            destination[destinationIndex + 2] = 0;
            return;
        }

        destination[destinationIndex] = (byte)((sumBlue + sumAlpha / 2) / sumAlpha);
        destination[destinationIndex + 1] = (byte)((sumGreen + sumAlpha / 2) / sumAlpha);
        destination[destinationIndex + 2] = (byte)((sumRed + sumAlpha / 2) / sumAlpha);
    }

    #endregion

    #region Validation and parallel scheduling

    private static int ValidateInPlace(
        Span<byte> pixels,
        int width,
        int height,
        int rowBytes) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        int rowLength = checked(width * BytesPerPixel);

        if (rowBytes < rowLength)
            throw new ArgumentOutOfRangeException(nameof(rowBytes));

        int requiredLength =
            checked((height - 1) * rowBytes + rowLength);

        if (pixels.Length < requiredLength)
            throw new ArgumentException("Pixel buffer is too small.");

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

        int rowLength = checked(width * BytesPerPixel);

        if (sourceRowBytes < rowLength)
            throw new ArgumentOutOfRangeException(nameof(sourceRowBytes));

        if (destinationRowBytes < rowLength)
            throw new ArgumentOutOfRangeException(nameof(destinationRowBytes));

        int requiredSourceLength =
            checked((height - 1) * sourceRowBytes + rowLength);

        int requiredDestinationLength =
            checked((height - 1) * destinationRowBytes + rowLength);

        if (source.Length < requiredSourceLength)
            throw new ArgumentException("Source buffer is too small.");

        if (destination.Length < requiredDestinationLength)
            throw new ArgumentException("Destination buffer is too small.");

        if (source.Overlaps(destination)) {
            throw new ArgumentException(
                "Source and destination buffers must not overlap.");
        }
    }

    private static void ValidateDegree(int maxDegreeOfParallelism) {
        if (maxDegreeOfParallelism != -1 &&
            maxDegreeOfParallelism <= 0) {
            throw new ArgumentOutOfRangeException(
                nameof(maxDegreeOfParallelism),
                "Use -1 for the default or a positive number.");
        }
    }

    private static void ForRows(int height, int maxDegreeOfParallelism, Action<int> rowAction) {
        if (maxDegreeOfParallelism == -1)
            Parallel.For(0, height, rowAction);
        else
            Parallel.For(
                0,
                height,
                new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
                rowAction
            );
    }

    #endregion
}
