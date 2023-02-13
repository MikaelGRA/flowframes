namespace Flowframes.Magick.Panning
{

    public struct ShiftCompareResult
    {
        public ShiftCompareResult(double fullMatch, double offsetMatch)
        {
            FullMatch = fullMatch;
            OffsetMatch = offsetMatch;
        }

        public double FullMatch { get; }

        public double OffsetMatch { get; }
    }
}
