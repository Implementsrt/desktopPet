using System;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace DesktopPet
{
    public partial class FormPet
    {
        private System.Windows.Forms.Timer observationTimer;
        private CancellationTokenSource observationLifetime;
        private DateTime firstObservationAt, lastObservationAt, lastChatAt, lastSpokenAt;
        private string lastObservation, lastAutonomousLine;
        private IntPtr lastForeground;
        private bool observing;

        private void StartSproutObservation()
        {
            if (observationTimer != null) return;
            firstObservationAt = DateTime.UtcNow.AddSeconds(25);
            observationLifetime = new CancellationTokenSource();
            observationTimer = new System.Windows.Forms.Timer { Interval = 8000 };
            observationTimer.Tick += ObserveSproutDesktop;
            observationTimer.Start();
        }

        private void StopSproutObservation()
        {
            observationTimer?.Dispose();
            observationTimer = null;
            observationLifetime?.Cancel();
            observationLifetime?.Dispose();
            observationLifetime = null;
        }

        private bool CanSpeakAutonomously(DateTime now) => !SproutAutoSpeech.Paused && !chatWaiting &&
            now - lastChatAt >= TimeSpan.FromMinutes(1) && Visible && Opacity >= .8 && !IsDragging && !IsTossing &&
            !IsLeaving && climbWindow == IntPtr.Zero && sproutSkill == SproutSkill.None && readSessionIdle() < QuietAfter &&
            CurrentAnimation.ID != Animations.AnimationKill && CurrentAnimation.ID != SproutSleep;

        private async void ObserveSproutDesktop(object sender, EventArgs e)
        {
            DateTime now = DateTime.UtcNow;
            if (observing || now < firstObservationAt || !SproutAutoSpeech.CanRequest(now) || !CanSpeakAutonomously(now) ||
                !File.Exists(SproutGeminiKey.Path)) return;
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            var context = ReadSproutContext(foreground);
            if (context.foreground == "fullscreen") return;
            string summary = new JavaScriptSerializer().Serialize(new { desktop = context, petEvents = SproutMemory.Local.EventContext() });
            if (summary == lastObservation && foreground == lastForeground && now - lastObservationAt < TimeSpan.FromMinutes(3)) return;
            string key;
            try { key = SproutGeminiKey.Load(); }
            catch (Exception error) when (SproutGeminiKey.IsStorageError(error))
            { SproutAutoSpeech.Defer(now.AddMinutes(30)); return; }
            if (string.IsNullOrEmpty(key)) return;
            SproutAutoSpeech.Record(now);
            lastObservation = summary;
            lastForeground = foreground;
            lastObservationAt = now;
            var cancellation = observationLifetime.Token;
            observing = true;
            try
            {
                var reply = await SproutGemini.Observe(key, Personality.Name, summary, cancellation);
                SproutAutoSpeech.Success();
                if (cancellation.IsCancellationRequested || !CanSpeakAutonomously(DateTime.UtcNow) || ReadSproutContext().foreground == "fullscreen") return;
                if (reply.Action == AgentSit || reply.Action == AgentFireworks) ExecuteAgentCommand(reply.Action);
                if (reply.Text.Length > 0 && reply.Text != lastAutonomousLine)
                {
                    SpeakSprout(reply.Text);
                    lastAutonomousLine = reply.Text;
                }
            }
            catch (SproutGemini.Error error)
            {
                if (!cancellation.IsCancellationRequested)
                {
                    if (error.Status == 429 || error.Status == 503) SproutAutoSpeech.RateLimited(DateTime.UtcNow, error.RetryAfter);
                    else SproutAutoSpeech.Defer(DateTime.UtcNow.AddMinutes(error.Status >= 400 && error.Status < 500 ? 15 : 2));
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (OperationCanceledException) { SproutAutoSpeech.Defer(DateTime.UtcNow.AddMinutes(2)); }
            catch (HttpRequestException) { SproutAutoSpeech.Defer(DateTime.UtcNow.AddMinutes(2)); }
            finally { observing = false; }
        }

        private static void RememberSproutEvent(string description)
        {
            try { SproutMemory.Local.AddEvent(description); }
            catch (Exception error) when (SproutGeminiKey.IsStorageError(error)) { }
        }

        private sealed class SproutContext
        {
            public string foreground { get; set; }
            public string visibleWindows { get; set; }
            public string pointer { get; set; }
            public bool perched { get; set; }
            public bool canSit { get; set; }
            public bool canFireworks { get; set; }
        }

        private SproutContext ReadSproutContext() => ReadSproutContext(NativeMethods.GetForegroundWindow());

        private SproutContext ReadSproutContext(IntPtr foreground)
        {
            string activity = "desktop";
            if (foreground == Handle || foreground == chatWindow?.Handle) activity = "pet";
            else if (IsDesktopContextWindow(foreground) && TryGetPetWindowRect(foreground, out NativeMethods.RECT bounds))
            {
                Rectangle active = Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
                Rectangle monitor = Screen.FromRectangle(active).Bounds;
                activity = active.Contains(monitor) ? "fullscreen" : active.IntersectsWith(ScreenBounds) ? "window_here" : "window_elsewhere";
            }
            int count = 0;
            NativeMethods.EnumWindows((window, unused) =>
            {
                if (IsDesktopContextWindow(window)) count++;
                return count < 4;
            }, IntPtr.Zero);
            Point pointer = Cursor.Position;
            Rectangle nearby = Bounds;
            nearby.Inflate(100, 100);
            return new SproutContext
            {
                foreground = activity,
                visibleWindows = count == 0 ? "none" : count == 1 ? "one" : count < 4 ? "few" : "many",
                pointer = nearby.Contains(pointer) ? "near_pet" : ScreenBounds.Contains(pointer) ? "same_screen" : "other_screen",
                perched = hwndWindow != IntPtr.Zero,
                canSit = hwndWindow != IntPtr.Zero && CanSupportPet(hwndWindow),
                canFireworks = motionClock.Elapsed.TotalMilliseconds >= skillAllowedAt
            };
        }

        private bool IsDesktopContextWindow(IntPtr window) => window != IntPtr.Zero && window != Handle && window != chatWindow?.Handle &&
            (ReadWindowStyle(window, -20) & (0x80 | 0x08000000)) == 0 && (ReadWindowStyle(window, -16) & 0x00C00000) != 0 &&
            CanSupportPet(window) && (GetDwmFlag(window, 14, out int cloaked, sizeof(int)) != 0 || cloaked == 0);
    }

    internal static class SproutAutoSpeech
    {
        private static readonly string PausePath = Path.Combine(Path.GetDirectoryName(SproutGeminiKey.Path), "pause-autonomous-speech");
        private static DateTime nextRequestAt;
        private static int consecutiveLimits;

        internal static bool Paused
        {
            get => File.Exists(PausePath);
            set
            {
                if (value)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(PausePath));
                    File.WriteAllText(PausePath, "");
                }
                else File.Delete(PausePath);
            }
        }

        internal static bool CanRequest(DateTime now) => now >= nextRequestAt;

        internal static void Record(DateTime now) => Defer(now.AddSeconds(45));

        internal static void Success() => consecutiveLimits = 0;

        internal static void RateLimited(DateTime now, TimeSpan? retryAfter)
        {
            consecutiveLimits = Math.Min(6, consecutiveLimits + 1);
            TimeSpan wait = retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero
                ? retryAfter.Value : TimeSpan.FromSeconds(45 * (1 << (consecutiveLimits - 1)));
            Defer(now + wait);
        }

        internal static void Defer(DateTime until)
        {
            if (until > nextRequestAt) nextRequestAt = until;
        }
    }
}
