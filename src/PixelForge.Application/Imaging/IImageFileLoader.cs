namespace PixelForge.Application.Imaging;

public interface IImageFileLoader {
    IImageDocument Load(string path);
}