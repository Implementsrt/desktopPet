using System;
using System.Drawing;
using System.Windows.Forms;

namespace DesktopPet
{
    public partial class FormPet
    {
        private enum SproutSkill { None, Fireworks, Portal, Lasso, Swing, Swat, Fly }
        private SproutSkill sproutSkill;
        private SproutEffect skillEffect, exitEffect;
        private double skillStartedAt, lastSwingAt, swingVelocityX, swingVelocityY;
        private Point skillOrigin, portalDestination, flyOrigin;
        private double flyLastAt, flyCaughtAt, nextTugAt;
        private int tugsLeft;
        private string portalDevice;
        private int portalDisplay;
        private bool portalArrived;

        private bool SkillMovesSprout => sproutSkill == SproutSkill.Portal || sproutSkill == SproutSkill.Swing ||
            sproutSkill == SproutSkill.Fly;

        private void AddSproutSkills(ContextMenuStrip menu)
        {
            AddTemperMenu(menu);
            if (Xml.AnimationXML.Image.Skins == 2)
            {
                var skin = new ToolStripMenuItem("外观");
                var robot = new ToolStripMenuItem("像素小芽", null, (s, e) => ChangeSproutSkin(1));
                var starfish = new ToolStripMenuItem("蜜桃海星", null, (s, e) => ChangeSproutSkin(0));
                skin.DropDownItems.AddRange(new ToolStripItem[] { robot, starfish });
                skin.DropDownOpening += (s, e) =>
                {
                    robot.Checked = CurrentSkin == 1;
                    starfish.Checked = CurrentSkin == 0;
                };
                menu.Items.Add(skin);
            }
            AddAgentMenu(menu);
            var sit = new ToolStripMenuItem("坐下晃腿", null, (s, e) => SitOnWindow(motionClock.Elapsed.TotalMilliseconds));
            menu.Items.Add(sit);
            menu.Opening += (s, e) => sit.Enabled = hwndWindow != IntPtr.Zero && CanSupportPet(hwndWindow) &&
                !IsDragging && !IsTossing && !SkillMovesSprout;
            menu.Items.Add(new ToolStripSeparator());
        }

        private void ChangeSproutSkin(int skin)
        {
            CurrentSkin = skin;
            pictureBox1.Image = GetSprite(SkinOffset(CurrentAnimation.Sequence.Frames[0]));
        }

        private Point SproutCenter => new Point(Left + Width / 2, Top + Height / 2);

        private void BeginSproutSkill(SproutSkill skill, Point pointer)
        {
            if (IsDisposed || IsDragging || IsTossing || IsLeaving || climbWindow != IntPtr.Zero ||
                SkillMovesSprout || CurrentAnimation.ID == Animations.AnimationKill) return;
            StopSproutSkill();
            double now = motionClock.Elapsed.TotalMilliseconds;
            string intendedScreen = roamScreen;
            RestSprout(now);
            skillOrigin = SproutCenter;
            sproutSkill = skill;
            skillStartedAt = now;
            skillAllowedAt = now + 21000 * Personality.Pause;
            skillEffect = new SproutEffect();
            if (skill == SproutSkill.Portal)
            {
                PlanPortalDestination(intendedScreen);
                portalArrived = false;
                exitEffect = new SproutEffect();
            }
            if (skill == SproutSkill.Lasso || skill == SproutSkill.Swat)
            {
                FaceLeft(pointer.X < skillOrigin.X);
                CompleteNaturalTransition(SproutTug, false);
                nextTugAt = now + (skill == SproutSkill.Swat ? 285 : 300);
                tugsLeft = skill == SproutSkill.Swat ? 1 : 3;
            }
            if (skill == SproutSkill.Fly)
            {
                CompleteNaturalTransition(SproutRun, false);
                flyOrigin = new Point(Left + (IsMovingLeft ? -110 : 110), Top + Height / 2);
                flyLastAt = now;
                flyCaughtAt = -1;
            }
            if (skill == SproutSkill.Swing)
            {
                hwndWindow = IntPtr.Zero;
                CompleteNaturalTransition(Animations.AnimationToss, false);
                lastSwingAt = now;
                swingVelocityX = swingVelocityY = 0;
            }
            AdvanceSproutSkill(now, pointer);
            timer1.Start();
            SetMotionTimer(true);
        }

