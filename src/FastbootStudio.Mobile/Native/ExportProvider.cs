using System;
using Android.Content;
using Android.Database;
using Android.OS;
using Android.Provider;
using Android.Runtime;
using FastbootStudio.Mobile.Core;

namespace FastbootStudio.Mobile.Native
{
    /// <summary>
    /// What lets the share sheet hand one of the app's exports to another app: a content
    /// provider that serves the files in the app's cache "export" folder and nothing else.
    /// It is not exported; another app reaches a file only through the one-time read
    /// permission the share intent grants for that file's address. Read-only: there is no
    /// write, no insert, no delete from outside. The name in the address is a plain file
    /// name, resolved by <see cref="ExportFolder.Resolve"/> (no path, no "..", no link out).
    ///
    /// Declared by hand in AndroidManifest.xml under the name given to [Register].
    /// </summary>
    [Register("com.ifoknr.fastbootstudio.mobile.ExportProvider")]
    public sealed class ExportProvider : ContentProvider
    {
        public const string Authority = "com.ifoknr.fastbootstudio.mobile.files";

        /// <summary>The export folder in the app's cache.</summary>
        public static string Folder(Context context) => System.IO.Path.Combine(context.CacheDir.AbsolutePath, "export");

        /// <summary>The address of a file in the export folder, by its name.</summary>
        public static Android.Net.Uri UriFor(string name) =>
            new Android.Net.Uri.Builder().Scheme("content").Authority(Authority).AppendPath(name).Build();

        /// <summary>The file an address names, or null: one path segment, a plain file right in the export folder.</summary>
        string Resolve(Android.Net.Uri uri)
        {
            if (uri == null || uri.Authority != Authority || Context == null)
                return null;
            var segments = uri.PathSegments;
            if (segments == null || segments.Count != 1)
                return null;
            string path = ExportFolder.Resolve(Folder(Context), segments[0]);
            if (path == null)
                return null;
            try
            {
                // Asked of the system as well: the real place of the file is the folder's own.
                string real = new Java.IO.File(path).CanonicalPath, root = new Java.IO.File(Folder(Context)).CanonicalPath;
                return real == root + "/" + segments[0] ? path : null;
            }
            catch (Java.IO.IOException)
            {
                return null;
            }
        }

        public override bool OnCreate() => true;

        public override string GetType(Android.Net.Uri uri)
        {
            string path = Resolve(uri);
            return path == null ? null : ExportFolder.TypeOf(path);
        }

        public override ICursor Query(Android.Net.Uri uri, string[] projection, string selection, string[] selectionArgs, string sortOrder)
        {
            string path = Resolve(uri);
            if (path == null)
                return null;
            // The name and the size, which is all a receiving app asks to show the file.
            string[] columns = projection ?? new[] { IOpenableColumns.DisplayName, IOpenableColumns.Size };
            MatrixCursor cursor = new MatrixCursor(columns, 1);
            Java.Lang.Object[] row = new Java.Lang.Object[columns.Length];
            for (int i = 0; i < columns.Length; i++)
                row[i] = columns[i] == IOpenableColumns.DisplayName ? new Java.Lang.String(System.IO.Path.GetFileName(path))
                    : columns[i] == IOpenableColumns.Size ? Java.Lang.Long.ValueOf(new System.IO.FileInfo(path).Length) : null;
            cursor.AddRow(row);
            return cursor;
        }

        public override ParcelFileDescriptor OpenFile(Android.Net.Uri uri, string mode)
        {
            string path = Resolve(uri);
            if (path == null)
                throw new Java.IO.FileNotFoundException("no such export");
            if (mode != "r")
                throw new Java.Lang.SecurityException("exports are read-only");
            return ParcelFileDescriptor.Open(new Java.IO.File(path), ParcelFileMode.ReadOnly);
        }

        public override Android.Net.Uri Insert(Android.Net.Uri uri, ContentValues values) => throw new Java.Lang.UnsupportedOperationException("read-only");

        public override int Update(Android.Net.Uri uri, ContentValues values, string selection, string[] selectionArgs) => throw new Java.Lang.UnsupportedOperationException("read-only");

        public override int Delete(Android.Net.Uri uri, string selection, string[] selectionArgs) => throw new Java.Lang.UnsupportedOperationException("read-only");
    }
}
