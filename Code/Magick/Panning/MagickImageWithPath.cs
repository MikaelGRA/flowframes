using ImageMagick;

namespace Flowframes.Magick.Panning
{
    public class MagickImageWithPath
    {
        public MagickImageWithPath(string path)
        {
            Path = path;
            Image = new MagickImage(path);

        }

        public string Path { get; }

        public MagickImage Image { get; set; }
    }
}
