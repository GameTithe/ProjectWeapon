#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ProjectWeapon.VfxRarityLab
{
    [RequireComponent(typeof(VfxRarityLab))]
    public sealed class VfxRarityBenchmark : MonoBehaviour
    {
        private const double MeasureSeconds = 4;
        private static readonly WaitForEndOfFrame EndOfFrame = new WaitForEndOfFrame();

        private IEnumerator Start()
        {
            string[] command = Environment.GetCommandLineArgs();
            int option = Array.IndexOf(command, "-kan31-report");
            if (option < 0 || option + 1 >= command.Length)
                yield break;

            string destination = command[option + 1];
            Directory.CreateDirectory(destination);
            bool oldRunInBackground = Application.runInBackground;
            int oldVSync = QualitySettings.vSyncCount;
            int oldCap = Application.targetFrameRate;
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;

            var lab = GetComponent<VfxRarityLab>();
            lab.BeginMeasurement();
            lab.Pose(2);
            lab.Show(-1, false);
            yield return EndOfFrame;
            yield return EndOfFrame;
            Color32[] warmupBaseline = Capture(destination, null);
            lab.Show(2, false);
            yield return EndOfFrame;
            if (CountChanged(warmupBaseline, Capture(destination, null)) == 0)
            {
                Debug.LogError("The desktop window did not render the effects. Run the player with a visible window.");
                Application.runInBackground = oldRunInBackground;
                QualitySettings.vSyncCount = oldVSync;
                Application.targetFrameRate = oldCap;
                Application.Quit(2);
                yield break;
            }

            var timings = new List<string>();
            for (int mode = 0; mode < 3; mode++)
                yield return MeasureFrames(lab, mode, timings);

            lab.Pose(2);
            lab.Show(-1, false);
            yield return EndOfFrame;
            yield return EndOfFrame;
            Color32[] baseline = Capture(destination, "baseline");
            var occupancy = new List<string>();
            for (int mode = 0; mode < 2; mode++)
            {
                bool reduced = mode == 1;
                for (int grade = -1; grade < VfxRarityLab.Grades; grade++)
                {
                    int peak = 0;
                    for (int frame = 0; frame < VfxRarityLab.Frames; frame++)
                    {
                        lab.Show(frame, reduced, grade);
                        yield return EndOfFrame;
                        bool allVisible = grade == -1 && frame == 2;
                        Color32[] sample = Capture(destination, allVisible ? reduced ? "reduced" : "full" : null);
                        peak = Math.Max(peak, CountChanged(baseline, sample));
                    }
                    occupancy.Add(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3:F4}",
                        reduced ? "reduced" : "full", grade + 1, peak, 100d * peak / baseline.Length));
                }
            }

            int renderers = GetComponentsInChildren<SpriteRenderer>().Length;
            for (int pulse = 0; pulse < 10; pulse++)
            {
                lab.Show(2, false);
                if (lab.ActiveEffects != 5)
                    throw new InvalidOperationException("The full measurement needs all five active effects.");
                lab.Show(-1, false);
            }
            if (renderers != GetComponentsInChildren<SpriteRenderer>().Length || lab.ActiveEffects != 0)
                throw new InvalidOperationException("Replay must only reuse its preallocated renderers.");

            string[] header =
            {
                "unity,width,height,graphics_api,gpu,cpu",
                string.Join(",", Application.unityVersion, Screen.width, Screen.height, SystemInfo.graphicsDeviceType,
                    SystemInfo.graphicsDeviceName, SystemInfo.processorType),
                "mode,frames,seconds,p50_ms,p95_ms,p99_ms"
            };
            var report = new StringBuilder(string.Join("\n", header));
            report.Append('\n').AppendJoin('\n', timings);
            report.Append("\nmode,grade_zero_means_all,peak_changed_pixels,viewport_percent\n").AppendJoin('\n', occupancy);
            report.Append("\nrenderer_count_after_ten_replays,").Append(renderers).Append('\n');
            File.WriteAllText(Path.Combine(destination, "measurements.csv"), report.ToString());
            Debug.Log($"KAN-31 captured {Screen.width}x{Screen.height}, {timings.Count} frame samples and {occupancy.Count} occupancy rows.");
            Application.runInBackground = oldRunInBackground;
            QualitySettings.vSyncCount = oldVSync;
            Application.targetFrameRate = oldCap;
            Application.Quit();
        }

        private static IEnumerator MeasureFrames(VfxRarityLab lab, int mode, List<string> report)
        {
            double startedAt = Time.realtimeSinceStartupAsDouble;
            while (Time.realtimeSinceStartupAsDouble - startedAt < 1.5)
            {
                ShowPulse(lab, mode, Time.realtimeSinceStartupAsDouble - startedAt);
                yield return null;
            }

            var frames = new List<float>(32768);
            startedAt = Time.realtimeSinceStartupAsDouble;
            double elapsed = 0d;
            while (elapsed < MeasureSeconds)
            {
                ShowPulse(lab, mode, elapsed);
                yield return null;
                elapsed = Time.realtimeSinceStartupAsDouble - startedAt;
                frames.Add(Time.unscaledDeltaTime * 1000f);
            }
            float[] sorted = frames.ToArray();
            Array.Sort(sorted);
            string name = mode == 0 ? "baseline" : mode == 1 ? "full" : "reduced";
            report.Add(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2:F3},{3:F3},{4:F3},{5:F3}",
                name, sorted.Length, elapsed, Percentile(sorted, .5), Percentile(sorted, .95), Percentile(sorted, .99)));
        }

        private static void ShowPulse(VfxRarityLab lab, int mode, double elapsed)
        {
            int frame = (int)(elapsed % .65 / VfxRarityLab.FrameSeconds);
            bool visible = frame < VfxRarityLab.Frames;
            lab.Pose(visible ? frame : 0);
            lab.Show(visible && mode != 0 ? frame : -1, lowerOverdraw: mode == 2);
        }

        private static float Percentile(float[] sorted, double percentile) => sorted[(int)Math.Ceiling(sorted.Length * percentile) - 1];

        private static Color32[] Capture(string destination, string? name)
        {
            Texture2D texture = ScreenCapture.CaptureScreenshotAsTexture();
            Color32[] pixels = texture.GetPixels32();
            if (name != null)
                File.WriteAllBytes(Path.Combine(destination, name + ".png"), texture.EncodeToPNG());
            Destroy(texture);
            return pixels;
        }

        private static int CountChanged(Color32[] baseline, Color32[] sample)
        {
            if (baseline.Length != sample.Length)
                throw new InvalidOperationException("The viewport changed during the coverage measurement.");
            int changed = 0;
            for (int i = 0; i < baseline.Length; i++)
            {
                Color32 before = baseline[i];
                Color32 after = sample[i];
                if (Math.Abs(before.r - after.r) >= 4 || Math.Abs(before.g - after.g) >= 4 || Math.Abs(before.b - after.b) >= 4)
                    changed++;
            }
            return changed;
        }
    }
}
