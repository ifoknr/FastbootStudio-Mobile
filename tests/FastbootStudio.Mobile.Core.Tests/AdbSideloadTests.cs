using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Xunit;

namespace FastbootStudio.Mobile.Core.Tests
{
    /// <summary>
    /// A recovery in "Apply update from ADB" as adbd behaves: it answers CNXN (after AUTH when
    /// told to), opens sideload-host, asks for the blocks it was told to ask for, checks every
    /// byte it gets against the real file, and ends with DONEDONE or FAILFAIL.
    /// </summary>
    sealed class FakeRecovery : IAdbTransport
    {
        public byte[] Zip = new byte[0];
        public string Banner = "sideload::ro.product.name=sweet;ro.product.model=Redmi;ro.product.device=sweet;";
        public uint MaxData = 256 * 1024;
        public bool Auth, KnowsKey, HostMode = true, StreamMode = true, Fail;
        public RSA Trusted;
        /// <summary>Reads hand over at most this many bytes at a time (a USB stack that splits transfers).</summary>
        public int ReadAtMost = int.MaxValue;
        /// <summary>The blocks asked for, in this order; null asks for all of them in order.</summary>
        public List<long> Ask;
        /// <summary>After this many requests the cable comes out.</summary>
        public int UnplugAfter = int.MaxValue;
        public bool BadChecksum, RequestBeforeOkay;
        public Action<long> OnBlock;

        public readonly List<string> Opened = new List<string>();
        public readonly List<long> Served = new List<long>();
        public readonly MemoryStream Streamed = new MemoryStream();
        public bool KeyOffered, Closed, Corrupt;
        public string OfferedKey;

        readonly MemoryStream fromHost = new MemoryStream();
        readonly Queue<byte> toHost = new Queue<byte>();
        readonly byte[] token = Enumerable.Range(1, 20).Select(i => (byte)i).ToArray();
        int blockSize, next, got;
        bool hostStream, gone;
        long size, current = -1;
        uint hostId;
        const uint MyId = 7;

        public void Write(byte[] buffer, int offset, int count)
        {
            if (gone)
                throw new IOException("unplugged");
            fromHost.Write(buffer, offset, count);
            while (true)
            {
                byte[] all = fromHost.ToArray();
                if (all.Length < 24)
                    return;
                int length = (int)BitConverter.ToUInt32(all, 12);
                if (all.Length < 24 + length)
                    return;
                uint command = BitConverter.ToUInt32(all, 0), a0 = BitConverter.ToUInt32(all, 4), a1 = BitConverter.ToUInt32(all, 8);
                byte[] data = all.Skip(24).Take(length).ToArray();
                Assert.Equal(command ^ 0xffffffff, BitConverter.ToUInt32(all, 20));
                Assert.Equal((uint)data.Sum(b => (long)b), BitConverter.ToUInt32(all, 16));
                Assert.True(length <= MaxData, "the host sent more than the phone allows");
                fromHost.SetLength(0);
                fromHost.Write(all, 24 + length, all.Length - 24 - length);
                Handle(command, a0, a1, data);
            }
        }

        void Send(uint command, uint a0, uint a1, byte[] data = null)
        {
            data = data ?? new byte[0];
            uint sum = (uint)data.Sum(b => (long)b);
            foreach (byte b in BitConverter.GetBytes(command).Concat(BitConverter.GetBytes(a0)).Concat(BitConverter.GetBytes(a1))
                .Concat(BitConverter.GetBytes((uint)data.Length)).Concat(BitConverter.GetBytes(BadChecksum && data.Length > 0 ? sum + 1 : sum))
                .Concat(BitConverter.GetBytes(command ^ 0xffffffff)).Concat(data))
                toHost.Enqueue(b);
        }

