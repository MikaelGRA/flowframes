namespace Flowframes.Magick.Panning
{
    public class FrameRange
    {
        public FrameRange(int startIndex, int endIndex)
        {
            StartIndex = startIndex;
            EndIndex = endIndex;
        }

        public int StartIndex { get; }
        public int EndIndex { get; }
    }
}