        private void PlanPortalDestination(string intendedScreen)
        {
            Screen[] screens = Screen.AllScreens;
            portalDisplay = DisplayIndex;
            int intended = Array.FindIndex(screens, screen => screen.DeviceName == intendedScreen);
            if (screens.Length > 1) portalDisplay = intended >= 0 ? intended : (DisplayIndex + 1) % screens.Length;
            portalDevice = screens[portalDisplay].DeviceName;
            Rectangle area = screens[portalDisplay].WorkingArea;
            int x = (Left - ScreenArea.Left < ScreenArea.Width / 2) ? area.Left + area.Width * 3 / 4 : area.Left + area.Width / 4;
            portalDestination = new Point(Math.Max(area.Left, Math.Min(x, area.Right - Width)), area.Bottom - Height);
        }

        private void AdvanceSproutSkill(double now, Point pointer)
        {
            if (sproutSkill == SproutSkill.None) return;
            if ((sproutSkill == SproutSkill.Lasso || sproutSkill == SproutSkill.Swat) &&
                (MouseButtons != MouseButtons.None || (GetAsyncKeyState(0x1B) & 0x8000) != 0))
            {
                FinishTug();
                return;
            }
            double elapsed = now - skillStartedAt;
            switch (sproutSkill)
            {
                case SproutSkill.Fireworks:
                    if (elapsed >= 2300) StopSproutSkill();
                    else
                    {
                        var origin = new Point(skillOrigin.X, skillOrigin.Y - 43);
                        skillEffect.DrawFireworks(origin, elapsed / 2300);
                    }
                    break;
                case SproutSkill.Portal:
                    AdvancePortal(elapsed, now);
                    break;
                case SproutSkill.Lasso:
                case SproutSkill.Swing:
                    AdvanceRope(elapsed, now, pointer);
                    break;
                case SproutSkill.Swat:
                    if (elapsed >= 720) FinishTug();
                    else
                    {
                        if (tugsLeft > 0 && now >= nextTugAt)
                        {
                            tugsLeft = 0;
                            MovePointer(pointer, SproutCenter, -26);
                        }
                        skillEffect.DrawSwat(pointer, elapsed);
                    }
                    break;
                case SproutSkill.Fly:
                    ChaseFly(elapsed, now);
                    break;
            }
        }

        private void AdvancePortal(double elapsed, double now)
        {
            if (elapsed >= 640 && !portalArrived)
            {
                portalArrived = true;
                Screen[] screens = Screen.AllScreens;
                int index = Array.FindIndex(screens, screen => screen.DeviceName == portalDevice);
                if (index < 0)
                {
                    StopSproutSkill();
                    return;
                }
                DisplayIndex = index;
                hwndWindow = IntPtr.Zero;
                PositionX = portalDestination.X;
                PositionY = portalDestination.Y;
                OffsetY = 0;
                CompleteNaturalTransition(SproutIdle, false);
                RestSprout(now);
            }
            if (elapsed >= 1450) { StopSproutSkill(); Opacity = 1; return; }
            Opacity = Math.Max(.12, Math.Min(1, Math.Abs(elapsed - 640) / (elapsed < 640 ? 220 : 330)));
            Point exit = new Point(portalDestination.X + Width / 2, portalDestination.Y + Height / 2);
            skillEffect.DrawPortal(skillOrigin, elapsed);
            exitEffect.DrawPortal(exit, elapsed);
        }