        void Handle(uint command, uint a0, uint a1, byte[] data)
        {
            if (command == AdbSideload.Cnxn)
            {
                Assert.Equal("host::\0", Encoding.ASCII.GetString(data));
                if (Auth) Send(AdbSideload.Auth, 1, 0, token);
                else Send(AdbSideload.Cnxn, AdbSideload.Version, MaxData, Encoding.ASCII.GetBytes(Banner));
            }
            else if (command == AdbSideload.Auth && a0 == 2)
            {
                if (KnowsKey && Trusted.VerifyHash(token, data, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1))
                    Send(AdbSideload.Cnxn, AdbSideload.Version, MaxData, Encoding.ASCII.GetBytes(Banner));
                else
                    Send(AdbSideload.Auth, 1, 0, token);
            }
            else if (command == AdbSideload.Auth && a0 == 3)
            {
                KeyOffered = true;
                OfferedKey = Encoding.ASCII.GetString(data).TrimEnd('\0');
                Send(AdbSideload.Cnxn, AdbSideload.Version, MaxData, Encoding.ASCII.GetBytes(Banner));
            }
            else if (command == AdbSideload.Open)
            {
                string service = Encoding.ASCII.GetString(data).TrimEnd('\0');
                Opened.Add(service);
                hostId = a0;
                string[] parts = service.Split(':');
                if (parts[0] == "sideload-host" && HostMode)
                {
                    size = long.Parse(parts[1]);
                    blockSize = int.Parse(parts[2]);
                    hostStream = false;
                    Ask ??= Enumerable.Range(0, (int)((size + blockSize - 1) / blockSize)).Select(i => (long)i).ToList();
                    Send(AdbSideload.Okay, MyId, hostId);
                    Request();
                }
                else if (parts[0] == "sideload" && StreamMode)
                {
                    size = long.Parse(parts[1]);
                    hostStream = true;
                    Send(AdbSideload.Okay, MyId, hostId);
                }
                else
                    Send(AdbSideload.Clse, 0, hostId);
            }
            else if (command == AdbSideload.Wrte)
            {
                Assert.Equal(MyId, a1);
                if (hostStream)
                {
                    Streamed.Write(data, 0, data.Length);
                    Send(AdbSideload.Okay, MyId, hostId);
                    return;
                }
                int want = (int)Math.Min(blockSize, size - current * blockSize);
                if (!data.SequenceEqual(Zip.Skip((int)(current * blockSize) + got).Take(data.Length)))
                    Corrupt = true;
                got += data.Length;
                bool whole = got >= want;
                if (whole && RequestBeforeOkay)
                    Request();
                Send(AdbSideload.Okay, MyId, hostId);
                if (whole && !RequestBeforeOkay)
                    Request();
            }
            else if (command == AdbSideload.Clse)
                Closed = true;
        }

        void Request()
        {
            if (current >= 0)
            {
                Served.Add(current);
                OnBlock?.Invoke(current);
            }
            if (Served.Count >= UnplugAfter)
            {
                gone = true;
                toHost.Clear();
                return;
            }
            got = 0;
            if (next >= Ask.Count)
            {
                current = -1;
                Send(AdbSideload.Wrte, MyId, hostId, Encoding.ASCII.GetBytes(Fail ? "FAILFAIL" : "DONEDONE"));
                return;
            }
            current = Ask[next++];
            string text = current.ToString("D8");
            Send(AdbSideload.Wrte, MyId, hostId, Encoding.ASCII.GetBytes(text));
        }

        public int Read(byte[] buffer, int offset, int count, int timeoutMs)
        {
            if (gone || toHost.Count == 0)
                return 0;
            int n = Math.Min(Math.Min(count, ReadAtMost), toHost.Count);
            for (int i = 0; i < n; i++)
                buffer[offset + i] = toHost.Dequeue();
            return n;
        }
    }

    public class AdbSideloadTests
    {
        static byte[] Package(int length) => Enumerable.Range(0, length).Select(i => (byte)(i * 131 + i / 251)).ToArray();

        static AdbSideload Client(FakeRecovery phone, RSA key = null, int maxData = AdbSideload.MaxDataLimit) =>
            new AdbSideload(phone, key, "FastbootStudio", maxData) { ReplyTimeoutMs = 50, ApprovalTimeoutMs = 50, RequestTimeoutMs = 50 };

