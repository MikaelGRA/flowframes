using ImageMagick;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Flowframes.Magick.Panning
{
    public static class PanningFrameLocator
    {
        public static async Task<List<string>> RemovePanningImages(PanningRemovalContext context)
        {
            var frames = context.Frames;

            var processors = Environment.ProcessorCount / 2;
            if ( processors == 0 )
            {
                processors = 1;
            }

            var framesPerProcessor = frames.Length / processors;
            if ( framesPerProcessor < 5 )
            {
                var result = await Task.Run(() => RemovePanningImages(frames, 0, frames.Length, context));

                return result.Except(context.UnremovableImages.Keys).ToList();
            }
            else
            {
                var tasks = new List<Task<List<string>>>();

                var remainderFrames = frames.Length % processors;
                for ( int i = 0;i < processors;i++ )
                {
                    var frameStart = i * framesPerProcessor;
                    var frameEnd = ( ( i + 1 ) * framesPerProcessor );
                    if ( i == processors - 1 )
                    {
                        frameEnd += remainderFrames;
                    }

                    tasks.Add(Task.Run(() => RemovePanningImages(frames, frameStart, frameEnd, context)));
                }

                var lists = await Task.WhenAll(tasks);

                return lists.SelectMany(x => x).ToHashSet().Except(context.UnremovableImages.Keys).ToList();
            }
        }


        static List<string> RemovePanningImages(string[] frames, int startIndex, int endIndex, PanningRemovalContext context)
        {
            var maxPanningFramesToRemove = context.MaxPanningFramesToRemove;
            var imagesToRemove = new HashSet<string>();
            var imagesToRemoveThisIteration = new string[ maxPanningFramesToRemove ];
            var lastProgress = startIndex - 1;

            for ( int i = startIndex;i < endIndex;i++ )
            {
                try
                {
                    var si = i;
                    var frame = frames[ i ];
                    if ( context.UnremovableImages.ContainsKey(frame) ) continue;

                    var img1 = context.GetOrCreateImage(frame);

                    for ( int j = 0;j < maxPanningFramesToRemove;j++ )
                    {
                        imagesToRemoveThisIteration[ j ] = null;
                    }

                    for ( int j = 1;j <= maxPanningFramesToRemove + 1;j++ )
                    {
                        var nextFrameIndex = si + j;
                        if ( nextFrameIndex < frames.Length )
                        {
                            var nextFrame = frames[ nextFrameIndex ];
                            var img2 = context.GetOrCreateImage(nextFrame);

                            var isPanning = IsPanning(img1, img2, context);
                            if ( isPanning )
                            {
                                i++;

                                if ( j > maxPanningFramesToRemove )
                                {
                                    // alright, then don't remove anything
                                    for ( int k = 0;k < maxPanningFramesToRemove;k++ )
                                    {
                                        var unremovableFrame = imagesToRemoveThisIteration[ k ];
                                        context.UnremovableImages.TryAdd(unremovableFrame, true);
                                        imagesToRemoveThisIteration[ k ] = null;
                                    }

                                    // Keep going until it stops panning
                                    while ( i < frames.Length )
                                    {
                                        var checkFrame = frames[ i ];

                                        img1 = img2;
                                        img2 = context.GetOrCreateImage(checkFrame);

                                        var panning = IsPanning(img1, img2, context);
                                        if ( panning )
                                        {
                                            context.UnremovableImages.TryAdd(checkFrame, true);
                                            i++;
                                        }
                                        else
                                        {
                                            break;
                                        }
                                    }

                                    break;
                                }
                                else
                                {
                                    imagesToRemoveThisIteration[ j - 1 ] = nextFrame;
                                    img1 = img2;
                                }
                            }
                            else
                            {
                                break;
                            }
                        }
                    }

                    for ( int j = 0;j < imagesToRemoveThisIteration.Length;j++ )
                    {
                        var frameToRemove = imagesToRemoveThisIteration[ j ];
                        if ( frameToRemove != null )
                        {
                            imagesToRemove.Add(frameToRemove);
                        }
                    }
                }
                finally
                {
                    // we've now handled up until i
                    var delta = i - lastProgress;

                    context.ReportProgress(delta);

                    lastProgress = i;
                }
            }

            return imagesToRemove.ToList();
        }


        static bool IsPanning(MagickImageWithPath img1, MagickImageWithPath img2, PanningRemovalContext context)
        {
            var pixelDepth = context.PixelDepth;
            var threshold = context.Threshold;
            var key = new ImagePanningCacheResult(img1.Path, img2.Path);

            if ( context.CachedPanningResults.TryGetValue(key, out var result) )
            {
                return result;
            }

            double diff = img1.Image.Compare(img2.Image, ErrorMetric.Fuzz) * 100;

            result = img1.Image.Height == img2.Image.Height && img1.Image.Width == img2.Image.Width && diff > threshold &&
               ( IsPanningHorizontally(img1.Image, img2.Image, pixelDepth, threshold) || IsPanningVertically(img1.Image, img2.Image, pixelDepth, threshold) );

            context.CachedPanningResults.TryAdd(key, result);

            return result;

        }

        static bool IsPanningHorizontally(MagickImage img1, MagickImage img2, int pixelDepth, double threshold)
        {
            var sideDepth = pixelDepth * 2;
            var widthOffset = img1.Width - sideDepth;

            var rightbar = img1.Clone(img1.Width - pixelDepth, 0, pixelDepth, img1.Height);
            //rightbar.Write( "rightbar.jpg" );
            var rightResult = img2.Clone(img1.Width - sideDepth, 0, sideDepth, img1.Height).SubImageSearch(rightbar, ErrorMetric.Fuzz, 0);
            var rightPosition = rightResult.BestMatch;
            var rightDiff = rightResult.SimilarityMetric * 100;
            if ( rightDiff < threshold )
            {
                var offset = img1.Width - ( widthOffset + rightPosition.X + pixelDepth );

                var sharedArea1 = img1.Clone(offset, 0, img1.Width - offset, img1.Height);
                //sharedArea1.Write( "sharedArea1.jpg" );

                var sharedArea2 = img2.Clone(0, 0, img2.Width - offset, img2.Height);
                //sharedArea2.Write( "sharedArea2.jpg" );

                double diff = sharedArea1.Compare(sharedArea2, ErrorMetric.Fuzz) * 100;
                if ( diff < threshold )
                {
                    return true;
                }
            }

            var leftbar = img1.Clone(0, 0, pixelDepth, img1.Height);
            //leftbar.Write( "leftbar.jpg" );
            var leftResult = img2.Clone(0, 0, sideDepth, img2.Height).SubImageSearch(leftbar, ErrorMetric.Fuzz);
            var leftPosition = leftResult.BestMatch;
            var leftDiff = leftResult.SimilarityMetric * 100;
            if ( leftDiff < threshold )
            {
                var offset = leftPosition.X;

                var sharedArea1 = img1.Clone(0, 0, img1.Width - offset, img1.Height);
                //sharedArea1.Write( "sharedArea1.jpg" );

                var sharedArea2 = img2.Clone(offset, 0, img2.Width - offset, img2.Height);
                //sharedArea2.Write( "sharedArea2.jpg" );

                double diff = sharedArea1.Compare(sharedArea2, ErrorMetric.Fuzz) * 100;
                if ( diff < threshold )
                {
                    return true;
                }
            }


            return false;
        }

        static bool IsPanningVertically(MagickImage img1, MagickImage img2, int pixelDepth, double threshold)
        {
            var sideDepth = pixelDepth * 2;
            var heightOffset = img1.Height - sideDepth;

            var topbar = img1.Clone(0, 0, img1.Width, pixelDepth);
            //topbar.Write( "topbar.jpg" );
            var topResult = img2.Clone(0, 0, img1.Width, sideDepth).SubImageSearch(topbar, ErrorMetric.Fuzz, 0);
            var topPosition = topResult.BestMatch;
            var topDiff = topResult.SimilarityMetric * 100;
            if ( topDiff < threshold )
            {
                var offset = topPosition.Y;

                var sharedArea1 = img1.Clone(0, 0, img1.Width, img1.Height - offset);
                //sharedArea1.Write( "sharedArea1.jpg" );

                var sharedArea2 = img2.Clone(0, offset, img2.Width, img2.Height - offset);
                //sharedArea2.Write( "sharedArea2.jpg" );

                double diff = sharedArea1.Compare(sharedArea2, ErrorMetric.Fuzz) * 100;
                if ( diff < threshold )
                {
                    return true;
                }
            }

            var bottombar = img1.Clone(0, img1.Height - pixelDepth, img1.Width, pixelDepth);
            //bottombar.Write( "bottombar.jpg" );
            var bottomResult = img2.Clone(0, img2.Height - sideDepth, img1.Width, sideDepth).SubImageSearch(bottombar, ErrorMetric.Fuzz, 0);
            var bottomPosition = bottomResult.BestMatch;
            var bottomDiff = bottomResult.SimilarityMetric * 100;
            if ( bottomDiff < threshold )
            {
                // create two images to compare
                var offset = img1.Height - ( heightOffset + bottomPosition.Y + bottomPosition.Height );

                var sharedArea1 = img1.Clone(0, offset, img1.Width, img1.Height - offset);
                //sharedArea1.Write( "sharedArea1.jpg" );

                var sharedArea2 = img2.Clone(0, 0, img2.Width, img2.Height - offset);
                //sharedArea2.Write( "sharedArea2.jpg" );

                double diff = sharedArea1.Compare(sharedArea2, ErrorMetric.Fuzz) * 100;
                if ( diff < threshold )
                {
                    return true;
                }
            }

            return false;
        }
    }
}
