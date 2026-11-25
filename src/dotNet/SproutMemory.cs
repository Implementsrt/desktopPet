using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace DesktopPet
{
    internal sealed class SproutMemory
    {
        internal static SproutMemory Local { get; set; } = new SproutMemory(Path.Combine(
            Path.GetDirectoryName(SproutGeminiKey.Path), "chat-memory.dat"));

        private readonly string path;
        private MemoryData data;

        public sealed class Entry
        {
            public string Role { get; set; }
            public string Text { get; set; }
            public DateTime At { get; set; }
        }

        public sealed class MemoryData
        {
            public List<Entry> Chat { get; set; } = new List<Entry>();
            public List<Entry> Events { get; set; } = new List<Entry>();
        }

        internal SproutMemory(string path) { this.path = path; }

        internal IReadOnlyList<Entry> Chat => Read().Chat.ToArray();

        internal void AddChat(string role, string text) => Append(Read().Chat, role, text, 160);

        internal void AddEvent(string text)
        {
            var events = Read().Events;
            if (events.Count > 0 && events.Last().Text == text && DateTime.UtcNow - events.Last().At < TimeSpan.FromSeconds(30)) return;
            Append(events, "event", text, 30);
        }

        internal void Clear()
        {
            File.Delete(path);
            data = new MemoryData();
        }

        internal string EventContext()
        {
            var events = Read().Events;
            return string.Join("；", events.Skip(Math.Max(0, events.Count - 6))
                .Select(row => row.At.ToLocalTime().ToString("MM-dd HH:mm") + " " + row.Text));
        }

        internal string Recall(string question)
        {
            // A small local index is enough to retrieve old topics without an embedding request.
            var terms = Bigrams(question);
            if (terms.Count == 0) return "";
            List<Entry> chat = Read().Chat;
            return string.Join("\n", chat.Take(Math.Max(0, chat.Count - 12))
                .Select(row => new { row, score = Bigrams(row.Text).Count(terms.Contains) })
                .Where(item => item.score >= 2).OrderByDescending(item => item.score).ThenByDescending(item => item.row.At).Take(4)
                .OrderBy(item => item.row.At).Select(item => (item.row.Role == "user" ? "用户：" : "小芽：") + Limit(item.row.Text, 300)));
        }

        internal SproutGemini.Turn[] RecentTurns()
        {
            var chat = Read().Chat;
            return chat.Skip(Math.Max(0, chat.Count - 12)).Select(row =>
                new SproutGemini.Turn(row.Role, Limit(row.Text, 1200))).ToArray();
        }

        private static HashSet<string> Bigrams(string text)
        {
            string clean = new string(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).Take(1200).ToArray());
            return new HashSet<string>(Enumerable.Range(0, Math.Max(0, clean.Length - 1)).Select(index => clean.Substring(index, 2)));
        }

        private static string Limit(string text, int count) => text.Length > count ? text.Substring(0, count) : text;

        private MemoryData Read()
        {
            if (data != null) return data;
            try
            {
                string saved = SproutLocalProtection.Read(path);
                data = saved == null ? new MemoryData() : new JavaScriptSerializer().Deserialize<MemoryData>(saved);
            }
            catch (ArgumentException) { data = new MemoryData(); }
            catch (InvalidOperationException) { data = new MemoryData(); }
            catch (Exception error) when (SproutGeminiKey.IsStorageError(error)) { data = new MemoryData(); }
            if (data?.Chat == null || data.Events == null) data = new MemoryData();
            return data;
        }

        private void Append(List<Entry> entries, string role, string text, int capacity)
        {
            entries.Add(new Entry { Role = role, Text = Limit(text, 4000), At = DateTime.UtcNow });
            if (entries.Count > capacity) entries.RemoveRange(0, entries.Count - capacity);
            SproutLocalProtection.Write(path, new JavaScriptSerializer().Serialize(data));
        }
    }

    internal static class SproutLocalProtection
    {
        internal static string Read(string path)
        {
            byte[] encrypted;
            try { encrypted = File.ReadAllBytes(path); }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            byte[] plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(plain); }
            finally { Array.Clear(plain, 0, plain.Length); }
        }

        internal static void Write(string path, string contents)
        {
            byte[] plain = Encoding.UTF8.GetBytes(contents);
            try
            {
                byte[] encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temporary = path + "." + Guid.NewGuid().ToString("N");
                try
                {
                    File.WriteAllBytes(temporary, encrypted);
                    if (File.Exists(path)) File.Replace(temporary, path, null);
                    else File.Move(temporary, path);
                }
                finally { File.Delete(temporary); }
            }
            finally { Array.Clear(plain, 0, plain.Length); }
        }
    }
}
