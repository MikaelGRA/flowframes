using Flowframes.IO;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Flowframes.Magick.Panning
{
    internal static class Depanner
    {
        public static async Task Run(string path, bool setStatus = true)
        {
            if ( path == null || !Directory.Exists(path) || Interpolate.canceled )
                return;

            if ( setStatus )
                Program.mainForm.SetStatus("Running depanning frame removal");

            Logger.Log("Running depanning frame removal...");

            await RemoveDupeFrames(path, "*");
        }

        public static async Task RemoveDupeFrames(string path, string ext)
        {
            var threshold = Config.GetFloat(Config.Key.depanningThresh);
            var maxPanningFrames = Config.GetInt(Config.Key.depanningMaxConsecutive);
            var allowVerHor = Config.GetBool(Config.Key.depanningVerHor);

            Stopwatch sw = new Stopwatch();
            sw.Restart();
            Logger.Log("Removing panning frames - Threshold: " + threshold.ToString("0.00"));

            var files = IoUtils.GetFileInfosSorted(path, false, "*." + ext).Select(x => x.FullName).ToList();
            var context = new PanningRemovalContext(files.ToArray(), maxPanningFrames, threshold, 100, allowVerHor);

            context.Progress += pct =>
            {
                Logger.Log($"Panning removal: {pct}%", false, true);
                Program.mainForm.SetProgress(pct);
            };

            var framesToDelete = await PanningFrameLocator.RemovePanningImages(context);


            foreach ( string frame in framesToDelete )
                IoUtils.TryDeleteIfExists(frame);


            if ( Interpolate.canceled ) return;

            Logger.Log($"[Panning removal] Done. Deleted {framesToDelete.Count} frames.", false, true);
        }
    }
}
