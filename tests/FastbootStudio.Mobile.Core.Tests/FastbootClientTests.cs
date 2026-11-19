using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace FastbootStudio.Mobile.Core.Tests
{
    /// <summary>A bootloader that answers like a real one and records what it was sent.</summary>
    sealed class FakeBootloader : IFastbootTransport
    {
        public readonly List<string> Commands = new List<string>();
        public readonly Dictionary<string, string> Vars = new Dictionary<string, string>
        {
            ["max-download-size"] = "0x10000000", ["current-slot"] = "a", ["has-slot:boot"] = "yes",
            ["has-slot:recovery"] = "no", ["serialno"] = "FBE0R3SCUE", ["product"] = "sweet",
        };
        public readonly MemoryStream Received = new MemoryStream();
        public readonly Dictionary<string, byte[]> Flashed = new Dictionary<string, byte[]>();
        public string FailFlash;
        readonly Queue<string> replies = new Queue<string>();
        long expecting;

        public void Write(byte[] buffer, int offset, int count)
        {
            if (expecting > 0)
            {
                Received.Write(buffer, offset, count);
                expecting -= count;
                if (expecting == 0)
                    replies.Enqueue("OKAY");
                return;
            }
            string command = Encoding.ASCII.GetString(buffer, offset, count);
            Commands.Add(command);
            if (command.StartsWith("getvar:"))
            {
                string name = command.Substring(7);
                replies.Enqueue(Vars.TryGetValue(name, out string value) ? "OKAY" + value : "FAILGetVar Variable Not found");
            }
            else if (command.StartsWith("download:"))
            {
                expecting = Convert.ToInt64(command.Substring(9), 16);
                Received.SetLength(0);
                replies.Enqueue("DATA" + command.Substring(9));
            }
            else if (command.StartsWith("flash:"))
            {
                string partition = command.Substring(6);
                if (partition == FailFlash) { replies.Enqueue("FAILpartition does not exist"); return; }
                replies.Enqueue("INFOwriting '" + partition + "'");
                Flashed[partition] = Received.ToArray();
                replies.Enqueue("OKAY");
            }
            else if (command.StartsWith("reboot"))
                replies.Enqueue("OKAY");
            else
                replies.Enqueue("FAILunknown command");
        }

        public int Read(byte[] buffer, int timeoutMs)
        {
            if (replies.Count == 0)
                return 0;
            byte[] reply = Encoding.ASCII.GetBytes(replies.Dequeue());
            reply.CopyTo(buffer, 0);
            return reply.Length;
        }
    }

    public class FastbootClientTests
    {
        [Fact]
        public void Getvar_and_max_download_size()
        {
            FakeBootloader phone = new FakeBootloader();
            FastbootClient client = new FastbootClient(phone);
            Assert.Equal("FBE0R3SCUE", client.GetVar("serialno"));
            Assert.Equal(0x10000000, client.MaxDownloadSize());
            Assert.Null(client.TryGetVar("unlocked"));
            Assert.Throws<FastbootException>(() => client.GetVar("unlocked"));
            phone.Vars["max-download-size"] = "268435456";
            Assert.Equal(268435456, client.MaxDownloadSize());
        }

        [Fact]
        public void Download_and_flash_write_the_exact_bytes_to_the_slotted_partition()
        {
            FakeBootloader phone = new FakeBootloader();
            FastbootClient client = new FastbootClient(phone);
            byte[] image = Enumerable.Range(0, 3 * 1024 * 1024 + 17).Select(i => (byte)(i * 13)).ToArray();
            string target = client.SlottedName("boot");
            Assert.Equal("boot_a", target);
            long last = 0;
            client.Download(new MemoryStream(image), image.Length, sent => last = sent);
            client.Flash(target);
            Assert.Equal(image.Length, last);
            Assert.Equal(image, phone.Flashed["boot_a"]);
            Assert.Contains("download:" + image.Length.ToString("x8"), phone.Commands);
            Assert.Contains("writing 'boot_a'", client.Info);
            Assert.Equal("recovery", client.SlottedName("recovery"));
        }

        [Fact]
        public void A_forbidden_partition_is_never_sent_to_the_phone()
        {
            FakeBootloader phone = new FakeBootloader();
            FastbootClient client = new FastbootClient(phone);
            Assert.Throws<FastbootException>(() => client.Flash("modem_a"));
            Assert.Throws<FastbootException>(() => client.Flash("super"));
            Assert.DoesNotContain(phone.Commands, c => c.StartsWith("flash:"));
        }

        [Fact]
        public void Failures_and_silence_are_errors()
        {
            FakeBootloader phone = new FakeBootloader { FailFlash = "dtbo_a" };
            FastbootClient client = new FastbootClient(phone);
            client.Download(new MemoryStream(new byte[100]), 100);
            FastbootException e = Assert.Throws<FastbootException>(() => client.Flash("dtbo_a"));
            Assert.Contains("does not exist", e.Message);
            Assert.Throws<FastbootException>(() => client.Download(new MemoryStream(new byte[10]), 100));
        }

        [Theory]
        [InlineData("0x10000000", 0x10000000L)]
        [InlineData(" 0X4000000 ", 0x4000000L)]
        [InlineData("268435456", 268435456L)]
        [InlineData("0xFFFFFFFFFFFFFFFF", long.MaxValue)]
        [InlineData("18446744073709551615", long.MaxValue)]
        [InlineData("0xFFFFFFFF", 4294967295L)]
        [InlineData("-1", null)]
        [InlineData("0x", null)]
        [InlineData("big", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void Sizes_are_read_without_a_sign(string text, long? bytes) => Assert.Equal(bytes, FastbootClient.ParseSize(text));

        [Fact]
        public void Partitions_are_listed_with_their_sizes()
        {
            FakeBootloader phone = new FakeBootloader();
            phone.Vars["partition-size:boot_a"] = " 0x6000000";
            phone.Vars["partition-size:boot_b"] = "0x6000000";
            phone.Vars["partition-size:recovery"] = "0x4000000";
            Dictionary<string, long> parts = new FastbootClient(phone).AllowedPartitions();
            Assert.Equal(new[] { "boot_a", "boot_b", "recovery" }, parts.Keys.OrderBy(k => k));
            Assert.Equal(0x6000000, parts["boot_a"]);
        }

        [Fact]
        public void A_bootloader_without_partition_size_still_lists_the_partitions_it_knows()
        {
            // has-slot:boot = yes and has-slot:recovery = no, and no partition-size at all.
            Dictionary<string, long> parts = new FastbootClient(new FakeBootloader()).AllowedPartitions();
            Assert.Equal(new[] { "boot_a", "boot_b", "recovery" }, parts.Keys.OrderBy(k => k));
            Assert.All(parts.Values, v => Assert.Equal(-1, v));
            // Nothing the bootloader did not answer for is made up.
            Assert.DoesNotContain("dtbo", parts.Keys);
        }

        [Fact]
        public void A_cancelled_download_stops_before_the_next_piece_and_nothing_is_flashed()
        {
            FakeBootloader phone = new FakeBootloader();
            FastbootClient client = new FastbootClient(phone);
            using System.Threading.CancellationTokenSource stop = new System.Threading.CancellationTokenSource();
            byte[] image = new byte[3 * 1024 * 1024];
            Assert.Throws<OperationCanceledException>(() =>
                client.Download(new MemoryStream(image), image.Length, sent => stop.Cancel(), stop.Token));
            Assert.Equal(1 << 20, phone.Received.Length);
            Assert.DoesNotContain(phone.Commands, c => c.StartsWith("flash:"));
        }

        [Fact]
        public void An_unknown_slot_is_not_guessed()
        {
            FakeBootloader phone = new FakeBootloader();
            phone.Vars["current-slot"] = "";
            Assert.Throws<FastbootException>(() => new FastbootClient(phone).SlottedName("boot"));
        }

        [Fact]
        public void Commands_stay_within_the_protocol()
        {
            FastbootClient client = new FastbootClient(new FakeBootloader());
            Assert.Throws<ArgumentException>(() => client.GetVar("a b"));
            Assert.Throws<ArgumentException>(() => client.GetVar("x;reboot"));
            Assert.Throws<ArgumentException>(() => client.GetVar("a:b:c"));
            Assert.Throws<ArgumentException>(() => client.Reboot("edl"));
        }
    }
}
