namespace PixelForge.Core.Imaging;

public static class PixelOperations {

    public static void InvertBgra32(Span<byte> pixels, int width, int height, int rowBytes) {
        int pixelBytesPerRow = checked(width * 4);

        for (int y = 0; y < height; y++) {
            Span<byte> row = pixels.Slice(y * rowBytes, pixelBytesPerRow);

            for (int x = 0; x < row.Length; x += 4) {
                row[x] = (byte)(255 - row[x]);          // Blue
                row[x + 1] = (byte)(255 - row[x + 1]);  // Green
                row[x + 2] = (byte)(255 - row[x + 2]);  // Red

                // Preserve alpha at row[x + 3].
            }
        }
    }

    public static void InvertBgra32(
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
            throw new ArgumentException("Source buffer is too small.");

        if (destination.Length < requiredDestinationLength)
            throw new ArgumentException("Destination buffer is too small.");

        if (source.Overlaps(destination))
            throw new ArgumentException("Source and destination must not overlap.");

        for (int y = 0; y < height; y++) {
            int sourceOffset = y * sourceRowBytes;
            int destinationOffset = y * destinationRowBytes;

            for (int x = 0; x < rowLength; x += 4) {
                destination[destinationOffset + x] =
                    (byte)(255 - source[sourceOffset + x]);

                destination[destinationOffset + x + 1] =
                    (byte)(255 - source[sourceOffset + x + 1]);

                destination[destinationOffset + x + 2] =
                    (byte)(255 - source[sourceOffset + x + 2]);

                // Preserve alpha.
                destination[destinationOffset + x + 3] = source[sourceOffset + x + 3];
            }
        }
    }

    public static void Blur3x3Bgra32(
        ReadOnlySpan<byte> source,
        Span<byte> destination,
        int width,
        int height,
        int sourceRowBytes,
        int destinationRowBytes) {

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        const int BytesPerPixel = 4;
        int pixelBytesPerRow = checked(width * BytesPerPixel);

        if (sourceRowBytes < pixelBytesPerRow)
            throw new ArgumentOutOfRangeException(
                nameof(sourceRowBytes),
                "Source row stride is smaller than the pixel data."
            );
        
        if (destinationRowBytes < pixelBytesPerRow)
            throw new ArgumentOutOfRangeException(
                nameof(destinationRowBytes),
                "Destination row stride is smaller than the pixel data."
            );
        
        int sourceLength = checked((height - 1) * sourceRowBytes + pixelBytesPerRow);
        int destinationLength = checked((height - 1) * destinationRowBytes + pixelBytesPerRow);

        if (source.Length < sourceLength)
            throw new ArgumentException(
                "Source buffer is too small for the specified dimensions.",
                nameof(source)
            );
        
        if (destination.Length < destinationLength)
            throw new ArgumentException(
                "Destination buffer is too small for the specified dimensions.",
                nameof(destination)
            );
        
        if (source.Overlaps(destination))
            throw new ArgumentException(
                "Source and destination buffers must not overlap.",
                nameof(destination)
            );
        
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                int sumAlpha = 0;
                int sumBluePremultiplied = 0;
                int sumGreenPremultiplied = 0;
                int sumRedPremultiplied = 0;

                // Visit the 3x3 neighborhood.
                for (int dy = -1; dy <= 1; dy++) {
                    int sampleY = Math.Clamp(y + dy, 0, height - 1);
                    int sourceRowOffset = sampleY * sourceRowBytes;

                    for (int dx = -1; dx <= 1; dx++) {
                        int sampleX = Math.Clamp(x + dx, 0, width - 1);
                        int sourceIndex = sourceRowOffset + sampleX * BytesPerPixel;

                        int blue = source[sourceIndex];
                        int green = source[sourceIndex + 1];
                        int red = source[sourceIndex + 2];
                        int alpha = source[sourceIndex + 3];

                        sumAlpha += alpha;

                        // Weight color by opacity so transparent pixels
                        // do not introduce arbitrary hidden RGB values.
                        sumBluePremultiplied += blue * alpha;
                        sumGreenPremultiplied += green * alpha;
                        sumRedPremultiplied += red * alpha;
                    }
                }

                int destinationIndex = y * destinationRowBytes + x * BytesPerPixel;

                // Average alpha, rounding to the nearest integer.
                int outputAlpha = (sumAlpha + 9 / 2) / 9;

                destination[destinationIndex + 3] = (byte)outputAlpha;

                if (sumAlpha == 0 || outputAlpha == 0) {
                    // Fully transparent output has no visible color.
                    destination[destinationIndex] = 0;
                    destination[destinationIndex + 1] = 0;
                    destination[destinationIndex + 2] = 0;
                    continue;
                }

                // Convert the alpha-weighted sums back to unpremultiplied color values, with integer rounding.
                destination[destinationIndex] = (byte)((sumBluePremultiplied + sumAlpha / 2) / sumAlpha);
                destination[destinationIndex + 1] = (byte)((sumGreenPremultiplied + sumAlpha / 2) / sumAlpha);
                destination[destinationIndex + 2] = (byte)((sumRedPremultiplied + sumAlpha / 2) / sumAlpha);
            }
        }
    }

}


