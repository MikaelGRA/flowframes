namespace Flowframes.Magick.Panning
{
    public class FrameInfo
    {
        private int _priorDuplicateFrames = -1;

        public FrameInfo(string frame)
        {
            Path = frame;
        }

        public string Path { get; }

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
