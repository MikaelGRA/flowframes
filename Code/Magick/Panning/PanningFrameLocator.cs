using ImageMagick;
using System;
using System.Collections.Generic;
using System.IO;
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

            var processors = Environment.ProcessorCount - 2;
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
                                    // alright, remove what we found, then keep searching while panning and DON'T delete those
                                    //for( int k = 0; k < maxPanningFramesToRemove; k++ )
                                    //{
                                    //   var unremovableFrame = imagesToRemoveThisIteration[ k ];
                                    //   context.UnremovableImages.TryAdd( unremovableFrame, true );
                                    //   imagesToRemoveThisIteration[ k ] = null;
                                    //}

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

        static int GetThicknessByHeight(int height)
        {
            if ( height <= 480 )
            {
                return 16;
            }
            else if ( height <= 720 )
            {
                return 20;
            }
            else if ( height <= 1080 )
            {
                return 30;
            }
            else if ( height <= 1440 )
            {
                return 40;
            }
            else
            {
                return 50;
            }
        }

        static int GetLeewayByHeight(int height)
        {
            if ( height <= 480 )
            {
                return 15;
            }
            else if ( height <= 720 )
            {
                return 20;
            }
            else if ( height <= 1080 )
            {
                return 30;
            }
            else if ( height <= 1440 )
            {
                return 40;
            }
            else
            {
                return 50;
            }
        }


        static bool IsPanning(MagickImageWithPath img1, MagickImageWithPath img2, PanningRemovalContext context)
        {
            var threshold = context.Threshold;
            var key = new ImagePanningCacheResult(img1.Path, img2.Path);

            if ( context.CachedPanningResults.TryGetValue(key, out var result) )
            {
                return result;
            }

            result = false;
            try
            {
                //double diff = img1.Image.Compare( img2.Image, ErrorMetric.Fuzz ) * 100;
                bool initialCheck = img1.Image.Height == img2.Image.Height && img1.Image.Width == img2.Image.Width;// && diff > threshold;
                if ( initialCheck )
                {
                    //var horizontalResult = IsPanningHorizontally( img1.Image, img2.Image, threshold );
                    //if( horizontalResult == LinearPanningCheckResult.Match )
                    //{
                    //   result = true;
                    //   Console.WriteLine( "FAST!" );
                    //   return result;
                    //}

                    //var verticalResult = IsPanningVertically( img1.Image, img2.Image, threshold );
                    //if( verticalResult == LinearPanningCheckResult.Match )
                    //{
                    //   result = true;
                    //   Console.WriteLine( "FAST!" );
                    //   return result;
                    //}

                    var singleDirectionResult = IsPanningSingleDirection(img1.Image, img2.Image, threshold);
                    if ( singleDirectionResult == LinearPanningCheckResult.Match )
                    {
                        result = true;
                        //Console.WriteLine("FAST!");
                        return result;
                    }

                    if ( context.AllowVerHor && singleDirectionResult == LinearPanningCheckResult.PerformHorVerCheck )
                    {
                        result = IsPanningHorizontallyAndOrVertically(img1.Image, img2.Image, threshold);

                        //Console.WriteLine("SLOW: " + result);
                        //if ( result )
                        //{
                        //    //Directory.CreateDirectory( "unremoved" );
                        //    //img1.Image.Write( @"unremoved\" + Path.GetFileName( img2.Path ) );
                        //}
                    }
                }
                //else
                //{
                //    Console.WriteLine("NO CHECK");
                //}

                return result;
            }
            finally
            {
                context.CachedPanningResults.TryAdd(key, result);
            }
        }

        static ShiftCompareResult ShiftCompare(
           IMagickImage<byte> img1,
           IMagickImage<byte> img2,
           double threshold,
           int xPosition,
           int yPosition,
           int outerWidth,
           int outerHeight,
           int innerWidth,
           int innerHeight)
        {
            //img1.Write( "img1.jpg" );
            //img2.Write( "img2.jpg" );

            int xNeutralOffset = ( outerWidth - innerWidth ) / 2;
            int yNeutralOffset = ( outerHeight - innerHeight ) / 2;
            int innerBoxX = xPosition + xNeutralOffset;
            int innerBoxY = yPosition + yNeutralOffset;

            var innerBox = img1.Clone(innerBoxX, innerBoxY, innerWidth, innerHeight);
            var outerBox = img2.Clone(xPosition, yPosition, outerWidth, outerHeight);

            //innerBox.Write( "innerBox.jpg" );
            //outerBox.Write( "outerBox.jpg" );

            var result = outerBox.SubImageSearch(innerBox, ErrorMetric.Fuzz);
            var subImgDiff = result.SimilarityMetric * 100;
            if ( subImgDiff < threshold * 2 )
            {
                var match = result.BestMatch;
                var xOffset = match.X - xNeutralOffset;
                var yOffset = match.Y - yNeutralOffset;

                var sharedImageWidth = img1.Width - xOffset;
                var sharedImageHeight = img1.Height - yOffset;

                // the position of X in img1 is in position X + xOffset in img2
                // the position of Y in img1 is in position X + yOffset in img2

                var sImg1 = img1.Clone(Math.Max(-xOffset, 0), Math.Max(-yOffset, 0), sharedImageWidth, sharedImageHeight);
                var sImg2 = img2.Clone(Math.Max(xOffset, 0), Math.Max(yOffset, 0), sharedImageWidth, sharedImageHeight);

                //sImg1.Write( "sharedArea1.jpg" );
                //sImg2.Write( "sharedArea2.jpg" );

                //foreach( var metric in Enum.GetValues<ErrorMetric>() )
                //{
                //   var d = sImg1.Compare( sImg2, metric ) * 100;
                //   Console.WriteLine($"{metric}: {d:0.###}" );
                //}
                //Console.WriteLine( "---" );

                var diff = sImg1.Compare(sImg2, ErrorMetric.Fuzz) * 100;
                //if( diff < threshold * 2 && diff > threshold && ( xOffset != 0 || yOffset != 0 ) )
                //{
                //   var phash = sImg1.Compare( sImg2, ErrorMetric.PerceptualHash ) * 100;
                //   if( phash < 0.75 )
                //   {
                //      var fileName = Path.GetFileName( img2.FileName );
                //      sImg2.Write( Path.Combine( "phash", fileName ) );
                //      return new ShiftCompareResult( phash, subImgDiff, xOffset != 0 || yOffset != 0 );
                //   }
                //}

                return new ShiftCompareResult(diff, subImgDiff, xOffset != 0 || yOffset != 0);
            }

            return new ShiftCompareResult(10000d, subImgDiff, false);
        }

        static LinearPanningCheckResult IsPanningSingleDirection(IMagickImage<byte> img1, IMagickImage<byte> img2, double threshold)
        {
            var width = img1.Width;
            var height = img1.Height;
            var leeway = GetLeewayByHeight(height);
            var thickness = GetThicknessByHeight(height);
            var halfThickness = thickness / 2;
            var midX = width / 2;
            var midY = height / 2;

            var xOffset = ( width / 4 );
            var yOffset = -( height / 4 );

            var horizontalBarWidth = width;
            var horizontalBarHeight = thickness;

            var outerHorizontalBarWidth = width;
            var outerHorizontalBarHeight = thickness + leeway * 2;
            var outerHorizontalBarX = 0;
            var outerHorizontalBarY = midY - halfThickness - leeway;

            var horizontalResult = ShiftCompare(
               img1, img2, threshold, outerHorizontalBarX, outerHorizontalBarY + yOffset, outerHorizontalBarWidth, outerHorizontalBarHeight, horizontalBarWidth, horizontalBarHeight);

            if ( horizontalResult.FullMatch < threshold )
            {
                return LinearPanningCheckResult.Match;
            }
            else if ( !horizontalResult.HasOffsetMatch ) // if we could NOT find
            {
                //// try again?
                //Console.WriteLine("NO OFFSET MATCH!");

            }

            var verticalBarWidth = thickness;
            var verticalBarHeight = height;

            var outerVerticalBarWidth = thickness + leeway * 2;
            var outerVerticalBarHeight = height;
            var outerVerticalBarX = midX - halfThickness - leeway;
            var outerVerticalBarY = 0;

            var verticalResult = ShiftCompare(
               img1, img2, threshold, outerVerticalBarX + xOffset, outerVerticalBarY, outerVerticalBarWidth, outerVerticalBarHeight, verticalBarWidth, verticalBarHeight);

            if ( verticalResult.FullMatch < threshold )
            {
                return LinearPanningCheckResult.Match;
            }
            else if ( !verticalResult.HasOffsetMatch ) // if we could NOT find
            {
                //// try again?
                //Console.WriteLine( "NO OFFSET MATCH!" );

            }

            var checkAgain = threshold * 4;

            return verticalResult.FullMatch < checkAgain || horizontalResult.FullMatch < checkAgain ? LinearPanningCheckResult.PerformHorVerCheck : LinearPanningCheckResult.Mismatch;
        }


        static bool IsPanningHorizontallyAndOrVertically(IMagickImage<byte> img1, IMagickImage<byte> img2, double threshold)
        {
            var width = img1.Width;
            var height = img1.Height;
            var leeway = GetLeewayByHeight(height);
            var thickness = (int)( GetThicknessByHeight(height) * 1.25 );

            var innerBoxWidth = thickness * 2;
            var innerBoxHeight = thickness * 2;
            var outerBoxWidth = innerBoxWidth + ( leeway * 2 );
            var outerBoxHeight = innerBoxHeight + ( leeway * 2 );

            var towardsCenterWidth = width / 4;
            var towardsCenterHeight = height / 4;

            int required = 1;
            int successes = 0;

            // dead-center
            var x5 = ( width / 2 ) - ( outerBoxWidth / 2 );
            var y5 = ( height / 2 ) - ( outerBoxHeight / 2 );
            if ( ShiftCompare(img1, img2, threshold, x5, y5, outerBoxWidth, outerBoxHeight, innerBoxWidth, innerBoxHeight).FullMatch < threshold )
            {
                successes++;
                if ( successes >= required )
                {
                    return true;
                }
            }

            // top-left
            var x1 = towardsCenterWidth + leeway;
            var y1 = towardsCenterHeight + leeway;
            if ( ShiftCompare(img1, img2, threshold, x1, y1, outerBoxWidth, outerBoxHeight, innerBoxWidth, innerBoxHeight).FullMatch < threshold )
            {
                successes++;
                if ( successes >= required )
                {
                    return true;
                }
            }

            // bottom-right
            var x2 = width - leeway - towardsCenterWidth - outerBoxWidth;
            var y2 = height - leeway - towardsCenterHeight - outerBoxHeight;
            if ( ShiftCompare(img1, img2, threshold, x2, y2, outerBoxWidth, outerBoxHeight, innerBoxWidth, innerBoxHeight).FullMatch < threshold )
            {
                successes++;
                if ( successes >= required )
                {
                    return true;
                }
            }

            // top-right
            var x3 = width - leeway - towardsCenterWidth - outerBoxWidth;
            var y3 = towardsCenterHeight + leeway;
            if ( ShiftCompare(img1, img2, threshold, x3, y3, outerBoxWidth, outerBoxHeight, innerBoxWidth, innerBoxHeight).FullMatch < threshold )
            {
                successes++;
                if ( successes >= required )
                {
                    return true;
                }
            }

            // bottom-left
            var x4 = towardsCenterWidth + leeway;
            var y4 = height - leeway - towardsCenterHeight - outerBoxHeight;
            if ( ShiftCompare(img1, img2, threshold, x4, y4, outerBoxWidth, outerBoxHeight, innerBoxWidth, innerBoxHeight).FullMatch < threshold )
            {
                successes++;
                if ( successes >= required )
                {
                    return true;
                }
            }

            return false;
        }

        //static LinearPanningCheckResult IsPanningHorizontally( MagickImage img1, MagickImage img2, double threshold )
        //{
        //   var fallbackResult = LinearPanningCheckResult.Mismatch;
        //   var checkAgain = threshold * 3;
        //   var leeway = GetLeeway( img1.Width );
        //   var thickness = GetThickness( img1.Width );
        //   var sideDepth = thickness + leeway;
        //   var widthOffset = img1.Width - sideDepth;

        //   var rightbar = img1.Clone( img1.Width - thickness, 0, thickness, img1.Height );
        //   //rightbar.Write( "rightbar.jpg" );
        //   var rightResult = img2.Clone( img1.Width - sideDepth, 0, sideDepth, img1.Height ).SubImageSearch( rightbar, ErrorMetric.Fuzz );
        //   var rightPosition = rightResult.BestMatch;
        //   var rightDiff = rightResult.SimilarityMetric * 100;
        //   if( rightDiff < checkAgain )
        //   {
        //      var offset = img1.Width - ( widthOffset + rightPosition.X + thickness );

        //      var sharedArea1 = img1.Clone( offset, 0, img1.Width - offset, img1.Height );
        //      //sharedArea1.Write( "sharedArea1.jpg" );

        //      var sharedArea2 = img2.Clone( 0, 0, img2.Width - offset, img2.Height );
        //      //sharedArea2.Write( "sharedArea2.jpg" );

        //      double diff = sharedArea1.Compare( sharedArea2, ErrorMetric.Fuzz ) * 100;
        //      if( diff < threshold )
        //      {
        //         return LinearPanningCheckResult.Match;
        //      }
        //      else if( diff < checkAgain )
        //      {
        //         fallbackResult = LinearPanningCheckResult.PerformHorVerCheck;
        //      }
        //   }

        //   var leftbar = img1.Clone( 0, 0, thickness, img1.Height );
        //   //leftbar.Write( "leftbar.jpg" );
        //   var leftResult = img2.Clone( 0, 0, sideDepth, img2.Height ).SubImageSearch( leftbar, ErrorMetric.Fuzz );
        //   var leftPosition = leftResult.BestMatch;
        //   var leftDiff = leftResult.SimilarityMetric * 100;
        //   if( leftDiff < checkAgain )
        //   {
        //      var offset = leftPosition.X;

        //      var sharedArea1 = img1.Clone( 0, 0, img1.Width - offset, img1.Height );
        //      //sharedArea1.Write( "sharedArea1.jpg" );

        //      var sharedArea2 = img2.Clone( offset, 0, img2.Width - offset, img2.Height );
        //      //sharedArea2.Write( "sharedArea2.jpg" );

        //      double diff = sharedArea1.Compare( sharedArea2, ErrorMetric.Fuzz ) * 100;
        //      if( diff < threshold )
        //      {
        //         return LinearPanningCheckResult.Match;
        //      }
        //      else if( diff < checkAgain )
        //      {
        //         fallbackResult = LinearPanningCheckResult.PerformHorVerCheck;
        //      }
        //   }

        //   return fallbackResult;
        //}

        //static LinearPanningCheckResult IsPanningVertically( MagickImage img1, MagickImage img2, double threshold )
        //{
        //   var fallbackResult = LinearPanningCheckResult.Mismatch;
        //   var checkAgain = threshold * 3;
        //   var leeway = GetLeeway( img1.Width );
        //   var thickness = GetThickness( img1.Width );
        //   var sideDepth = thickness + leeway;
        //   var heightOffset = img1.Height - sideDepth;

        //   var topbar = img1.Clone( 0, 0, img1.Width, thickness );
        //   //topbar.Write( "topbar.jpg" );
        //   var topResult = img2.Clone( 0, 0, img1.Width, sideDepth ).SubImageSearch( topbar, ErrorMetric.Fuzz );
        //   var topPosition = topResult.BestMatch;
        //   var topDiff = topResult.SimilarityMetric * 100;
        //   if( topDiff < checkAgain )
        //   {
        //      var offset = topPosition.Y;

        //      var sharedArea1 = img1.Clone( 0, 0, img1.Width, img1.Height - offset );
        //      //sharedArea1.Write( "sharedArea1.jpg" );

        //      var sharedArea2 = img2.Clone( 0, offset, img2.Width, img2.Height - offset );
        //      //sharedArea2.Write( "sharedArea2.jpg" );

        //      double diff = sharedArea1.Compare( sharedArea2, ErrorMetric.Fuzz ) * 100;
        //      if( diff < threshold )
        //      {
        //         return LinearPanningCheckResult.Match;
        //      }
        //      else if( diff < checkAgain )
        //      {
        //         fallbackResult = LinearPanningCheckResult.PerformHorVerCheck;
        //      }
        //   }

        //   var bottombar = img1.Clone( 0, img1.Height - thickness, img1.Width, thickness );
        //   //bottombar.Write( "bottombar.jpg" );
        //   var bottomResult = img2.Clone( 0, img2.Height - sideDepth, img1.Width, sideDepth ).SubImageSearch( bottombar, ErrorMetric.Fuzz );
        //   var bottomPosition = bottomResult.BestMatch;
        //   var bottomDiff = bottomResult.SimilarityMetric * 100;
        //   if( bottomDiff < checkAgain )
        //   {
        //      // create two images to compare
        //      var offset = img1.Height - ( heightOffset + bottomPosition.Y + bottomPosition.Height );

        //      var sharedArea1 = img1.Clone( 0, offset, img1.Width, img1.Height - offset );
        //      //sharedArea1.Write( "sharedArea1.jpg" );

        //      var sharedArea2 = img2.Clone( 0, 0, img2.Width, img2.Height - offset );
        //      //sharedArea2.Write( "sharedArea2.jpg" );

        //      double diff = sharedArea1.Compare( sharedArea2, ErrorMetric.Fuzz ) * 100;
        //      if( diff < threshold )
        //      {
        //         return LinearPanningCheckResult.Match;
        //      }
        //      else if( diff < checkAgain )
        //      {
        //         fallbackResult = LinearPanningCheckResult.PerformHorVerCheck;
        //      }
        //   }

        //   return fallbackResult;
        //}

        //static bool IsPanningHorizontallyAndOrVertically( MagickImage img1, MagickImage img2, double threshold )
        //{
        //   var leeway = GetLeeway( img1.Width );
        //   var fullThickness = GetThickness( img1.Width );

        //   return IsPanningHorizontallyAndOrVertically( img1, img2, fullThickness, leeway, threshold );
        //}

        //static bool IsPanningHorizontallyAndOrVertically( IMagickImage<byte> img1, IMagickImage<byte> img2, int thickness, int leeway, double threshold )
        //{
        //   var yMod = leeway;
        //   var xMod = leeway;
        //   var sideDepth = thickness + leeway;
        //   var widthOffset = img1.Width - sideDepth;
        //   var heightOffset = img1.Height - sideDepth;

        //   var isPanningHorizontally = false;

        //   var rightbar = img1.Clone( img1.Width - thickness, yMod, thickness, img1.Height - yMod * 2 );
        //   //rightbar.Write( "rightbar.jpg" );
        //   var rightResult = img2.Clone( img1.Width - sideDepth, 0, sideDepth, img1.Height ).SubImageSearch( rightbar, ErrorMetric.Fuzz );
        //   var rightPosition = rightResult.BestMatch;
        //   var rightDiff = rightResult.SimilarityMetric * 100;
        //   if( rightDiff < threshold * 2 )
        //   {
        //      var xOffset = img1.Width - ( widthOffset + rightPosition.X + thickness );
        //      var yOffset = rightPosition.Y - yMod;

        //      var sharedArea1 = img1.Clone( xOffset, yMod, img1.Width - xOffset, img1.Height - yMod * 2 );
        //      //sharedArea1.Write( "RsharedArea1.jpg" );

        //      var sharedArea2 = img2.Clone( 0, yMod + yOffset, img2.Width - xOffset, img2.Height - yMod * 2 );
        //      //sharedArea2.Write( "RsharedArea2.jpg" );

        //      double diff = sharedArea1.Compare( sharedArea2, ErrorMetric.Fuzz ) * 100;
        //      if( diff < threshold )
        //      {
        //         if( yOffset == 0 )
        //         {
        //            return true;
        //         }
        //         isPanningHorizontally = true;
        //      }
        //   }

        //   if( !isPanningHorizontally )
        //   {
        //      var leftbar = img1.Clone( 0, yMod, thickness, img1.Height - yMod * 2 );
        //      //leftbar.Write( "leftbar.jpg" );
        //      var leftResult = img2.Clone( 0, 0, sideDepth, img2.Height ).SubImageSearch( leftbar, ErrorMetric.Fuzz );
        //      var leftPosition = leftResult.BestMatch;
        //      var leftDiff = leftResult.SimilarityMetric * 100;
        //      if( leftDiff < threshold * 2 )
        //      {
        //         var offset = leftPosition.X;
        //         var yOffset = leftPosition.Y - yMod;

        //         var sharedArea1 = img1.Clone( 0, yMod, img1.Width - offset, img1.Height - yMod * 2 );
        //         //sharedArea1.Write( "sharedArea1.jpg" );

        //         var sharedArea2 = img2.Clone( offset, yMod + yOffset, img2.Width - offset, img2.Height - yMod * 2 );
        //         //sharedArea2.Write( "sharedArea2.jpg" );

        //         double diff = sharedArea1.Compare( sharedArea2, ErrorMetric.Fuzz ) * 100;
        //         if( diff < threshold )
        //         {
        //            if( yOffset == 0 )
        //            {
        //               return true;
        //            }
        //            isPanningHorizontally = true;
        //         }
        //      }
        //   }

        //   var topbar = img1.Clone( xMod, 0, img1.Width - xMod * 2, thickness );
        //   //topbar.Write( "topbar.jpg" );
        //   var topResult = img2.Clone( 0, 0, img1.Width, sideDepth ).SubImageSearch( topbar, ErrorMetric.Fuzz );
        //   var topPosition = topResult.BestMatch;
        //   var topDiff = topResult.SimilarityMetric * 100;
        //   if( topDiff < threshold * 2 )
        //   {
        //      var yOffset = topPosition.Y;
        //      var xOffset = topPosition.X - xMod; // xOffset is between 0 and 16

        //      var sharedArea1 = img1.Clone( xMod, 0, img1.Width - xMod * 2, img1.Height - yOffset );
        //      //sharedArea1.Write( "TsharedArea1.jpg" );

        //      var sharedArea2 = img2.Clone( xMod + xOffset, yOffset, img2.Width - xMod * 2, img2.Height - yOffset );
        //      //sharedArea2.Write( "TsharedArea2.jpg" );

        //      double diff = sharedArea1.Compare( sharedArea2, ErrorMetric.Fuzz ) * 100;
        //      if( diff < threshold )
        //      {
        //         if( xOffset == 0 )
        //         {
        //            return true;
        //         }
        //         return isPanningHorizontally;
        //      }
        //   }

        //   var bottombar = img1.Clone( xMod, img1.Height - thickness, img1.Width - 2 * xMod, thickness );
        //   //bottombar.Write( "bottombar.jpg" );
        //   var bottomResult = img2.Clone( 0, img2.Height - sideDepth, img1.Width, sideDepth ).SubImageSearch( bottombar, ErrorMetric.Fuzz );
        //   var bottomPosition = bottomResult.BestMatch;
        //   var bottomDiff = bottomResult.SimilarityMetric * 100;
        //   if( bottomDiff < threshold * 2 )
        //   {
        //      // create two images to compare
        //      var yOffset = img1.Height - ( heightOffset + bottomPosition.Y + bottomPosition.Height );
        //      var xOffset = bottomPosition.X - xMod;

        //      var sharedArea1 = img1.Clone( xMod, yOffset, img1.Width - xMod * 2, img1.Height - yOffset );
        //      //sharedArea1.Write( "sharedArea1.jpg" );

        //      var sharedArea2 = img2.Clone( xMod + xOffset, 0, img2.Width - xMod * 2, img2.Height - yOffset );
        //      //sharedArea2.Write( "sharedArea2.jpg" );

        //      double diff = sharedArea1.Compare( sharedArea2, ErrorMetric.Fuzz ) * 100;
        //      if( diff < threshold )
        //      {
        //         if( xOffset == 0 )
        //         {
        //            return true;
        //         }
        //         return isPanningHorizontally;
        //      }
        //   }

        //   return false;
        //}
    }
}
