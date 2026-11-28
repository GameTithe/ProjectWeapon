#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ProjectWeapon.QaMetrics
{
    public static class QaEventLog
    {
        /// <summary>Appends a batch of complete synthetic events to a local QA file.</summary>
        public static void Append(string path, IEnumerable<QaMetricEvent> events)
        {
            if (events == null)
                throw new ArgumentNullException(nameof(events));
            string location = Path.GetFullPath(path);
            var batch = new List<string>();
            foreach (QaMetricEvent entry in events)
            {
                if (entry == null)
                    throw new ArgumentException("A QA event is missing.", nameof(events));
                batch.Add(entry.Encode());
            }

            Directory.CreateDirectory(Path.GetDirectoryName(location)!);
            using var writer = new StreamWriter(location, append: true, new UTF8Encoding(false));
            writer.NewLine = "\n";
            foreach (string line in batch)
                writer.WriteLine(line);
        }

        /// <summary>Returns no report for an absent source. An existing empty file is a valid observation.</summary>
        public static QaReplayReport? Replay(string path, QaReplayWindow window)
        {
            if (window == null)
                throw new ArgumentNullException(nameof(window));
            string location = Path.GetFullPath(path);
            try
            {
                return QaMetricsReplay.Calculate(Read(location), window);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
        }

        private static IEnumerable<QaMetricEvent> Read(string path)
        {
            int number = 0;
            foreach (string line in File.ReadLines(path))
            {
                number++;
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                QaMetricEvent entry;
                try
                {
                    entry = QaMetricEvent.Decode(line);
                }
                catch (Exception error) when (error is ArgumentException or FormatException)
                {
                    throw new FormatException($"Invalid synthetic QA event at line {number}.");
                }
                yield return entry;
            }
        }
    }
}
