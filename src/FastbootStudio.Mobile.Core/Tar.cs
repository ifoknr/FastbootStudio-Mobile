using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FastbootStudio.Mobile.Core
{
    /// <summary>
    /// The little of tar the app needs to hand a backup over as one file: writing plain files
    /// (ustar), and reading an archive back member by member to hash what was really written.
    /// Everything is streamed; nothing is held in memory but one block.
    /// </summary>
    public static class Tar
    {
        const int Block = 512;

        /// <summary>
        /// The SHA-256 of every regular file in a tar stream, by its name inside the archive
        /// ("folder/boot_a.img"). Reads ustar and GNU archives (prefix, long names, pax headers).
        /// Throws <see cref="InvalidDataException"/> for a stream that is cut off or is not tar.
        /// </summary>
        public static Dictionary<string, string> Hashes(Stream archive)
        {
            Dictionary<string, string> hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            byte[] header = new byte[Block], buffer = new byte[1 << 16];
            string longName = null;
            while (true)
            {
                int got = Fill(archive, header, Block);
                if (got == 0 || (got == Block && IsZero(header)))
                    return hashes;          // the end: the stream ends, or the first all-zero block
                if (got != Block)
                    throw new InvalidDataException("tar: cut off in a header");
                if (!ChecksumOk(header))
                    throw new InvalidDataException("tar: not a tar header");
                long size = Octal(header, 124, 12);
                char type = (char)header[156];
                string name = Text(header, 0, 100);
                string prefix = Encoding.ASCII.GetString(header, 257, 5) == "ustar" ? Text(header, 345, 155) : "";
                if (prefix.Length > 0)
                    name = prefix + "/" + name;
                long padded = (size + Block - 1) / Block * Block;

                if (type == 'L')
                {
                    // GNU: the next member's name, too long for the header.
                    byte[] raw = new byte[size];
                    if (Fill(archive, raw, (int)size) != size)
                        throw new InvalidDataException("tar: cut off in a name");
                    Skip(archive, padded - size, buffer);
                    longName = Encoding.UTF8.GetString(raw).TrimEnd('\0');
                    continue;
                }
                if (type == '0' || type == '\0' || type == '7')
                {
                    using (SHA256 sha = SHA256.Create())
                    {
                        long left = size;
                        while (left > 0)
                        {
                            int n = Fill(archive, buffer, (int)Math.Min(buffer.Length, left));
                            if (n <= 0)
                                throw new InvalidDataException("tar: cut off in " + name);
                            sha.TransformBlock(buffer, 0, n, null, 0);
                            left -= n;
                        }
                        sha.TransformFinalBlock(buffer, 0, 0);
                        hashes[longName ?? name] = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
                    }
                    Skip(archive, padded - size, buffer);
                }
                else
                    // A folder, a link, a pax header: its data (if any) is passed over.
                    Skip(archive, padded, buffer);
                longName = null;
            }
        }

        /// <summary>Writes one file into a ustar stream. The name is at most 100 bytes, without a path above it.</summary>
        public static void WriteFile(Stream archive, string name, Stream data, long length, DateTime modified)
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(name ?? "");
            if (nameBytes.Length == 0 || nameBytes.Length > 100 || name.StartsWith("/", StringComparison.Ordinal) || name.Contains(".."))
                throw new ArgumentException("bad name for a tar member", nameof(name));
            byte[] header = new byte[Block];
            nameBytes.CopyTo(header, 0);
            Put(header, 100, "0000644\0");
            Put(header, 108, "0000000\0");
            Put(header, 116, "0000000\0");
            Put(header, 124, Convert.ToString(length, 8).PadLeft(11, '0') + "\0");
            long seconds = Math.Max(0, (long)(modified.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds);
            Put(header, 136, Convert.ToString(seconds, 8).PadLeft(11, '0') + "\0");
            Put(header, 148, "        ");
            header[156] = (byte)'0';
            Put(header, 257, "ustar\0" + "00");
            int sum = 0;
            foreach (byte b in header)
                sum += b;
            Put(header, 148, Convert.ToString(sum, 8).PadLeft(6, '0') + "\0 ");
            archive.Write(header, 0, Block);

            byte[] buffer = new byte[1 << 16];
            long left = length;
            while (left > 0)
            {
                int n = data.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                if (n <= 0)
                    throw new EndOfStreamException("the file ended early: " + name);
                archive.Write(buffer, 0, n);
                left -= n;
            }
            int pad = (int)((Block - length % Block) % Block);
            if (pad > 0)
                archive.Write(new byte[pad], 0, pad);
        }

        /// <summary>The two empty blocks that end an archive.</summary>
        public static void WriteEnd(Stream archive) => archive.Write(new byte[Block * 2], 0, Block * 2);

        /// <summary>
        /// The files of a backup whose SHA-256 inside a written archive is not the one expected
        /// (file name to SHA-256), or that are missing from it. Names in the archive may sit in a folder.
        /// </summary>
        public static List<string> Mismatches(IReadOnlyDictionary<string, string> inArchive, IReadOnlyDictionary<string, string> expected)
        {
            Dictionary<string, string> byFile = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> e in inArchive)
                byFile[e.Key.Substring(e.Key.LastIndexOf('/') + 1)] = e.Value;
            List<string> bad = new List<string>();
            foreach (KeyValuePair<string, string> e in expected)
                if (!byFile.TryGetValue(e.Key, out string got) || !string.Equals(got, e.Value, StringComparison.OrdinalIgnoreCase))
                    bad.Add(e.Key);
            return bad;
        }

        static void Put(byte[] header, int at, string text) => Encoding.ASCII.GetBytes(text).CopyTo(header, at);

        static int Fill(Stream s, byte[] buffer, int count)
        {
            int total = 0, n;
            while (total < count && (n = s.Read(buffer, total, count - total)) > 0)
                total += n;
            return total;
        }

        static void Skip(Stream s, long count, byte[] buffer)
        {
            while (count > 0)
            {
                int n = Fill(s, buffer, (int)Math.Min(buffer.Length, count));
                if (n <= 0)
                    throw new InvalidDataException("tar: cut off");
                count -= n;
            }
        }

        static bool IsZero(byte[] block)
        {
            foreach (byte b in block)
                if (b != 0)
                    return false;
            return true;
        }

        static string Text(byte[] header, int at, int length)
        {
            int end = at;
            while (end < at + length && header[end] != 0)
                end++;
            return Encoding.UTF8.GetString(header, at, end - at);
        }

        static long Octal(byte[] header, int at, int length)
        {
            // GNU writes sizes of 8 GiB and more in binary, flagged by the top bit.
            if ((header[at] & 0x80) != 0)
            {
                long big = 0;
                for (int i = at + length - 8; i < at + length; i++)
                    big = (big << 8) | header[i];
                return big;
            }
            string text = Encoding.ASCII.GetString(header, at, length).Trim('\0', ' ');
            try { return text.Length == 0 ? 0 : Convert.ToInt64(text, 8); }
            catch (Exception e) when (e is FormatException || e is OverflowException) { throw new InvalidDataException("tar: bad number"); }
        }

        static bool ChecksumOk(byte[] header)
        {
            long stored;
            try { stored = Octal(header, 148, 8); }
            catch (InvalidDataException) { return false; }
            long sum = 0;
            for (int i = 0; i < Block; i++)
                sum += i >= 148 && i < 156 ? 32 : header[i];
            return sum == stored;
        }
    }
}
