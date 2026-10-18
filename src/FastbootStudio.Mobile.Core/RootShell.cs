using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace FastbootStudio.Mobile.Core
{
    public sealed class ShellResult
    {
        public ShellResult(int exitCode, string output, string error)
        {
            ExitCode = exitCode;
            Output = output ?? "";
            Error = error ?? "";
        }

        public int ExitCode { get; }
        public string Output { get; }
        public string Error { get; }
        public bool Ok => ExitCode == 0;
    }

    /// <summary>Runs a script as root (su) on the phone. The Android app implements it; tests fake it.</summary>
    public interface IRootShell
    {
        ShellResult Run(string script);
    }

    public static class Sh
    {
        /// <summary>Single-quotes a value for sh: nothing inside is expanded.</summary>
        public static string Q(string value) => "'" + (value ?? "").Replace("'", "'\\''") + "'";

        static readonly Regex PartitionName = new Regex("^[a-z0-9_]{1,64}$");

        /// <summary>Partition names come from by-name and the allowed list; anything else is not a name.</summary>
        public static bool IsPartitionName(string name) => name != null && PartitionName.IsMatch(name);

        /// <summary>The 64-hex-digit hash at the start of sha256sum's output, or null.</summary>
        public static string Sha(string output)
        {
            Match m = Regex.Match(output ?? "", "^([0-9a-fA-F]{64})\\b");
            return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
        }

        /// <summary>Non-empty lines of a command's output.</summary>
        public static List<string> Lines(string output)
        {
            List<string> lines = new List<string>();
            foreach (string raw in (output ?? "").Replace("\r", "").Split('\n'))
                if (raw.Trim().Length > 0)
                    lines.Add(raw.Trim());
            return lines;
        }
    }
}
