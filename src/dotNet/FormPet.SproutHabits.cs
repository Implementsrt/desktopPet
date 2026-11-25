using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using NAudio.CoreAudioApi;

namespace DesktopPet
{
    public partial class FormPet
    {
        private const uint QuietAfter = 3 * 60 * 1000;
        private const uint SleepAfter = 10 * 60 * 1000;
        private const int SproutSleep = 17, SproutDance = 18, SproutPeek = 19, SproutTug = 20;
        private struct LastInput { internal uint Size, Tick; }
        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LastInput input);

        private sealed class Temper
        {
            internal readonly string Name;
            internal readonly int Tosses, Curiosity, Patience, Dance;
            internal readonly double Pause;
            internal Temper(string name, int tosses, int curiosity, int patience, int dance, double pause)
            { Name = name; Tosses = tosses; Curiosity = curiosity; Patience = patience; Dance = dance; Pause = pause; }
        }

        private static readonly Temper[] Tempers = {
            new Temper("好奇", 2, 60, 3, 3, 1),
            new Temper("淘气", 2, 85, 2, 3, .8),
            new Temper("暴脾气", 1, 45, 3, 4, .9),
            new Temper("胆小", 4, 30, 2, 5, 1.35),
            new Temper("冒失", 3, 75, 2, 3, 1.1),
            new Temper("慢性子", 5, 38, 4, 6, 1.65)
        };
        private int temperIndex, grudges, teasing, climbWins;
        private double lastThrownAt, skillAllowedAt, nextFlyAt, nextAudioAt, hiddenUntil, nextPeekAt;
        private double sleepCorner = double.NaN, sleepSearchUntil;
        private Func<uint> readSessionIdle = SproutSessionIdle;
        private MMDeviceEnumerator audioDevices;
        private MMDevice audioEndpoint;
        private int audibleSamples;
        private float audioLevel;
        private Temper Personality => Tempers[temperIndex];

        private void AddTemperMenu(ContextMenuStrip menu)
        {
            var personality = new ToolStripMenuItem("性格");
            foreach (Temper temper in Tempers)
            {
                int index = personality.DropDownItems.Count;
                var item = new ToolStripMenuItem(temper.Name);
                item.Checked = index == temperIndex;
                item.Click += (s, e) =>
                {
                    temperIndex = index;
                    foreach (ToolStripMenuItem option in personality.DropDownItems) option.Checked = option == item;
                };
                personality.DropDownItems.Add(item);
            }
            menu.Items.Add(personality);
        }

        private bool UpdateSleep(uint idle, double now)
        {
            int animation = CurrentAnimation.ID;
            if (idle < SleepAfter)
            {
                sleepCorner = double.NaN;
                if (animation != SproutSleep) return false;
                RestSprout(now);
                CompleteNaturalTransition(SproutIdle, false);
                RememberSproutEvent("小芽睡醒了");
                return true;
            }

            if (animation == SproutSleep) return false;
            if (hwndWindow == IntPtr.Zero)
            {
                Rectangle area = ScreenArea;
                if (double.IsNaN(sleepCorner))
                {
                    sleepCorner = PositionX < area.Left + area.Width / 2 ? area.Left + 12 : area.Right - Width - 12;
                    sleepSearchUntil = now + 3400;
                }
                double distance = sleepCorner - PositionX;
                if (Math.Abs(distance) > 8 && now < sleepSearchUntil)
                {
                    FaceLeft(distance < 0);
                    if (animation != SproutRun) { CompleteNaturalTransition(SproutRun, false); return true; }
                    return false;
                }
            }
            StopSproutSkill();
            CompleteNaturalTransition(SproutSleep, false);
            return true;
        }

        private static uint SproutSessionIdle()
        {
            var sample = new LastInput { Size = 8 };
            if (!GetLastInputInfo(ref sample)) return 0;
            uint idle = unchecked((uint)Environment.TickCount - sample.Tick);
            return idle < int.MaxValue ? idle : 0;
        }

        private void RememberToss(double now)
        {
            RememberSproutEvent("小芽被鼠标抓起来扔了出去");
            if (now - lastThrownAt > 90000) grudges = 0;
            grudges++;
            lastThrownAt = now;
        }

        private bool TrySproutHabits(double now, int animation)
        {
            if (animation == SproutPeek)
            {
                if (now < hiddenUntil && !CursorOverSprout()) return false;
                CompleteNaturalTransition(SproutIdle, false);
                return true;
            }
            if (animation == SproutTug) return false;
            if (animation == SproutDance)
            {
                if (ReadAudio(now)) { timer1.Interval = Math.Max(65, 155 - (int)(audioLevel * 170)); return false; }
                CompleteNaturalTransition(SproutIdle, false);
                return true;
            }
            if (animation != SproutIdle && animation != SproutSit) return false;
            if (sproutSkill != SproutSkill.None || CurrentAnimation.ID == SproutSleep) return false;

            Point pointer = Cursor.Position, center = SproutCenter;
            int dx = pointer.X - center.X, dy = pointer.Y - center.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (now >= skillAllowedAt && MouseButtons == MouseButtons.None)
            {
                if (now - lastThrownAt <= 90000 && grudges >= Personality.Tosses && distance < 190)
                {
                    grudges = 0;
                    BeginSproutSkill(SproutSkill.Swat, pointer);
                    return true;
                }
                if (teasing >= Personality.Patience && distance < 175)
                {
                    teasing = 0;
                    BeginSproutSkill(SproutSkill.Lasso, pointer);
                    return true;
                }
                if (dy < -Width * .85 && dy > -215 && Math.Abs(dx) < Width &&
                    now >= nextRoamAt && rand.Next(100) < Personality.Curiosity / 5)
                {
                    BeginSproutSkill(SproutSkill.Swing, pointer);
                    return true;
                }
                if (climbWins >= 3)
                {
                    climbWins = 0;
                    BeginSproutSkill(SproutSkill.Fireworks, pointer);
                    return true;
                }
                if (now >= nextFlyAt && now >= nextRoamAt)
                {
                    nextFlyAt = now + rand.Next(210000, 410001);
                    BeginSproutSkill(SproutSkill.Fly, pointer);
                    return true;
                }
            }
            if (hwndWindow != IntPtr.Zero && animation == SproutIdle && now >= nextPeekAt &&
                dy > Height / 3 && distance < Width * 3.0)
            {
                hiddenUntil = now + 2200;
                nextPeekAt = now + 18000;
                CompleteNaturalTransition(SproutPeek, false);
                return true;
            }
            if (animation == SproutIdle && ReadAudio(now))
            {
                CompleteNaturalTransition(SproutDance, false);
                return true;
            }
            return false;
        }

        private bool ReadAudio(double now)
        {
            if (now < nextAudioAt) return audibleSamples >= Personality.Dance;
            nextAudioAt = now + 240;
            try
            {
                if (audioDevices == null) audioDevices = new MMDeviceEnumerator();
                if (audioEndpoint == null) audioEndpoint = audioDevices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                audioLevel = audioEndpoint.AudioMeterInformation.MasterPeakValue;
                audibleSamples = audioLevel >= .055f ? Math.Min(10, audibleSamples + 1) : Math.Max(0, audibleSamples - 1);
            }
            catch (COMException)
            {
                audioEndpoint?.Dispose();
                audioEndpoint = null;
                audibleSamples = 0;
                nextAudioAt = now + 5000;
            }
            return audibleSamples >= Personality.Dance;
        }

        private void DisposeSproutAudio()
        {
            audioEndpoint?.Dispose();
            audioDevices?.Dispose();
            audioEndpoint = null;
            audioDevices = null;
        }
    }
}
