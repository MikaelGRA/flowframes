using System.Globalization;

namespace Flowframes.Magick.Panning
{

    public class FrameInfo
    {
        private int _priorDuplicateFrames = -1;

        public FrameInfo(string frame)
        {
            Path = frame;
            FrameNumber = int.Parse(System.IO.Path.GetFileNameWithoutExtension(frame), CultureInfo.InvariantCulture);
        }

        public string Path { get; }

        public int FrameNumber { get; }

        public int GetPriorDuplicateFrames()
        {
            lock ( this )
            {
                return _priorDuplicateFrames;
            }
        }


        public int SetPriorDuplicateFrames(int priorDuplicateFrames)
        {
            lock ( this )
            {
                if ( priorDuplicateFrames > _priorDuplicateFrames )
                {
                    _priorDuplicateFrames = priorDuplicateFrames;
                }

                return _priorDuplicateFrames;
            }
        }
    }
}