        private void AdvanceRope(double elapsed, double now, Point pointer)
        {
            Point center = SproutCenter;
            bool swing = sproutSkill == SproutSkill.Swing;
            double dx = pointer.X - center.X, dy = pointer.Y - center.Y;
            if (elapsed >= (swing ? 2400 : 1250) || dx * dx + dy * dy > 360 * 360)
            {
                StopSproutSkill();
                if (swing)
                    StartSproutFlight(now, swingVelocityX, swingVelocityY, true);
                else FinishTug();
                return;
            }
            double cast = Math.Min(1, elapsed / 230);
            Point tip = new Point(center.X + (int)(dx * cast), center.Y + (int)(dy * cast));
            if (swing && cast >= 1)
            {
                Rectangle area = ScreenArea;
                double angle = Math.Sin((elapsed - 230) / 285) * .82;
                double tension = Math.Min(1, (elapsed - 230) / 290);
                double targetX = pointer.X + Math.Sin(angle) * 93 - Width / 2.0;
                double targetY = pointer.Y + Math.Cos(angle) * 93 - Height / 2.0;
                double nextX = Math.Max(area.Left, Math.Min(area.Right - Width, targetX));
                double nextY = Math.Max(area.Top, Math.Min(area.Bottom - Height, targetY));
                double dt = Math.Max(1, now - lastSwingAt);
                double blend = (1 - Math.Exp(-dt / 48)) * tension;
                nextX = PositionX + (nextX - PositionX) * blend;
                nextY = PositionY + (nextY - PositionY) * blend;
                swingVelocityX = Math.Max(-12, Math.Min(12, (nextX - PositionX) * 30 / dt));
                swingVelocityY = Math.Max(-10, Math.Min(10, (nextY - PositionY) * 30 / dt));
                PositionX = nextX;
                PositionY = nextY;
                OffsetY = 0;
                lastSwingAt = now;
                motion.Reset(PositionX, PositionY, now);
                RenderMotion(now);
                center = SproutCenter;
            }
            bool taut = swing || elapsed >= 230 && tugsLeft > 0;
            if (!swing && tugsLeft > 0 && now >= nextTugAt)
            {
                tugsLeft--;
                nextTugAt = now + 105;
                MovePointer(pointer, center, 7);
            }
            skillEffect.DrawRope(center, tip, taut);
        }

        private void MovePointer(Point sampled, Point toward, int pixels)
        {
            if (MouseButtons != MouseButtons.None || (GetAsyncKeyState(0x1B) & 0x8000) != 0 ||
                Cursor.Position != sampled) return;
            Point destination = PointerDestination(sampled, toward, pixels, Screen.FromPoint(sampled).Bounds);
            if (destination != sampled) Cursor.Position = destination;
        }

        private static Point PointerDestination(Point sampled, Point toward, int pixels, Rectangle screen)
        {
            double dx = toward.X - sampled.X, dy = toward.Y - sampled.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 12 || length > 200) return sampled;
            return new Point(
                Math.Max(screen.Left, Math.Min(screen.Right - 1, sampled.X + (int)Math.Round(dx * pixels / length))),
                Math.Max(screen.Top, Math.Min(screen.Bottom - 1, sampled.Y + (int)Math.Round(dy * pixels / length))));
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);

        private void FinishTug()
        {
            StopSproutSkill();
            CompleteNaturalTransition(SproutIdle, false);
        }

        private void ChaseFly(double elapsed, double now)
        {
            Rectangle area = ScreenArea;
            int low = area.Left + Width / 2, high = area.Right - Width / 2;
            if (hwndWindow != IntPtr.Zero && TryGetPetWindowRect(hwndWindow, out NativeMethods.RECT edge))
            {
                low = Math.Max(low, edge.Left + Width / 2);
                high = Math.Min(high, edge.Right - Width / 2);
            }
            if (low >= high || elapsed >= 5000 || flyCaughtAt >= 0 && now - flyCaughtAt > 350)
            {
                StopSproutSkill();
                CompleteNaturalTransition(SproutIdle, false);
                return;
            }
            var point = new Point(Math.Max(low, Math.Min(high, flyOrigin.X + (int)(Math.Sin(elapsed / 530) * 95))),
                flyOrigin.Y - 13 + (int)(Math.Sin(elapsed / 90) * 16));
            double dt = Math.Max(0, Math.Min(50, now - flyLastAt));
            flyLastAt = now;
            if (flyCaughtAt < 0)
            {
                double distance = point.X - (PositionX + Width / 2.0);
                FaceLeft(distance < 0);
                PositionX += Math.Sign(distance) * Math.Min(Math.Abs(distance), dt * .115);
                motion.Reset(PositionX, PositionY + OffsetY, now);
                RenderMotion(now);
                if (Math.Abs(distance) < 9 && elapsed >= 1100)
                {
                    flyCaughtAt = now;
                    CompleteNaturalTransition(SproutLook, false);
                }
            }
            skillEffect.DrawFly(point, elapsed, flyCaughtAt >= 0);
        }

