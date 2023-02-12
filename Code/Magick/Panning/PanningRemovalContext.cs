using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace Flowframes.Magick.Panning
{
    public class PanningRemovalContext
    {
        public event Action<int> Progress;

        private int _progress;

        private ConcurrentQueue<string> _addedPaths = new ConcurrentQueue<string>();

        private Stopwatch _stopwatch = Stopwatch.StartNew();

        public PanningRemovalContext(
            string[] frames, 
            int maxPanningFramesToRemove,
            double threshold,
            int maxCachedImages,
            bool allowVerHor)
        {
            Frames = frames;
            MaxPanningFramesToRemove = maxPanningFramesToRemove;
            Threshold = threshold;
            MaxCachedImages = maxCachedImages;
            AllowVerHor = allowVerHor;
        }

        public ConcurrentDictionary<ImagePanningCacheResult, bool> CachedPanningResults { get; } = new ConcurrentDictionary<ImagePanningCacheResult, bool>();

        public ConcurrentDictionary<string, MagickImageWithPath> CachedImages { get; } = new ConcurrentDictionary<string, MagickImageWithPath>();

        public ConcurrentDictionary<string, bool> UnremovableImages = new ConcurrentDictionary<string, bool>();

        public string[] Frames { get; }

        public int MaxPanningFramesToRemove { get; }

        public double Threshold { get; }

        public int MaxCachedImages { get; }

        public bool AllowVerHor { get; }

        public void ReportProgress(int framesHandled)
        {
            var progress = Interlocked.Add(ref _progress, framesHandled);
            if ( _stopwatch.ElapsedMilliseconds >= 500 || progress == Frames.Length )
            {
                lock ( _stopwatch )
                {
                    _stopwatch.Restart();

                    var pct = ( progress / (double)Frames.Length * 100 );

                    Progress?.Invoke((int)Math.Round(pct, 0));
                }
            }
        }

        public MagickImageWithPath GetOrCreateImage(string path)
        {
            if ( !CachedImages.TryGetValue(path, out var image) )
            {
                image = new MagickImageWithPath(path);
                if ( CachedImages.TryAdd(path, image) )
                {
                    _addedPaths.Enqueue(path);

                    if ( CachedImages.Count > MaxCachedImages )
                    {
                        _addedPaths.TryDequeue(out string uncachedPath);
                        CachedImages.TryRemove(uncachedPath, out _);
                    }
                }
            }

            return image;
        }
    }
}
