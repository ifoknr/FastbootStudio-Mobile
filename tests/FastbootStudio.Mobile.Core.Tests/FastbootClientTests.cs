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
