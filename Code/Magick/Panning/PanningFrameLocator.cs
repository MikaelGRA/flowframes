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
                    //if ( context.UnremovableImages.ContainsKey(frame) ) continue;

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
                                if ( imagesToRemoveThisIteration[ 0 ] != null )
                                {
                                    // Do not remove if the non-panning is caused by a scene change!

                                    double errNormalizedCrossCorrelation = img1.Image.Compare(img2.Image, ErrorMetric.NormalizedCrossCorrelation);
                                    double errRootMeanSquared = img1.Image.Compare(img2.Image, ErrorMetric.RootMeanSquared);

                                    bool rmsNccTrigger = errRootMeanSquared > ( 0.18f * 2 ) && errNormalizedCrossCorrelation < ( 0.6f / 2 );
                                    bool nccRmsTrigger = errNormalizedCrossCorrelation < ( 0.45f / 2 ) && errRootMeanSquared > ( 0.11f * 2 );

                                    if ( rmsNccTrigger && nccRmsTrigger )
                                    {
                                        context.UnremovableImages.TryAdd(img1.Path, true);
                                        context.UnremovableImages.TryAdd(img2.Path, true);

                                        //Directory.CreateDirectory( "unremoved" );
                                        //img1.Image.Write( @"unremoved\" + Path.GetFileName( img1.Path ) );
                                        //img2.Image.Write( @"unremoved\" + Path.GetFileName( img2.Path ) );
                                    }
                                }

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
            var pixels = context.PixelDepth;
            var threshold = context.Threshold;
            var key = new ImagePanningCacheResult(img1.Path, img2.Path);

            if ( context.CachedPanningResults.TryGetValue(key, out var result) )
            {
                return result;
            }

            result = false;
            try
            {
                double diff = img1.Image.Compare(img2.Image, ErrorMetric.Fuzz) * 100;
                bool initialCheck = img1.Image.Height == img2.Image.Height && img1.Image.Width == img2.Image.Width && diff > threshold;
                if ( initialCheck )
                {
                    var horizontalResult = IsPanningHorizontally(img1.Image, img2.Image, pixels, threshold);
                    if ( horizontalResult == LinearPanningCheckResult.Match )
                    {
                        result = true;
                        return result;
                    }

                    var verticalResult = IsPanningVertically(img1.Image, img2.Image, pixels, threshold);
                    if ( verticalResult == LinearPanningCheckResult.Match )
                    {
                        result = true;
                        return result;
                    }

                    //if( horizontalResult == LinearPanningCheckResult.CheckOtherDirection && verticalResult == LinearPanningCheckResult.CheckOtherDirection )
                    //{
                    //   result = IsPanningHorizontallyAndOrVertically( img1.Image, img2.Image, pixels, threshold );

                    //   return result;
                    //}
                }

                return result;
            }
            finally
            {
                context.CachedPanningResults.TryAdd(key, result);
            }
        }

        static int GetLeeway(int width)
        {
            if ( width <= 720 )
            {
                return 15;
            }
            else if ( width <= 1280 )
            {
                return 20;
            }
            else if ( width <= 1920 )
            {
                return 30;
            }
            else if ( width <= 2560 )
            {
                return 40;
            }
            else
            {
                return 50;
            }
        }

        static LinearPanningCheckResult IsPanningHorizontally(MagickImage img1, MagickImage img2, int pixels, double threshold)
        {
            var fallbackResult = LinearPanningCheckResult.Mismatch;
            var checkAgain = threshold * 1.5;
            var leeway = GetLeeway(img1.Width);
            var sideDepth = pixels + leeway;
            var widthOffset = img1.Width - sideDepth;

            var rightbar = img1.Clone(img1.Width - pixels, 0, pixels, img1.Height);
            //rightbar.Write( "rightbar.jpg" );
            var rightResult = img2.Clone(img1.Width - sideDepth, 0, sideDepth, img1.Height).SubImageSearch(rightbar, ErrorMetric.Fuzz);
            var rightPosition = rightResult.BestMatch;
            var rightDiff = rightResult.SimilarityMetric * 100;
            if ( rightDiff < threshold * 2 )
            {
                var offset = img1.Width - ( widthOffset + rightPosition.X + pixels );

                var sharedArea1 = img1.Clone(offset, 0, img1.Width - offset, img1.Height);
                //sharedArea1.Write( "sharedArea1.jpg" );

                var sharedArea2 = img2.Clone(0, 0, img2.Width - offset, img2.Height);
                //sharedArea2.Write( "sharedArea2.jpg" );

                double diff = sharedArea1.Compare(sharedArea2, ErrorMetric.Fuzz) * 100;
                if ( diff < threshold )
                {
                    return LinearPanningCheckResult.Match;
                }
                else if ( diff < checkAgain )
                {
                    fallbackResult = LinearPanningCheckResult.CheckOtherDirection;
                }
            }

            var leftbar = img1.Clone(0, 0, pixels, img1.Height);
            //leftbar.Write( "leftbar.jpg" );
            var leftResult = img2.Clone(0, 0, sideDepth, img2.Height).SubImageSearch(leftbar, ErrorMetric.Fuzz);
            var leftPosition = leftResult.BestMatch;
            var leftDiff = leftResult.SimilarityMetric * 100;
            if ( leftDiff < threshold * 2 )
            {
                var offset = leftPosition.X;

                var sharedArea1 = img1.Clone(0, 0, img1.Width - offset, img1.Height);
                //sharedArea1.Write( "sharedArea1.jpg" );

                var sharedArea2 = img2.Clone(offset, 0, img2.Width - offset, img2.Height);
                //sharedArea2.Write( "sharedArea2.jpg" );

                double diff = sharedArea1.Compare(sharedArea2, ErrorMetric.Fuzz) * 100;
                if ( diff < threshold )
                {
                    return LinearPanningCheckResult.Match;
                }
                else if ( diff < checkAgain )
                {
                    fallbackResult = LinearPanningCheckResult.CheckOtherDirection;
                }
            }

            return fallbackResult;
        }

        static LinearPanningCheckResult IsPanningVertically(MagickImage img1, MagickImage img2, int pixels, double threshold)
        {
            var fallbackResult = LinearPanningCheckResult.Mismatch;
            var checkAgain = threshold * 1.5;
            var leeway = GetLeeway(img1.Width);
            var sideDepth = pixels + leeway;
            var heightOffset = img1.Height - sideDepth;

            var topbar = img1.Clone(0, 0, img1.Width, pixels);
            //topbar.Write( "topbar.jpg" );
            var topResult = img2.Clone(0, 0, img1.Width, sideDepth).SubImageSearch(topbar, ErrorMetric.Fuzz);
            var topPosition = topResult.BestMatch;
            var topDiff = topResult.SimilarityMetric * 100;
            if ( topDiff < threshold * 2 )
            {
                var offset = topPosition.Y;

                var sharedArea1 = img1.Clone(0, 0, img1.Width, img1.Height - offset);
                //sharedArea1.Write( "sharedArea1.jpg" );

                var sharedArea2 = img2.Clone(0, offset, img2.Width, img2.Height - offset);
                //sharedArea2.Write( "sharedArea2.jpg" );

                double diff = sharedArea1.Compare(sharedArea2, ErrorMetric.Fuzz) * 100;
                if ( diff < threshold )
                {
                    return LinearPanningCheckResult.Match;
                }
                else if ( diff < checkAgain )
                {
                    fallbackResult = LinearPanningCheckResult.CheckOtherDirection;
                }
            }

            var bottombar = img1.Clone(0, img1.Height - pixels, img1.Width, pixels);
            //bottombar.Write( "bottombar.jpg" );
            var bottomResult = img2.Clone(0, img2.Height - sideDepth, img1.Width, sideDepth).SubImageSearch(bottombar, ErrorMetric.Fuzz);
            var bottomPosition = bottomResult.BestMatch;
            var bottomDiff = bottomResult.SimilarityMetric * 100;
            if ( bottomDiff < threshold * 2 )
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
                    return LinearPanningCheckResult.Match;
                }
                else if ( diff < checkAgain )
                {
                    fallbackResult = LinearPanningCheckResult.CheckOtherDirection;
                }
            }

            return fallbackResult;
        }

        static bool IsPanningHorizontallyAndOrVertically(MagickImage img1, MagickImage img2, int pixels, double threshold)
        {
            var leeway = 8;
            var yMod = leeway;
            var xMod = leeway;
            var sideDepth = pixels + leeway;
            var widthOffset = img1.Width - sideDepth;
            var heightOffset = img1.Height - sideDepth;

            var isPanningHorizontally = false;

            var rightbar = img1.Clone(img1.Width - pixels, yMod, pixels, img1.Height - yMod * 2);
            //rightbar.Write( "rightbar.jpg" );
            var rightResult = img2.Clone(img1.Width - sideDepth, 0, sideDepth, img1.Height).SubImageSearch(rightbar, ErrorMetric.Fuzz);
            var rightPosition = rightResult.BestMatch;
            var rightDiff = rightResult.SimilarityMetric * 100;
            if ( rightDiff < threshold * 2 )
            {
                var xOffset = img1.Width - ( widthOffset + rightPosition.X + pixels );
                var yOffset = rightPosition.Y - yMod;

                var sharedArea1 = img1.Clone(xOffset, yMod, img1.Width - xOffset, img1.Height - yMod * 2);
                //sharedArea1.Write( "sharedArea1.jpg" );

                var sharedArea2 = img2.Clone(0, yMod + yOffset, img2.Width - xOffset, img2.Height - yMod * 2 + yOffset);
                //sharedArea2.Write( "sharedArea2.jpg" );

                double diff = sharedArea1.Compare(sharedArea2, ErrorMetric.Fuzz) * 100;
                if ( diff < threshold )
                {
                    if ( yOffset == 0 )
                    {
                        return true;
                    }
                    isPanningHorizontally = true;
                }
            }

            if ( !isPanningHorizontally )
            {
                var leftbar = img1.Clone(0, yMod, pixels, img1.Height - yMod * 2);
                //leftbar.Write( "leftbar.jpg" );
                var leftResult = img2.Clone(0, 0, sideDepth, img2.Height).SubImageSearch(leftbar, ErrorMetric.Fuzz);
                var leftPosition = leftResult.BestMatch;
                var leftDiff = leftResult.SimilarityMetric * 100;
                if ( leftDiff < threshold * 2 )
                {
                    var offset = leftPosition.X;
                    var yOffset = leftPosition.Y - yMod;

                    var sharedArea1 = img1.Clone(0, yMod, img1.Width - offset, img1.Height - yMod * 2);
                    //sharedArea1.Write( "sharedArea1.jpg" );

                    var sharedArea2 = img2.Clone(offset, yMod + yOffset, img2.Width - offset, img2.Height - yMod * 2 + yOffset);
                    //sharedArea2.Write( "sharedArea2.jpg" );

                    double diff = sharedArea1.Compare(sharedArea2, ErrorMetric.Fuzz) * 100;
                    if ( diff < threshold )
                    {
                        if ( yOffset == 0 )
                        {
                            return true;
                        }
                        isPanningHorizontally = true;
                    }
                }
            }

            var topbar = img1.Clone(xMod, 0, img1.Width - xMod * 2, pixels);
            //topbar.Write( "topbar.jpg" );
            var topResult = img2.Clone(0, 0, img1.Width, sideDepth).SubImageSearch(topbar, ErrorMetric.Fuzz);
            var topPosition = topResult.BestMatch;
            var topDiff = topResult.SimilarityMetric * 100;
            if ( topDiff < threshold * 2 )
            {
                var yOffset = topPosition.Y;
                var xOffset = topPosition.X - xMod; // xOffset is between 0 and 16

                var sharedArea1 = img1.Clone(xMod, 0, img1.Width - xMod * 2, img1.Height - yOffset);
                //sharedArea1.Write( "sharedArea1.jpg" );

                var sharedArea2 = img2.Clone(xMod + xOffset, yOffset, img2.Width - xMod * 2 + xOffset, img2.Height - yOffset);
                //sharedArea2.Write( "sharedArea2.jpg" );

                double diff = sharedArea1.Compare(sharedArea2, ErrorMetric.Fuzz) * 100;
                if ( diff < threshold )
                {
                    if ( xOffset == 0 )
                    {
                        return true;
                    }
                    return isPanningHorizontally;
                }
            }

            var bottombar = img1.Clone(xMod, img1.Height - pixels, img1.Width - 2 * xMod, pixels);
            //bottombar.Write( "bottombar.jpg" );
            var bottomResult = img2.Clone(0, img2.Height - sideDepth, img1.Width, sideDepth).SubImageSearch(bottombar, ErrorMetric.Fuzz);
            var bottomPosition = bottomResult.BestMatch;
            var bottomDiff = bottomResult.SimilarityMetric * 100;
            if ( bottomDiff < threshold * 2 )
            {
                // create two images to compare
                var yOffset = img1.Height - ( heightOffset + bottomPosition.Y + bottomPosition.Height );
                var xOffset = bottomPosition.X - xMod;

                var sharedArea1 = img1.Clone(xMod, yOffset, img1.Width - xMod * 2, img1.Height - yOffset);
                //sharedArea1.Write( "sharedArea1.jpg" );

                var sharedArea2 = img2.Clone(xMod + xOffset, 0, img2.Width - xMod * 2 + xOffset, img2.Height - yOffset);
                //sharedArea2.Write( "sharedArea2.jpg" );

                double diff = sharedArea1.Compare(sharedArea2, ErrorMetric.Fuzz) * 100;
                if ( diff < threshold )
                {
                    if ( xOffset == 0 )
                    {
                        return true;
                    }
                    return isPanningHorizontally;
                }
            }

            return false;
        }
    }
}
