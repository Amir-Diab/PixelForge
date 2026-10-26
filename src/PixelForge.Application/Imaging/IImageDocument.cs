using PixelForge.Core.Imaging;

namespace PixelForge.Application.Imaging;

/// <summary>
/// Represents an image whose pixel data can be modified.
/// The pixel span is valid only while this document remains alive.
/// </summary>
public interface IImageDocument : IDisposable {

    int Width { get; }

    int Height { get; }

    int RowBytes { get; }

    ImagePixelFormat PixelFormat { get; }

    /// <summary>
    /// Returns a view over the image's existing pixel memory.
    /// The caller must not retain this span beyond the document's lifetime.
    /// </summary>
    Span<byte> PixelBytes { get; }

    /// <summary>
    /// Saves the modified image in its original file format.
    /// lossyQuality applies to encoders that support quality settings.
    /// </summary>
    void Save(string path, int lossyQuality = 100);
}

