namespace Flowframes.Magick.Panning
{
    public struct ShiftCompareResult
    {
        public ShiftCompareResult(double fullMatch, double offsetMatch, bool hasOffsetMatch)
        {
            FullMatch = fullMatch;
            OffsetMatch = offsetMatch;
            HasOffsetMatch = hasOffsetMatch;
        }

        public double FullMatch { get; }

        public double OffsetMatch { get; }

        public bool HasOffsetMatch { get; }
    }
}