        private void StopSproutSkill()
        {
            if (sproutSkill == SproutSkill.Portal && !IsDisposed && !Disposing) Opacity = 1;
            sproutSkill = SproutSkill.None;
            skillEffect?.Dispose();
            exitEffect?.Dispose();
            skillEffect = exitEffect = null;
        }
    }

    internal sealed class SproutEffect : Form
    {
        private Action<Graphics> paintEffect;

        internal SproutEffect()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = TransparencyKey = Color.Magenta;
            DoubleBuffered = true;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x20 | 0x08000000; // click-through and no activation
                return parameters;
            }
        }

        private void ShowEffect(Rectangle bounds, Action<Graphics> paint)
        {
            paintEffect = paint;
            Bounds = bounds;
            if (!Visible) Show();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e) => paintEffect?.Invoke(e.Graphics);

        internal void DrawPortal(Point center, double elapsed)
        {
            double open = Math.Min(1, Math.Min(elapsed / 360, (1450 - elapsed) / 360));
            int radius = 6 + (int)(42 * open);
            ShowEffect(new Rectangle(center.X - 67, center.Y - 68, 134, 136), g =>
            {
                using (var shade = new SolidBrush(Color.FromArgb(28, 33, 70)))
                using (var edge = new Pen(Color.FromArgb(100, 235, 215), 2))
                using (var inner = new Pen(Color.Lavender, 2))
                using (var rings = new Pen(Color.MediumSlateBlue, 2))
                {
                    g.FillEllipse(shade, 67 - radius / 2, 68 - radius, radius, radius * 2);
                    g.DrawEllipse(edge, 67 - radius / 2, 68 - radius, radius, radius * 2);
                    for (int i = 0; i < 5; i++)
                    {
                        int spin = (int)(elapsed * .43) + i * 72;
                        int r = radius + i * 2;
                        g.DrawArc(i % 2 == 0 ? inner : rings, 67 - r / 2, 68 - r, r, r * 2, spin, 55);
                        double a = spin * Math.PI / 180;
                        int x = 67 + (int)(Math.Cos(a) * (r * .6 + 6)), y = 68 + (int)(Math.Sin(a) * (r + 9));
                        g.FillRectangle(Brushes.Aquamarine, x, y, 2 + i % 3, 2);
                    }
                }
            });
        }

        internal void DrawFly(Point center, double elapsed, bool caught)
        {
            ShowEffect(new Rectangle(center.X - 15, center.Y - 15, 30, 30), g =>
            {
                if (caught)
                {
                    g.DrawLine(Pens.Gold, 5, 5, 23, 23);
                    g.DrawLine(Pens.Gold, 23, 5, 5, 23);
                    g.FillRectangle(Brushes.White, 13, 13, 5, 5);
                    return;
                }
                int flap = (int)(elapsed / 41) % 2 == 0 ? -3 : 3;
                g.FillEllipse(Brushes.LightSkyBlue, 8, 11 + flap, 7, 4);
                g.FillEllipse(Brushes.LightSkyBlue, 15, 11 - flap, 7, 4);
                g.FillEllipse(Brushes.DarkSlateGray, 12, 12, 7, 7);
                g.FillRectangle(Brushes.Khaki, 14, 12, 2, 2);
            });
        }

        internal void DrawSwat(Point center, double elapsed)
        {
            ShowEffect(new Rectangle(center.X - 29, center.Y - 29, 58, 58), g =>
            {
                if (elapsed < 285)
                {
                    g.DrawArc(Pens.Gold, 10, 10, 38, 38, (int)(elapsed * 1.1), 125);
                    return;
                }
                int length = 7 + (int)((elapsed - 285) / 31);
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    float x = (float)Math.Cos(a), y = (float)Math.Sin(a);
                    g.DrawLine(Pens.Orange, 29 + x * 5, 29 + y * 5, 29 + x * length, 29 + y * length);
                }
            });
        }

        internal void DrawRope(Point origin, Point tip, bool swing)
        {
            var box = Rectangle.FromLTRB(Math.Min(origin.X, tip.X) - 13, Math.Min(origin.Y, tip.Y) - 13,
                Math.Max(origin.X, tip.X) + 14, Math.Max(origin.Y, tip.Y) + 32);
            ShowEffect(box, g =>
            {
                var start = new Point(origin.X - box.Left, origin.Y - box.Top);
                var end = new Point(tip.X - box.Left, tip.Y - box.Top);
                int sag = swing ? 5 : 22;
                using (var rope = new Pen(Color.Goldenrod, 2))
                {
                    g.DrawBezier(rope, start, new Point(start.X, start.Y + sag), new Point(end.X, end.Y + sag), end);
                    g.DrawEllipse(rope, end.X - 7, end.Y - 7, 14, 14);
                }
            });
        }

        internal void DrawGrapnel(Point hand, Point anchor, double progress)
        {
            if (progress <= 0) return;
            var hook = new Point((int)(hand.X + (anchor.X - hand.X) * progress),
                (int)(hand.Y + (anchor.Y - hand.Y) * progress - Math.Sin(Math.PI * progress) * 53));
            var box = Rectangle.FromLTRB(Math.Min(hand.X, hook.X) - 17, Math.Min(hand.Y, hook.Y) - 17,
                Math.Max(hand.X, hook.X) + 18, Math.Max(hand.Y, hook.Y) + 18);
            ShowEffect(box, g =>
            {
                var start = new Point(hand.X - box.X, hand.Y - box.Y);
                var tip = new Point(hook.X - box.X, hook.Y - box.Y);
                using (var shadow = new Pen(Color.FromArgb(46, 38, 31), 4))
                using (var rope = new Pen(Color.Wheat, 1.5f))
                using (var steel = new Pen(Color.LightSteelBlue, 2))
                {
                    int sag = (int)(13 * (1 - progress));
                    var control = new Point((start.X + tip.X) / 2 + sag, (start.Y + tip.Y) / 2 + sag);
                    g.DrawBezier(shadow, start, control, control, tip);
                    g.DrawBezier(rope, start, control, control, tip);
                    g.DrawLine(steel, tip.X, tip.Y + 8, tip.X, tip.Y - 5);
                    g.DrawLines(steel, new[] { new Point(tip.X - 7, tip.Y + 1), new Point(tip.X - 4, tip.Y - 6),
                        new Point(tip.X + 4, tip.Y - 6), new Point(tip.X + 7, tip.Y + 1) });
                }
            });
        }

        internal void DrawFireworks(Point center, double progress)
        {
            ShowEffect(new Rectangle(center.X - 146, center.Y - 173, 292, 238), g =>
            {
                Brush[] colors = { Brushes.Gold, Brushes.MediumTurquoise, Brushes.LightPink, Brushes.Lavender };
                for (int burst = 0; burst < 3; burst++)
                {
                    double local = (progress - burst * .18) / .64;
                    if (local < 0 || local > 1) continue;
                    int cx = burst == 0 ? 145 : burst == 1 ? 88 : 202;
                    int cy = burst == 0 ? 58 : 92;
                    if (local < .30)
                    {
                        double launch = local / .30;
                        int x = 145 + (int)((cx - 145) * launch);
                        int y = 231 + (int)((cy - 231) * launch);
                        g.FillRectangle(colors[burst], x, y, 3, 4);
                        g.FillRectangle(Brushes.OrangeRed, x + 1, y + 8, 2, 6);
                        g.FillRectangle(Brushes.DarkOrange, x + 2, y + 19, 1, 3);
                        continue;
                    }
                    double t = (local - .30) / .70;
                    if (t < .07) g.FillEllipse(Brushes.White, cx - 5, cy - 5, 10, 10);
                    for (int i = 0; i < 32; i++)
                    {
                        if (t > .66 && ((int)(local * 26) + i) % 3 == 0) continue;
                        double a = i * Math.PI / 16 + burst * .33;
                        double radius = (1 - Math.Pow(1 - t, 2)) * (31 + i % 5 * 8);
                        int x = cx + (int)(Math.Cos(a) * radius);
                        int y = cy + (int)(Math.Sin(a) * radius + t * t * 45);
                        int size = t > .68 ? 2 : 3;
                        g.FillRectangle(colors[(i + burst) % colors.Length], x, y, size, size);
                        if (t < .57) g.FillRectangle(colors[burst], x - (int)(Math.Cos(a) * 6), y - (int)(Math.Sin(a) * 6), 1, 2);
                    }
                }
            });
        }
    }
}
