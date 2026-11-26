using System;
using System.IO;

namespace FastbootStudio.Mobile.Core
{
    /// <summary>
    /// The one folder (in the app's cache) whose files the app hands to another app through
    /// the share sheet. A name asked for from outside is only ever a plain file directly in
    /// that folder: no path, no "..", and no link that leads out of it.
    /// </summary>
    public static class ExportFolder
    {
        /// <summary>The full path of the file <paramref name="name"/> in <paramref name="folder"/>, or null when it is not a plain file right there.</summary>
        public static string Resolve(string folder, string name)
        {
            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(name) || name == "." || name == ".."
                || name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0 || name.IndexOf('\0') >= 0)
                return null;
            try
            {
                string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
                string path = Path.GetFullPath(Path.Combine(root, name));
                if (Path.GetDirectoryName(path) != root || !File.Exists(path))
                    return null;
                FileInfo info = new FileInfo(path);
                // A link is followed by whoever opens it: one that is there is refused, wherever it points.
                if (info.LinkTarget != null || (info.Attributes & FileAttributes.ReparsePoint) != 0 || (info.Attributes & FileAttributes.Directory) != 0)
                    return null;
                return path;
            }
            catch (Exception e) when (e is IOException || e is ArgumentException || e is NotSupportedException || e is UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>The type a receiving app is told: a tar for ".tar", plain text for ".txt", else bytes.</summary>
        public static string TypeOf(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            return n.EndsWith(".tar", StringComparison.Ordinal) ? "application/x-tar"
                : n.EndsWith(".txt", StringComparison.Ordinal) ? "text/plain" : "application/octet-stream";
        }

        /// <summary>Empties the folder (old exports), making it if it is not there. Returns it.</summary>
        public static string Clear(string folder)
        {
            try
            {
                if (Directory.Exists(folder))
                    foreach (string old in Directory.GetFileSystemEntries(folder))
                    {
                        try
                        {
                            if (Directory.Exists(old) && new DirectoryInfo(old).LinkTarget == null) Directory.Delete(old, true);
                            else File.Delete(old);
                        }
                        catch (IOException) { }
                    }
                Directory.CreateDirectory(folder);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            return folder;
        }
    }
}
