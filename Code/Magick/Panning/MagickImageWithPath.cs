using ImageMagick;

namespace Flowframes.Magick.Panning
{

    public class MagickImageWithPath
    {
        private MagickImage _image;

        public MagickImageWithPath(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public MagickImage Image
        {
            get
            {
                if ( _image == null )
                {
                    lock ( this )
                    {
                        if ( _image == null )
                        {
                            _image = new MagickImage(Path);
                        }
                    }
                }
                return _image;
            }
        }
    }
}
