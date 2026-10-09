using System;
using System.Globalization;
using System.IO;

namespace DhcbTools.Shared.Logic.Batch
{
    /// <summary>Reserve a separate log and workspace for every invocation, including simultaneous runs.</summary>
    public static class RunArtifacts
    {
        public static string CreateLog(string directory, DateTime time)
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "run-" + time.ToString("HHmmss-fffffff", CultureInfo.InvariantCulture)
                + "-" + Guid.NewGuid().ToString("N") + ".jsonl");
            using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { }
            return path;
        }
    }
}
