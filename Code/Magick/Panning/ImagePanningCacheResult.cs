using System;

namespace Flowframes.Magick.Panning
{
    public struct ImagePanningCacheResult : IEquatable<ImagePanningCacheResult>
    {
        public ImagePanningCacheResult(string frame1, string frame2)
        {
            Frame1 = frame1;
            Frame2 = frame2;
        }

        public string Frame1 { get; }
        public string Frame2 { get; }

        public override bool Equals(object obj)
        {
            return obj is ImagePanningCacheResult result && Equals(result);
        }

        public bool Equals(ImagePanningCacheResult other)
        {
            return Frame1 == other.Frame1 &&
                     Frame2 == other.Frame2;
        }

        public override int GetHashCode()
        {
            return Frame1.GetHashCode() * Frame2.GetHashCode();
        }

        public static bool operator ==(ImagePanningCacheResult left, ImagePanningCacheResult right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ImagePanningCacheResult left, ImagePanningCacheResult right)
        {
            return !( left == right );
        }
    }
}
