using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using FastbootStudio.Mobile.Core;

namespace FastbootStudio.Mobile.Native
{
    /// <summary>Runs scripts as root through su: the script goes in on stdin, so nothing is re-quoted by su.</summary>
    public sealed class SuShell : IRootShell
    {
        public static readonly SuShell Instance = new SuShell();

        public ShellResult Run(string script)
        {
            Log.Add("$ " + (script.Length > 300 ? script.Substring(0, 300) + "…" : script));
            try
            {
                using (Process p = Start())
                {
                    p.StandardInput.Write(script + "\nexit $?\n");
                    p.StandardInput.Close();
                    Task<string> error = p.StandardError.ReadToEndAsync();
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    ShellResult r = new ShellResult(p.ExitCode, output, error.Result);
                    if (!r.Ok)
                        Log.Add("  exit " + r.ExitCode + (r.Error.Length > 0 ? ": " + r.Error.Trim() : ""));
                    return r;
                }
            }
            catch (Exception e)
            {
                Log.Add("  su failed: " + e.Message);
                return new ShellResult(-1, "", e.Message);
            }
        }

        /// <summary>Runs a long job, handing each line of its output to <paramref name="line"/> as it comes.</summary>
        public ShellResult RunLines(string script, Action<string> line)
        {
            Log.Add("$ " + script);
            try
            {
                using (Process p = Start())
                {
                    p.StandardInput.Write(script + "\nexit $?\n");
                    p.StandardInput.Close();
                    Task<string> error = p.StandardError.ReadToEndAsync();
                    System.Text.StringBuilder all = new System.Text.StringBuilder();
                    string l;
                    while ((l = p.StandardOutput.ReadLine()) != null)
                    {
                        all.Append(l).Append('\n');
                        line?.Invoke(l);
                    }
                    p.WaitForExit();
                    ShellResult r = new ShellResult(p.ExitCode, all.ToString(), error.Result);
                    if (!r.Ok)
                        Log.Add("  exit " + r.ExitCode + (r.Error.Length > 0 ? ": " + r.Error.Trim() : ""));
                    return r;
                }
            }
            catch (Exception e)
            {
                Log.Add("  su failed: " + e.Message);
                return new ShellResult(-1, "", e.Message);
            }
        }

        /// <summary>
        /// Copies a file root can read into a file the app creates itself (so it carries the
        /// app's own SELinux label and owner), by streaming cat's output.
        /// </summary>
        public bool CopyOut(string remotePath, string localPath)
        {
            Log.Add("$ cat " + remotePath + " > " + localPath);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(localPath));
                using (Process p = Start())
                using (FileStream file = File.Create(localPath))
                {
                    p.StandardInput.Write("cat " + Sh.Q(remotePath) + "\nexit $?\n");
                    p.StandardInput.Close();
                    Task<string> error = p.StandardError.ReadToEndAsync();
                    p.StandardOutput.BaseStream.CopyTo(file);
                    p.WaitForExit();
                    if (p.ExitCode != 0)
                        Log.Add("  exit " + p.ExitCode + ": " + error.Result.Trim());
                    return p.ExitCode == 0;
                }
            }
            catch (Exception e)
            {
                Log.Add("  copy failed: " + e.Message);
                return false;
            }
        }

        /// <summary>True when su gives uid 0.</summary>
        public bool Available()
        {
            ShellResult r = Run("id -u");
            return r.Ok && r.Output.Trim() == "0";
        }

        static Process Start()
        {
            // Through sh, so su is found the way root managers expect (KernelSU answers the exec, not a file).
            ProcessStartInfo info = new ProcessStartInfo("/system/bin/sh", new[] { "-c", "exec su" })
            {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            };
            return Process.Start(info);
        }
    }

    /// <summary>The app's log: every command and result, shown on the Logs page.</summary>
    public static class Log
    {
        static readonly System.Collections.Generic.List<string> lines = new System.Collections.Generic.List<string>();

        public static void Add(string line)
        {
            lock (lines)
            {
                lines.Add(DateTime.Now.ToString("HH:mm:ss") + " " + line);
                if (lines.Count > 2000)
                    lines.RemoveRange(0, 500);
            }
        }

        public static string[] All()
        {
            lock (lines)
                return lines.ToArray();
        }
    }
}