        [Fact]
        public void A_recovery_that_asks_no_questions_gets_every_block_it_wants()
        {
            byte[] zip = Package(3 * AdbSideload.BlockSize + 1234);
            FakeRecovery phone = new FakeRecovery { Zip = zip };
            AdbSideload adb = Client(phone);
            adb.Connect();
            Assert.Equal("sideload", adb.State);
            Assert.Equal(256 * 1024, adb.MaxData);
            long last = 0, total = 0;
            adb.Send(new MemoryStream(zip), zip.Length, (sent, size) => { Assert.True(sent >= last); last = sent; total = size; });
            Assert.Equal("host", adb.Mode);
            Assert.Equal(new[] { "sideload-host:" + zip.Length + ":65536" }, phone.Opened);
            Assert.Equal(new long[] { 0, 1, 2, 3 }, phone.Served);
            Assert.False(phone.Corrupt);
            Assert.True(phone.Closed);
            Assert.Equal(zip.Length, last);
            Assert.Equal(zip.Length, total);
        }

        [Fact]
        public void Blocks_are_served_in_whatever_order_and_as_often_as_they_are_asked_for()
        {
            byte[] zip = Package(5 * AdbSideload.BlockSize - 7);
            // A recovery reads the end of a zip first (its directory), then the rest, some of it twice.
            FakeRecovery phone = new FakeRecovery { Zip = zip, Ask = new List<long> { 4, 0, 4, 2, 1, 3, 0, 4 }, MaxData = 4096, ReadAtMost = 5, RequestBeforeOkay = true };
            AdbSideload adb = Client(phone);
            adb.Connect();
            // The phone allows 4096 a message: a block goes out in sixteen pieces, each acknowledged.
            Assert.Equal(4096, adb.MaxData);
            adb.Send(new MemoryStream(zip), zip.Length);
            Assert.Equal(new long[] { 4, 0, 4, 2, 1, 3, 0, 4 }, phone.Served);
            Assert.False(phone.Corrupt);
        }

        [Fact]
        public void A_recovery_that_asks_who_is_there_is_answered_with_the_apps_key()
        {
            byte[] zip = Package(1000);
            using RSA key = RSA.Create(2048);
            // It knows the key already: the signature is enough.
            FakeRecovery known = new FakeRecovery { Zip = zip, Auth = true, KnowsKey = true, Trusted = key };
            AdbSideload adb = Client(known, key);
            adb.Connect();
            Assert.False(known.KeyOffered);
            adb.Send(new MemoryStream(zip), zip.Length);
            Assert.False(known.Corrupt);

            // It does not: the public key is offered, and the person allows it on the phone.
            FakeRecovery stranger = new FakeRecovery { Zip = zip, Auth = true, Trusted = key };
            bool asked = false;
            Client(stranger, key).Connect(() => asked = true);
            Assert.True(asked);
            Assert.True(stranger.KeyOffered);
            Assert.EndsWith(" FastbootStudio", stranger.OfferedKey);

            // Without a key there is nothing to answer with.
            Assert.Equal("auth-required", Assert.Throws<AdbException>(() => Client(new FakeRecovery { Auth = true }).Connect()).Code);
        }

        [Fact]
        public void The_public_key_is_in_adbs_own_format()
        {
            using RSA key = RSA.Create(2048);
            string text = AdbSideload.PublicKey(key, "FastbootStudio");
            byte[] raw = Convert.FromBase64String(text.Split(' ')[0]);
            Assert.Equal(4 + 4 + 256 + 256 + 4, raw.Length);
            Assert.Equal(64u, BitConverter.ToUInt32(raw, 0));
            RSAParameters p = key.ExportParameters(false);
            var n = new System.Numerics.BigInteger(p.Modulus, isUnsigned: true, isBigEndian: true);
            Assert.Equal(n, new System.Numerics.BigInteger(raw.Skip(8).Take(256).ToArray(), isUnsigned: true));
            // n0inv * n[0] == -1 (mod 2^32), and rr == R^2 mod n: what the phone's verifier relies on.
            uint n0 = BitConverter.ToUInt32(raw, 8), n0inv = BitConverter.ToUInt32(raw, 4);
            Assert.Equal(uint.MaxValue, unchecked(n0 * n0inv));
            Assert.Equal(System.Numerics.BigInteger.ModPow(System.Numerics.BigInteger.One << 2048, 2, n), new System.Numerics.BigInteger(raw.Skip(264).Take(256).ToArray(), isUnsigned: true));
            Assert.Equal(65537u, BitConverter.ToUInt32(raw, 520));
        }

