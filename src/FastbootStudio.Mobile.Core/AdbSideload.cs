using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace FastbootStudio.Mobile.Core
{
    /// <summary>One connection to one phone's adb interface: bulk out and bulk in, or anything that behaves like it.</summary>
    public interface IAdbTransport
    {
        /// <summary>Writes all of <paramref name="count"/> bytes or throws.</summary>
        void Write(byte[] buffer, int offset, int count);

        /// <summary>Reads up to <paramref name="count"/> bytes; 0 or less when nothing came in time or the link is gone.</summary>
        int Read(byte[] buffer, int offset, int count, int timeoutMs);
    }

    /// <summary>An adb failure by its code: "no-reply", "bad-packet", "auth-required", "not-sideload", "sideload-refused", "sideload-failed", "bad-request".</summary>
    public sealed class AdbException : Exception
    {
        public AdbException(string code, string detail = null) : base(detail == null ? code : code + ": " + detail) { Code = code; }
        public string Code { get; }
    }

    /// <summary>
    /// "adb sideload" from the phone itself: the part of the adb protocol a recovery in
    /// "Apply update from ADB" speaks (system/core/adb/protocol.txt and the sideload service).
    /// A message is a 24-byte header (command, two arguments, data length, data checksum,
    /// command ^ 0xffffffff, all little-endian) and its data. The host says CNXN, answers AUTH
    /// when asked, opens "sideload-host:size:blocksize" and then serves whatever block of the
    /// zip the recovery asks for (eight digits) until it says DONEDONE or FAILFAIL; a recovery
    /// too old for that gets the whole file as one "sideload:size" stream.
    ///
    /// The recovery verifies the package itself (its signature, the device it is for): this
    /// class only carries the bytes it is asked for, and reports honestly how it ended.
    /// </summary>
    public sealed class AdbSideload
    {
        public const uint Cnxn = 0x4e584e43, Auth = 0x48545541, Open = 0x4e45504f, Okay = 0x59414b4f, Clse = 0x45534c43, Wrte = 0x45545257;
        public const uint Version = 0x01000001;
        public const int BlockSize = 64 * 1024;

        /// <summary>The largest data a message may carry here; a phone that allows less gets less.</summary>
        public const int MaxDataLimit = 256 * 1024;

        const int HeaderSize = 24;
        const uint AuthToken = 1, AuthSignature = 2, AuthPublicKey = 3;

        readonly IAdbTransport transport;
        readonly RSA key;
        readonly string keyName;
        readonly int ourMaxData;

        /// <summary>How long to wait for a reply to a command, for the person to allow the key on the phone, and for the recovery's next request (it can be busy installing for minutes).</summary>
        public int ReplyTimeoutMs = 10000, ApprovalTimeoutMs = 60000, RequestTimeoutMs = 15 * 60 * 1000;

        /// <param name="key">The app's own adb key, for a recovery that asks who is connecting; null when there is none (most recoveries do not ask).</param>
        /// <param name="maxData">The largest message this side reads in one piece (a USB stack may allow less than <see cref="MaxDataLimit"/>).</param>
        public AdbSideload(IAdbTransport transport, RSA key = null, string keyName = "FastbootStudio", int maxData = MaxDataLimit)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            this.key = key;
            this.keyName = keyName ?? "FastbootStudio";
            ourMaxData = Math.Max(4096, Math.Min(maxData, MaxDataLimit));
        }

        /// <summary>What the phone said it is: "sideload::ro.product.name=...;" after <see cref="Connect"/>.</summary>
        public string Banner { get; private set; }

        /// <summary>"sideload", "recovery", "device"...: the first word of the banner.</summary>
        public string State => (Banner ?? "").Split(':')[0];

        /// <summary>The largest data either side accepts in one message.</summary>
        public int MaxData { get; private set; } = 4096;

        /// <summary>"host" (the recovery asked for blocks) or "stream" (the older way), after <see cref="Send"/>.</summary>
        public string Mode { get; private set; }

        struct Message
        {
            public uint Command, Arg0, Arg1;
            public byte[] Data;
        }

        // ---------------------------------------------------------------- the handshake

        /// <summary>
        /// CNXN, with AUTH when the phone asks: first a signature with the app's key, and if the
        /// phone does not know the key, the public key, which the person allows on the phone.
        /// </summary>
        /// <param name="waitingForApproval">Called when the phone is showing its "allow this computer?" question.</param>
        public void Connect(Action waitingForApproval = null)
        {
            Write(Cnxn, Version, (uint)ourMaxData, Encoding.ASCII.GetBytes("host::\0"));
            bool signed = false, offered = false;
            int timeout = ReplyTimeoutMs;
            for (int n = 0; n < 16; n++)
            {
                Message m = Read(timeout);
                if (m.Command == Cnxn)
                {
                    MaxData = (int)Math.Max(4096, Math.Min((uint)ourMaxData, m.Arg1));
                    Banner = Encoding.UTF8.GetString(m.Data).TrimEnd('\0');
                    return;
                }
                if (m.Command != Auth || m.Arg0 != AuthToken)
                    continue;       // nothing else belongs here: left unread traffic from before
                if (key == null)
                    throw new AdbException("auth-required");
                if (!signed)
                {
                    // The token stands for a SHA-1 digest: signed as one, the way adb does.
                    signed = true;
                    if (m.Data.Length == 20)
                    {
                        Write(Auth, AuthSignature, 0, key.SignHash(m.Data, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1));
                        continue;
                    }
                }
                if (!offered)
                {
                    offered = true;
                    Write(Auth, AuthPublicKey, 0, Encoding.ASCII.GetBytes(PublicKey(key, keyName) + "\0"));
                    waitingForApproval?.Invoke();
                    timeout = ApprovalTimeoutMs;
                }
            }
            throw new AdbException("auth-required");
        }

        /// <summary>
        /// The public key as adb writes it (adbkey.pub): Android's RSAPublicKey structure for a
        /// 2048-bit key (length in words, -1/n[0] mod 2^32, n, R^2 mod n, e; little-endian), in
        /// base64, then a name.
        /// </summary>
        public static string PublicKey(RSA key, string name)
        {
            RSAParameters p = key.ExportParameters(false);
            if (p.Modulus.Length != 256)
                throw new ArgumentException("adb keys are RSA 2048", nameof(key));
            BigInteger n = new BigInteger(p.Modulus, isUnsigned: true, isBigEndian: true);
            BigInteger rr = BigInteger.ModPow(BigInteger.One << 2048, 2, n);
            uint n0 = (uint)(n & uint.MaxValue), inverse = 1;
            for (int i = 0; i < 6; i++)
                inverse = unchecked(inverse * (2 - n0 * inverse));     // 1/n[0] mod 2^32
            uint e = 0;
            foreach (byte b in p.Exponent)
                e = (e << 8) | b;
            using (MemoryStream s = new MemoryStream())
            {
                void Word(uint v) => s.Write(BitConverter.GetBytes(v), 0, 4);
                void Number(BigInteger v)
                {
                    byte[] little = v.ToByteArray(isUnsigned: true, isBigEndian: false);
                    s.Write(little, 0, Math.Min(256, little.Length));
                    for (int i = little.Length; i < 256; i++)
                        s.WriteByte(0);
                }
                Word(64);
                Word(unchecked((uint)-(int)inverse));
                Number(n);
                Number(rr);
                Word(e);
                return Convert.ToBase64String(s.ToArray()) + " " + name;
            }
        }

        // ---------------------------------------------------------------- sideload

        /// <summary>
        /// Hands the zip to the recovery. Returns when it said DONEDONE (or, the older way, when
        /// the whole file went out); throws <see cref="AdbException"/> when it said FAILFAIL, closed
        /// the stream or fell silent, and <see cref="OperationCanceledException"/> when cancelled.
        /// </summary>
        /// <param name="zip">Seekable: the recovery asks for blocks in its own order, some of them twice.</param>
        /// <param name="progress">The furthest byte handed over so far, and the size.</param>
        public void Send(Stream zip, long size, Action<long, long> progress = null, CancellationToken cancel = default)
        {
            if (Banner == null)
                throw new InvalidOperationException("Connect comes first");
            if (State != "sideload")
                throw new AdbException("not-sideload", State);
            if (size <= 0 || !zip.CanSeek)
                throw new ArgumentOutOfRangeException(nameof(size));

            const uint local = 1;
            uint? remote = OpenStream(local, "sideload-host:" + size + ":" + BlockSize);
            if (remote != null)
            {
                Mode = "host";
                ServeBlocks(zip, size, local, remote.Value, progress, cancel);
                return;
            }
            remote = OpenStream(local + 1, "sideload:" + size);
            if (remote == null)
                throw new AdbException("sideload-refused");
            Mode = "stream";
            StreamAll(zip, size, local + 1, remote.Value, progress, cancel);
        }

        /// <summary>OPEN, answered by OKAY (the stream's number on the phone) or CLSE (null: it does not have that service).</summary>
        uint? OpenStream(uint local, string service)
        {
            Write(Open, local, 0, Encoding.ASCII.GetBytes(service + "\0"));
            for (int n = 0; n < 16; n++)
            {
                Message m = Read(ReplyTimeoutMs);
                if (m.Arg1 != local)
                    continue;
                if (m.Command == Okay)
                    return m.Arg0;
                if (m.Command == Clse)
                    return null;
            }
            throw new AdbException("bad-packet", "no answer to OPEN");
        }

        void ServeBlocks(Stream zip, long size, uint local, uint remote, Action<long, long> progress, CancellationToken cancel)
        {
            List<byte> asked = new List<byte>();
            byte[] block = new byte[BlockSize];
            long furthest = 0;
            while (true)
            {
                Cancelled(cancel, local, remote);
                // A request is eight characters; they arrive as the data of one or more WRTE.
                while (asked.Count < 8)
                {
                    Message m = Read(RequestTimeoutMs);
                    if (m.Arg1 != local)
                        continue;
                    if (m.Command == Clse)
                        throw new AdbException("sideload-failed", "the recovery closed the connection");
                    if (m.Command != Wrte)
                        continue;
                    asked.AddRange(m.Data);
                    Write(Okay, local, remote, null);
                    Cancelled(cancel, local, remote);
                }
                string request = Encoding.ASCII.GetString(asked.GetRange(0, 8).ToArray());
                asked.RemoveRange(0, 8);
                if (request == "DONEDONE")
                {
                    progress?.Invoke(size, size);
                    Write(Clse, local, remote, null);
                    return;
                }
                if (request == "FAILFAIL")
                {
                    Write(Clse, local, remote, null);
                    throw new AdbException("sideload-failed", "the recovery refused the package");
                }
                long number;
                if (!long.TryParse(request, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out number))
                    throw new AdbException("bad-request", request);
                long offset = number * BlockSize;
                if (offset >= size)
                    throw new AdbException("bad-request", "block " + number + " is past the end");
                int length = (int)Math.Min(BlockSize, size - offset);
                zip.Seek(offset, SeekOrigin.Begin);
                for (int got = 0, n; got < length; got += n)
                    if ((n = zip.Read(block, got, length - got)) <= 0)
                        throw new AdbException("bad-request", "the file ended early");
                SendData(block, length, local, remote, asked, cancel);
                furthest = Math.Max(furthest, offset + length);
                progress?.Invoke(furthest, size);
            }
        }

        void StreamAll(Stream zip, long size, uint local, uint remote, Action<long, long> progress, CancellationToken cancel)
        {
            byte[] buffer = new byte[BlockSize];
            zip.Seek(0, SeekOrigin.Begin);
            long sent = 0;
            while (sent < size)
            {
                Cancelled(cancel, local, remote);
                int want = (int)Math.Min(buffer.Length, size - sent), got = 0, n;
                while (got < want && (n = zip.Read(buffer, got, want - got)) > 0)
                    got += n;
                if (got != want)
                    throw new AdbException("bad-request", "the file ended early");
                SendData(buffer, got, local, remote, null, cancel);
                sent += got;
                progress?.Invoke(sent, size);
            }
            Write(Clse, local, remote, null);
        }

        /// <summary>WRTE in pieces the phone accepts, each one acknowledged (OKAY) before the next goes out.</summary>
        void SendData(byte[] data, int length, uint local, uint remote, List<byte> inbox, CancellationToken cancel)
        {
            for (int at = 0; at < length;)
            {
                int n = Math.Min(MaxData, length - at);
                byte[] piece = new byte[n];
                Buffer.BlockCopy(data, at, piece, 0, n);
                Write(Wrte, local, remote, piece);
                at += n;
                while (true)
                {
                    Message m = Read(ReplyTimeoutMs * 6);
                    if (m.Arg1 != local)
                        continue;
                    if (m.Command == Okay)
                        break;
                    if (m.Command == Clse)
                        throw new AdbException("sideload-failed", "the recovery closed the connection");
                    if (m.Command == Wrte)
                    {
                        // The next request can arrive before the acknowledgement: kept, and acknowledged.
                        inbox?.AddRange(m.Data);
                        Write(Okay, local, remote, null);
                    }
                }
                Cancelled(cancel, local, remote);
            }
        }

        void Cancelled(CancellationToken cancel, uint local, uint remote)
        {
            if (!cancel.IsCancellationRequested)
                return;
            // Told, so the recovery stops waiting; whether it hears is not waited for.
            try { Write(Clse, local, remote, null); } catch (Exception) { }
            cancel.ThrowIfCancellationRequested();
        }

        // ---------------------------------------------------------------- messages

        static uint Sum(byte[] data)
        {
            uint sum = 0;
            foreach (byte b in data)
                sum += b;
            return sum;
        }

        void Write(uint command, uint arg0, uint arg1, byte[] data)
        {
            data = data ?? Array.Empty<byte>();
            byte[] header = new byte[HeaderSize];
            BitConverter.GetBytes(command).CopyTo(header, 0);
            BitConverter.GetBytes(arg0).CopyTo(header, 4);
            BitConverter.GetBytes(arg1).CopyTo(header, 8);
            BitConverter.GetBytes((uint)data.Length).CopyTo(header, 12);
            // Always sent: an older adbd insists on it, a newer one ignores it.
            BitConverter.GetBytes(Sum(data)).CopyTo(header, 16);
            BitConverter.GetBytes(command ^ 0xffffffff).CopyTo(header, 20);
            transport.Write(header, 0, HeaderSize);
            if (data.Length > 0)
                transport.Write(data, 0, data.Length);
        }

        Message Read(int timeoutMs)
        {
            byte[] header = Exactly(HeaderSize, timeoutMs);
            Message m = new Message
            {
                Command = BitConverter.ToUInt32(header, 0), Arg0 = BitConverter.ToUInt32(header, 4), Arg1 = BitConverter.ToUInt32(header, 8),
            };
            uint length = BitConverter.ToUInt32(header, 12), sum = BitConverter.ToUInt32(header, 16), magic = BitConverter.ToUInt32(header, 20);
            if (magic != (m.Command ^ 0xffffffff) || length > MaxDataLimit)
                throw new AdbException("bad-packet", "not an adb message");
            m.Data = length == 0 ? Array.Empty<byte>() : Exactly((int)length, ReplyTimeoutMs);
            // A newer adbd sends no checksum (0); one that is sent has to be right.
            if (sum != 0 && sum != Sum(m.Data))
                throw new AdbException("bad-packet", "checksum");
            return m;
        }

        /// <summary>A USB read hands over what one transfer brought, which may be less than asked for: read until all is in.</summary>
        byte[] Exactly(int count, int timeoutMs)
        {
            byte[] buffer = new byte[count];
            for (int got = 0; got < count;)
            {
                int n = transport.Read(buffer, got, count - got, timeoutMs);
                if (n <= 0)
                    throw new AdbException("no-reply");
                got += n;
            }
            return buffer;
        }
    }
}
