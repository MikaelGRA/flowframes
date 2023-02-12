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
                framesPerProcessor = frames.Length;
                processors = 1;
            }

            var frameInfos = frames.Select(x => new FrameInfo(x)).ToArray();

            // frameInfos up into chunks
            var ranges = new List<FrameRange>();
            if ( processors == 1 )
            {
                ranges.Add(new FrameRange(0, frameInfos.Length));
            }
            else
            {
                var chunkCount = processors * 3;
                var framesPerChunk = frames.Length / chunkCount;
                var remainderFrames = frames.Length % chunkCount;
                for ( var i = 0;i < chunkCount;i++ )
                {
                    var frameStart = i * framesPerChunk;
                    var frameEnd = ( ( i + 1 ) * framesPerChunk );
                    if ( i == chunkCount - 1 )
                    {
                        frameEnd += remainderFrames;
                    }
                    ranges.Add(new FrameRange(frameStart, frameEnd));
                }
            }


            var rng = new Random();
            var tasks = new List<Task<List<string>>>();
            for ( int i = 0;i < processors;i++ )
            {
                tasks.Add(Task.Run(() =>
                {
                    HashSet<string> framesFoundByTask = new HashSet<string>();

                    while ( true )
                    {
                        FrameRange range = null;
                        lock ( ranges )
                        {
                            if ( ranges.Count == 0 )
                            {
                                break;
                            }

                            var idx = rng.Next(ranges.Count);
                            range = ranges[ idx ];
                            ranges.RemoveAt(idx);
                        }

                        //Console.WriteLine( $"Started processing range: {range.StartIndex:000000} => {range.EndIndex:000000}" );

                        var removedFramesInThisChunk = RemovePanningImagesSimple(frameInfos, range.StartIndex, range.EndIndex, context);
                        foreach ( var frame in removedFramesInThisChunk )
                        {
                            framesFoundByTask.Add(frame);
                        }
                    }

                    return framesFoundByTask.ToList();
                }));
            }

            var lists = await Task.WhenAll(tasks);

            return lists.SelectMany(x => x).ToHashSet().Except(context.UnremovableImages.Keys).ToList();
        }


        static List<string> RemovePanningImagesSimple(FrameInfo[] frames, int startIndex, int endIndex, PanningRemovalContext context)
        {
            var maxPanningFramesToRemove = context.MaxPanningFramesToRemove;
            var imagesToRemove = new HashSet<string>();
            var lastProgress = startIndex - 1;


            MagickImageWithPath img1 = null;
            for ( int i = startIndex;i < endIndex;i++ )
            {
                try
                {
                    var frame = frames[ i ];
                    //if( context.UnremovableImages.ContainsKey( frame.Path ) )
                    //{
                    //   img1 = null;
                    //   continue;
                    //}

                    var priorDuplicates = frame.GetPriorDuplicateFrames();
                    if ( priorDuplicates == -1 )
                    {
                        // keep processing BACKWARDS until:
                        //  * We are no longer panning
                        //  * We find duplicate frame information in prior frame

                        int actualPriorDuplicates = 0;
                        int bi = i;
                        while ( bi > 0 )
                        {
                            var pbi = bi - 1;

                            var bframe = frames[ bi ];
                            var pbframe = frames[ pbi ];

                            var bImg = context.GetOrCreateImage(bframe.Path);
                            var pbImg = context.GetOrCreateImage(pbframe.Path);

                            if ( IsPanning(pbImg, bImg, context) )
                            {
                                //Console.WriteLine( $"GOING BACK: {Thread.CurrentThread.ManagedThreadId}, FRAME: {bi}" );

                                bi--;
                                actualPriorDuplicates++;
                            }
                            else
                            {
                                break;
                            }
                        }

                        frame.SetPriorDuplicateFrames(actualPriorDuplicates);
                        priorDuplicates = actualPriorDuplicates;
                    }

                    if ( img1 == null )
                    {
                        img1 = context.GetOrCreateImage(frame.Path);
                    }

                    var nextIndex = i + 1;
                    if ( nextIndex < frames.Length )
                    {
                        var nextFrame = frames[ nextIndex ];

                        var img2 = context.GetOrCreateImage(nextFrame.Path);

                        var isPanning = IsPanning(img1, img2, context);
                        var priorDuplicatesForNextFrame = isPanning
                           ? priorDuplicates + 1
                           : 0;

                        nextFrame.SetPriorDuplicateFrames(priorDuplicatesForNextFrame);

                        if ( i < endIndex )
                        {
                            if ( isPanning )
                            {
                                if ( priorDuplicatesForNextFrame <= maxPanningFramesToRemove )
                                {
                                    imagesToRemove.Add(nextFrame.Path);
                                }
                                else
                                {
                                    context.UnremovableImages.TryAdd(nextFrame.Path, true);
                                }
                            }
                        }

                        img1 = img2;
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
            if ( subImgDiff < threshold * 3 ) // TOVERIFY: Could be * 2 instead
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

                var diff = sImg1.Compare(sImg2, ErrorMetric.Fuzz) * 100;
                if ( diff < threshold * 3 && diff > threshold && ( xOffset != 0 || yOffset != 0 ) ) // TOVERIFY: Might cause more trouble?
                {
                    // Use NCC and/or SSIM
                    var ncc = sImg1.Compare(sImg2, ErrorMetric.NormalizedCrossCorrelation) * 100;
                    if ( ncc > 98.5 )
                    {
                        //var fileName = Path.GetFileName( img2.FileName );
                        //sImg2.Write( Path.Combine( "phash", fileName ) );
                        //Console.WriteLine( "MATCH BY NCC: " + ncc );

                        return new ShiftCompareResult(0.1, subImgDiff, xOffset != 0 || yOffset != 0);
                    }
                }

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
    }
}