        [Fact]
        public void An_older_recovery_gets_the_file_as_one_stream()
        {
            byte[] zip = Package(2 * AdbSideload.BlockSize + 99);
            FakeRecovery phone = new FakeRecovery { Zip = zip, HostMode = false, MaxData = 4096 };
            AdbSideload adb = Client(phone);
            adb.Connect();
            adb.Send(new MemoryStream(zip), zip.Length);
            Assert.Equal("stream", adb.Mode);
            Assert.Equal(new[] { "sideload-host:" + zip.Length + ":65536", "sideload:" + zip.Length }, phone.Opened);
            Assert.Equal(zip, phone.Streamed.ToArray());
            Assert.True(phone.Closed);

            FakeRecovery neither = new FakeRecovery { Zip = zip, HostMode = false, StreamMode = false };
            AdbSideload refused = Client(neither);
            refused.Connect();
            Assert.Equal("sideload-refused", Assert.Throws<AdbException>(() => refused.Send(new MemoryStream(zip), zip.Length)).Code);
        }

        [Fact]
        public void A_refusal_a_silence_and_a_broken_message_are_errors_not_successes()
        {
            byte[] zip = Package(3 * AdbSideload.BlockSize);
            // FAILFAIL: the recovery did not accept the package.
            AdbSideload failed = Client(new FakeRecovery { Zip = zip, Fail = true });
            failed.Connect();
            Assert.Equal("sideload-failed", Assert.Throws<AdbException>(() => failed.Send(new MemoryStream(zip), zip.Length)).Code);

            // The cable comes out after the second block.
            FakeRecovery unplugged = new FakeRecovery { Zip = zip, UnplugAfter = 2 };
            AdbSideload cut = Client(unplugged);
            cut.Connect();
            Exception gone = Assert.ThrowsAny<Exception>(() => cut.Send(new MemoryStream(zip), zip.Length));
            Assert.True(gone is AdbException a && a.Code == "no-reply" || gone is IOException);
            Assert.Equal(2, unplugged.Served.Count);

            // Nothing there at all, a phone in normal adb rather than sideload, a damaged message.
            Assert.Equal("no-reply", Assert.Throws<AdbException>(() => Client(new FakeRecovery { ReadAtMost = 0 }).Connect()).Code);
            AdbSideload normal = Client(new FakeRecovery { Banner = "device::ro.product.name=sweet;" });
            normal.Connect();
            Assert.Equal("not-sideload", Assert.Throws<AdbException>(() => normal.Send(new MemoryStream(zip), zip.Length)).Code);
            Assert.Equal("bad-packet", Assert.Throws<AdbException>(() => Client(new FakeRecovery { BadChecksum = true }).Connect()).Code);
            Assert.Equal("bad-packet", Assert.Throws<AdbException>(() => new AdbSideload(new GarbageTransport()).Connect()).Code);
        }

        [Fact]
        public void A_cancelled_sideload_stops_and_tells_the_recovery()
        {
            byte[] zip = Package(6 * AdbSideload.BlockSize);
            using CancellationTokenSource stop = new CancellationTokenSource();
            FakeRecovery phone = new FakeRecovery { Zip = zip };
            phone.OnBlock = block => { if (block == 1) stop.Cancel(); };
            AdbSideload adb = Client(phone);
            adb.Connect();
            Assert.ThrowsAny<OperationCanceledException>(() => adb.Send(new MemoryStream(zip), zip.Length, null, stop.Token));
            Assert.True(phone.Served.Count < 6);
            Assert.True(phone.Closed);
        }

        sealed class GarbageTransport : IAdbTransport
        {
            public void Write(byte[] buffer, int offset, int count) { }
            public int Read(byte[] buffer, int offset, int count, int timeoutMs)
            {
                for (int i = 0; i < count; i++)
                    buffer[offset + i] = 0x41;
                return count;
            }
        }
    }
}
