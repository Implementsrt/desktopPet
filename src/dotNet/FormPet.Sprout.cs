using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DesktopPet
{
    public partial class FormPet
    {
        private const int SproutIdle = 1, SproutRun = 2, SproutLook = 11, SproutDodge = 12, SproutBrake = 13,
            SproutClimb = 14, SproutHang = 15, SproutSit = 16;
        private enum ClimbOutcome { Success, Slip, Catch }
        private enum ClimbRoute { Wall, RunningJump, Grapnel }
        private IntPtr hopTarget;
        private IntPtr walkPastWindow;
        private bool walkPastLeft;
        private double nextWindowSearchAt, nextCursorReactionAt, cursorSampleAt;
        private double hopTravelDelay;
        private double nextRoamAt, nextScreenRoamAt, screenRouteExpiresAt;
        private bool reboundOnGround;
        private string roamScreen, airborneScreen;
        private IntPtr climbWindow;
        private IntPtr opposingClimbWindow;
        private int climbSide;
        private double climbStartedAt, climbStartFeet, climbStartX, climbDuration;
        private double climbReachAt, climbReachDuration, climbReachStartY;
        private ClimbRoute climbRoute;
        private SproutEffect climbRope;
        private double sitUntil, nextSitAt;
        private double climbApproachReadyAt;
        private ClimbOutcome climbOutcome;
        private Point lastCursor;
        private int resumeAfterCursor = SproutIdle;
        private bool facingBeforeCursor;
        private Dictionary<IntPtr, string> collisionWindows;
        private double refreshCollisionWindowsAt;

        private bool IsSprout => Xml?.IsSprout ?? false;

        private bool TrySproutAction()
        {
            if (IsDragging || IsTossing || IsLeaving || SkillMovesSprout || climbWindow != IntPtr.Zero) return false;
            if (CurrentAnimation.ID == Animations.AnimationKill) return false;
            double now = motionClock.Elapsed.TotalMilliseconds;
            if (hwndWindow != IntPtr.Zero && !CanSupportPet(hwndWindow))
            {
                StartSproutFall(now);
                return true;
            }
            if (hwndWindow == IntPtr.Zero && PositionY + Height < ScreenArea.Bottom - 2)
            {
                StartSproutFall(now);
                return true;
            }

            int animation = CurrentAnimation.ID;
            uint idle = readSessionIdle();
            if (idle >= SleepAfter || animation == SproutSleep)
                return UpdateSleep(idle, now);
            if (idle >= QuietAfter)
            {
                StopSproutSkill();
                if (animation == SproutIdle || animation == SproutSit) return false;
                RestSprout(now);
                CompleteNaturalTransition(SproutIdle, false);
                return true;
            }
            if (TrySproutHabits(now, animation)) return true;
            if (animation == SproutSit)
            {
                if (now < sitUntil) return false;
                CompleteNaturalTransition(SproutIdle, false);
                return true;
            }
            if (animation == SproutIdle && hwndWindow != IntPtr.Zero && now >= nextSitAt)
            {
                SitOnWindow(now);
                return true;
            }
            if (animation != SproutIdle && animation != SproutRun && animation != SproutBrake) return false;
            if (animation != SproutBrake && ReactToCursor(Cursor.Position, now, animation)) return true;
            if (animation == SproutRun && CursorOverSprout())
            {
                CompleteNaturalTransition(SproutIdle, false);
                return true;
            }
            if (animation == SproutIdle && (now < nextRoamAt || CursorOverSprout())) return false;
            if (walkPastWindow != IntPtr.Zero)
            {
                if (CanSupportPet(walkPastWindow) && TryGetPetWindowRect(walkPastWindow, out NativeMethods.RECT passed) && CanWalkUnder(passed) &&
                    (walkPastLeft ? PositionX + Width > passed.Left - 12 : PositionX < passed.Right + 12) &&
                    (walkPastLeft ? PositionX > ScreenArea.Left : PositionX + Width < ScreenArea.Right))
                {
                    FaceLeft(walkPastLeft);
                    if (animation == SproutRun) return false;
                    CompleteNaturalTransition(SproutRun, false);
                    return true;
                }
                walkPastWindow = IntPtr.Zero;
            }
            if (TryRoamBetweenScreens(now, animation)) return true;
            if (roamScreen != null) return false;

            // Revalidate the intended window at takeoff. A window can move between two sprite ticks.
            if (animation == SproutBrake && climbApproachReadyAt > 0)
            {
                if (now < climbApproachReadyAt) return true;
                climbApproachReadyAt = 0;
                if (StartWindowClimb(now, hopTarget, rand.Next(100))) return true;
                CompleteNaturalTransition(SproutIdle, false);
                return true;
            }
            if (animation == SproutBrake && AnimationStep < CurrentAnimation.Sequence.TotalSteps) return false;
            if (animation != SproutBrake && now >= nextWindowSearchAt)
            {
                nextWindowSearchAt = now + 320;
                FindHopTarget();
            }

            if (!TryGetHopTarget(hopTarget, out double distance, out double rise))
            {
                hopTarget = IntPtr.Zero;
                return false;
            }
            FaceLeft(distance < 0 || (distance == 0 && IsMovingLeft));
            bool hasTarget = TryGetPetWindowRect(hopTarget, out NativeMethods.RECT targetRect);
            bool climb = rise > Width * 2.3 || (hasTarget && RouteTo(targetRect) != ClimbRoute.Wall);
            double decisionDistance = climb ? Math.Max(8, Width * .12) : Width * 1.5;
            if (animation != SproutBrake && hasTarget && Math.Abs(distance) <= decisionDistance &&
                CanWalkUnder(targetRect) && rand.Next(2) == 0)
            {
                walkPastWindow = hopTarget;
                walkPastLeft = PositionX >= targetRect.Right;
                hopTarget = IntPtr.Zero;
                FaceLeft(walkPastLeft);
                if (animation == SproutRun) return false;
                CompleteNaturalTransition(SproutRun, false);
                return true;
            }
            if (animation == SproutBrake)
            {
                if (climb)
                {
                    CompleteNaturalTransition(SproutRun, false);
                    return true;
                }
                StartWindowHop(now, distance, rise);
                return true;
            }

            if (climb && Math.Abs(distance) <= Math.Max(8, Width * .12))
            {
                CompleteNaturalTransition(SproutBrake, false);
                PositionX += distance;
                climbApproachReadyAt = now + 145;
                QueueMotion(145);
                return true;
            }
            if (!climb && Math.Abs(distance) <= Width * 1.5)
            {
                CompleteNaturalTransition(SproutBrake, false);
                return true;
            }
            if (animation == SproutIdle)
            {
                CompleteNaturalTransition(SproutRun, false);
                return true;
            }
            return false;
        }

        private void SitOnWindow(double now)
        {
            sitUntil = now + rand.Next(4300, 7001);
            nextSitAt = sitUntil + 16000;
            CompleteNaturalTransition(SproutSit, false);
        }

        private bool ReactToCursor(Point pointer, double now, int animation)
        {
            Point center = new Point(Left + Width / 2, Top + Height / 2);
            var current = new Vector2(pointer.X - center.X, pointer.Y - center.Y);
            var previous = new Vector2(lastCursor.X - center.X, lastCursor.Y - center.Y);
            Vector2 change = current - previous;
            double interval = now - cursorSampleAt;
            cursorSampleAt = now;
            lastCursor = pointer;
            if (now < nextCursorReactionAt || interval <= 0 || interval > 400 || change.LengthSquared() < 9)
                return false;

            float closestTime = Math.Max(0, Math.Min(1, -Vector2.Dot(previous, change) / change.LengthSquared()));
            Vector2 closest = previous + change * closestTime;
            bool sweptPast = change.Length() / interval > .95 && closest.Length() < Width * 1.25 &&
                (current.Length() > Width * .74 || previous.Length() > Width * 2);
            bool approached = current.Length() < Width * 1.6 && previous.Length() > current.Length() + 3;
            if (!sweptPast && !approached) return false;
            if (sweptPast) teasing++;

            resumeAfterCursor = sweptPast ? animation : SproutIdle;
            if (!sweptPast) RestSprout(now);
            facingBeforeCursor = IsMovingLeft;
            nextCursorReactionAt = now + 3200;
            FaceLeft((sweptPast && Math.Abs(previous.X) > 3 ? previous.X : current.X) < 0);
            CompleteNaturalTransition(sweptPast ? SproutDodge : SproutLook, false);
            return true;
        }

        private bool CursorOverSprout()
        {
            Rectangle reach = Bounds;
            reach.Inflate(8, 8);
            return reach.Contains(Cursor.Position);
        }

        private void FindHopTarget()
        {
            double bestDistance = Width * 5;
            hopTarget = IntPtr.Zero;
            foreach (var window in GetMotionCollisionWindows())
            {
                if (window.Value == "Sheep" || !TryGetHopTarget(window.Key, out double distance, out _)) continue;
                if (Math.Abs(distance) >= bestDistance) continue;
                bestDistance = Math.Abs(distance);
                hopTarget = window.Key;
            }
        }

        private Dictionary<IntPtr, string> GetMotionCollisionWindows()
        {
            if (!IsSprout) return EnumerateWindows();
            double now = motionClock.Elapsed.TotalMilliseconds;
            if (collisionWindows == null || now >= refreshCollisionWindowsAt)
            {
                collisionWindows = EnumerateWindows();
                refreshCollisionWindowsAt = now + 160;
            }
            return collisionWindows;
        }

        private bool TryGetHopTarget(IntPtr window, out double distance, out double rise)
        {
            distance = rise = 0;
            if (window == IntPtr.Zero || window == hwndWindow || !CanSupportPet(window) ||
                !TryGetPetWindowRect(window, out NativeMethods.RECT rect))
                return false;

            Rectangle area = ScreenArea;
            rise = PositionY + Height - rect.Top;
            int left = Math.Max(area.Left, rect.Left) + Width / 4;
            int right = Math.Min(area.Right, rect.Right) - Width - Width / 4;
            if (rise < 12 || rect.Top - Height < area.Top || left > right)
                return false;

            double targetX = Math.Max(left, Math.Min(right, PositionX));
            double approach = targetX;
            if (rise > Width * 2.3 || RouteTo(rect) != ClimbRoute.Wall)
            {
                if (!TryGetClimbApproach(window, rect, out approach, out _, out _)) return false;
                targetX = approach < rect.Left ? left : right;
            }
            distance = approach - PositionX;
            return Math.Abs(distance) <= Width * 5 &&
                IsVisibleStrip(window, new Rectangle((int)targetX + Width / 2, rect.Top, 2, 5));
        }

        private ClimbRoute RouteTo(NativeMethods.RECT rect)
        {
            if (rect.Bottom >= PositionY + Height - 5) return ClimbRoute.Wall;
            return PositionY + Height * .58 - rect.Bottom + 19 <= Height * 1.12 ? ClimbRoute.RunningJump : ClimbRoute.Grapnel;
        }

        private double ClimbRunway(ClimbRoute route) => route == ClimbRoute.RunningJump ? Width * 1.55 :
            route == ClimbRoute.Grapnel ? Width * .20 : 0;

        private bool TryGetClimbApproach(IntPtr window, NativeMethods.RECT rect, out double approach, out int side, out ClimbRoute route)
        {
            Rectangle area = ScreenArea;
            route = RouteTo(rect);
            double runway = ClimbRunway(route);
            double left = rect.Left - Width - runway;
            double right = rect.Right + runway;
            int handY = (int)(PositionY + Height * .58);
            bool inside = PositionX + Width > rect.Left + 2 && PositionX < rect.Right - 2;
            bool leftUsable = left >= area.Left + Width / 4 && (inside || PositionX + Width <= rect.Left + 2) &&
                ClimbEdgeVisible(window, rect, -1, handY);
            bool rightUsable = right <= area.Right - Width - Width / 4 && (inside || PositionX >= rect.Right - 2) &&
                ClimbEdgeVisible(window, rect, 1, handY);
            side = leftUsable && (!rightUsable || Math.Abs(left - PositionX) < Math.Abs(right - PositionX)) ? -1 : 1;
            approach = side < 0 ? left : right;
            return leftUsable || rightUsable;
        }

        private bool StartWindowClimb(double now, IntPtr window, int roll)
        {
            if (!CanSupportPet(window) || !TryGetPetWindowRect(window, out NativeMethods.RECT rect) ||
                !TryGetClimbApproach(window, rect, out double approach, out int side, out ClimbRoute route) || Math.Abs(approach - PositionX) > 3)
                return false;
            PositionX = approach;
            climbWindow = window;
            climbSide = side;
            climbRoute = route;
            climbReachStartY = PositionY;
            double reachY = route == ClimbRoute.RunningJump ? Math.Min(PositionY - 18, rect.Bottom - Height * .77) : PositionY;
            climbReachDuration = route == ClimbRoute.RunningJump ? 855 : route == ClimbRoute.Grapnel ? 1150 : 0;
            climbReachAt = climbReachDuration > 0 ? now + climbReachDuration : 0;
            climbStartedAt = climbReachAt > 0 ? climbReachAt : now;
            climbStartFeet = reachY + Height;
            climbStartX = PositionX;
            climbDuration = ClimbSteps(climbStartFeet - rect.Top) * 700;
            opposingClimbWindow = climbReachAt > 0 ? IntPtr.Zero : FindOpposingWall(window, rect);
            climbOutcome = JumpOutcome(opposingClimbWindow, roll);
            hwndWindow = IntPtr.Zero;
            hopTarget = IntPtr.Zero;
            FaceLeft(climbSide > 0);
            CompleteNaturalTransition(route == ClimbRoute.RunningJump ? SproutRun : SproutClimb, false);
            if (route == ClimbRoute.Grapnel) climbRope = new SproutEffect();
            timer1.Interval = 55;
            SetMotionTimer(true);
            return true;
        }

        private void AdvanceClimb(double now)
        {
            IntPtr target = climbWindow;
            if (!CanSupportPet(target) || !TryGetPetWindowRect(target, out NativeMethods.RECT rect))
            {
                StartSproutFall(now);
                return;
            }

            Rectangle area = ScreenArea;
            double edge = climbSide < 0 ? rect.Left - Width : rect.Right;
            double berth = edge;
            double top = rect.Top - Height;
            double perch = edge - climbSide * (Width + 11);
            Point anchor = new Point(climbSide < 0 ? rect.Left + 12 : rect.Right - 12, rect.Top + 3);
            if (top < area.Top || berth < area.Left || berth > area.Right - Width ||
                perch < area.Left || perch > area.Right - Width ||
                !ClimbEdgeVisible(target, rect, climbSide, (int)(PositionY + Height * .58)) ||
                !IsVisibleStrip(target, new Rectangle((int)perch + Width / 2, rect.Top, 2, 5)) ||
                (climbRope != null && !IsVisibleStrip(target, new Rectangle(anchor.X - 4, anchor.Y - 3, 8, 8))))
            {
                StartSproutFall(now);
                return;
            }
            if (climbReachAt > 0)
            {
                AdvanceClimbApproach(now, rect, berth, anchor);
                motion.Reset(PositionX, PositionY, now);
                RenderMotion(now);
                return;
            }
            const double recovery = 850;
            double elapsed = now - climbStartedAt;
            double startTrouble = climbDuration * .5;
            double trouble = elapsed - startTrouble;
            if (climbOutcome == ClimbOutcome.Catch && trouble >= 0) elapsed -= Math.Min(trouble, recovery);
            double progress = Math.Min(1, elapsed / climbDuration);
            double sinceLip = elapsed - climbDuration;
            if (progress < 1)
            {
                double rise = climbStartFeet - rect.Top;
                double wallX;
                if (TryOpposingWallX(opposingClimbWindow, rect, out wallX))
                {
                    // Each segment starts and ends against a real surface. An even count returns to the target wall.
                    double legs = 2 * Math.Max(1, Math.Ceiling(rise / (Width * 1.7)));
                    double step = progress * legs;
                    int leg = (int)step;
                    double phase = step - leg;
                    bool movingAway = leg % 2 == 0;
                    double flight = Math.Max(0, Math.Min(1, (phase - .65) / .28));
                    double reach = movingAway ? flight : 1 - flight;
                    PositionX = berth + (wallX - berth) * reach;
                    PositionY = climbStartFeet - Height - (rise - Height * .44) * (leg + flight) / legs - 11 * Math.Sin(Math.PI * flight);
                    if (climbOutcome == ClimbOutcome.Slip && step >= legs / 2 + .80)
                    {
                        StartSproutFall(now, (movingAway ? climbSide : -climbSide) * 3.5);
                        return;
                    }
                    bool towardOther = flight >= .5 ? movingAway : !movingAway;
                    FaceLeft((climbSide < 0) == towardOther);
                    if (climbOutcome == ClimbOutcome.Catch && trouble >= 0 && trouble < recovery)
                    {
                        PositionY += 30 * Math.Sin(Math.PI * trouble / recovery);
                        SelectClimbFrame(trouble < 370 ? 11 : 48);
                    }
                    else SelectClimbFrame(flight > 0 && flight < 1 ? 11 : phase < .48 ? 35 : 36);
                }
                else if (opposingClimbWindow != IntPtr.Zero)
                {
                    StartSproutFall(now);
                    return;
                }
                else
                {
                    AdvanceClimbStroke(progress, rise, berth, rect.Bottom);
                }
            }
            else
            {
                AnimateClimbOver(sinceLip, berth, perch, top);
                if (sinceLip >= 1850)
                {
                    climbWindow = opposingClimbWindow = IntPtr.Zero;
                    CloseClimbRope();
                    hwndWindow = target;
                    currentWindowSize = rect;
                    climbWins++;
                    RestSprout(now);
                    CompleteNaturalTransition(Animations.AnimationFallSoft, false);
                }
            }
            if (climbRope != null) climbRope.DrawGrapnel(ClimbingHand(), anchor, 1);
            motion.Reset(PositionX, PositionY, now);
            RenderMotion(now);
        }

        private static ClimbOutcome JumpOutcome(IntPtr opposingWall, int roll) => opposingWall == IntPtr.Zero || roll < 80
            ? ClimbOutcome.Success : roll < 90 ? ClimbOutcome.Catch : ClimbOutcome.Slip;

        private void AnimateClimbOver(double elapsed, double berth, double perch, double top)
        {
            FaceLeft(climbSide > 0);
            if (elapsed < 420)
            {
                PositionX = berth;
                PositionY = top + Height * (.13 + .31 * (1 - EaseClimb(elapsed / 420)));
                SelectClimbFrame(elapsed < 150 ? 57 : 58);
            }
            else if (elapsed < 1050)
            {
                double knee = EaseClimb((elapsed - 420) / 630);
                PositionX = berth + (perch - berth) * .52 * knee;
                PositionY = top + Height * (.13 - .08 * knee);
                SelectClimbFrame(elapsed < 770 ? 59 : 60);
            }
            else
            {
                double stand = EaseClimb(Math.Min(1, (elapsed - 1050) / 800));
                PositionX = berth + (perch - berth) * (.52 + .48 * stand);
                PositionY = top + Height * .05 * (1 - stand);
                CloseClimbRope();
                SelectClimbFrame(elapsed < 1480 ? 61 : 62);
            }
        }

        private static double EaseClimb(double t) => t * t * (3 - 2 * t);

        private static double ClimbSteps(double rise) => Math.Max(2, Math.Ceiling(rise / 17));

        private void AdvanceClimbApproach(double now, NativeMethods.RECT rect, double berth, Point anchor)
        {
            double elapsed = now - (climbReachAt - climbReachDuration);
            if (climbRoute == ClimbRoute.Grapnel)
            {
                SelectClimbFrame(elapsed < 240 ? 43 : elapsed < 420 ? 44 : 45);
                Point hand = elapsed < 420 ? ClimbingHand(.34, .13) : ClimbingHand(.05, .25);
                climbRope.DrawGrapnel(hand, anchor, Math.Max(0, Math.Min(1, (elapsed - 355) / 500)));
            }
            else
            {
                const double takeoff = 490;
                double takeoffX = berth + climbSide * Width * .63;
                if (elapsed < takeoff)
                {
                    double run = Math.Max(0, elapsed) / takeoff;
                    PositionX = climbStartX + (takeoffX - climbStartX) * run * run;
                    SelectClimbFrame(2 + (int)(elapsed / 58) % 8);
                    return;
                }
                double flight = Math.Min(1, (elapsed - takeoff) / (climbReachDuration - takeoff));
                double reachY = Math.Min(climbReachStartY - 18, rect.Bottom - Height * .77);
                if (reachY < climbReachStartY - Height * 1.6) { StartSproutFall(now); return; }
                PositionX = takeoffX + (berth - takeoffX) * flight;
                PositionY = climbReachStartY + (reachY - climbReachStartY) * flight * (2 - flight) - Math.Sin(Math.PI * flight) * 8;
                SelectClimbFrame(flight < .67 ? 11 : 48);
            }
            if (now < climbReachAt) return;
            climbReachAt = 0;
            climbStartFeet = PositionY + Height;
            climbDuration = ClimbSteps(climbStartFeet - rect.Top) * 700;
            CompleteNaturalTransition(SproutClimb, false);
        }

        private void AdvanceClimbStroke(double progress, double rise, double berth, int bottom)
        {
            double step = progress * ClimbSteps(rise);
            int count = (int)step;
            double phase = step - count;
            double lift = Math.Max(0, Math.Min(1, (phase - .29) / .48));
            double climbed = count + lift * lift * (3 - 2 * lift);
            PositionY = climbStartFeet - Height - (rise - Height * .44) * climbed / ClimbSteps(rise);
            bool onWall = climbRoute != ClimbRoute.Grapnel || PositionY + Height * .82 < bottom;
            PositionX = onWall ? berth : berth + climbSide * Width * .20;
            FaceLeft(climbSide > 0);
            int pose = phase < .18 ? 0 : phase < .39 ? 1 : phase < .72 ? 2 : 3;
            SelectClimbFrame((onWall ? 35 : 49) + (count % 2) * 4 + pose);
        }

        private Point ClimbingHand(double x = .1, double y = .42) => new Point(
            (int)(PositionX + Width * (climbSide < 0 ? 1 - x : x)), (int)(PositionY + Height * y));

        private void CloseClimbRope()
        {
            climbRope?.Dispose();
            climbRope = null;
        }

        private IntPtr FindOpposingWall(IntPtr target, NativeMethods.RECT rect)
        {
            foreach (var window in GetMotionCollisionWindows())
                if (window.Key != target && window.Value != "Sheep" && TryOpposingWallX(window.Key, rect, out _))
                    return window.Key;
            return IntPtr.Zero;
        }

        private bool TryOpposingWallX(IntPtr wall, NativeMethods.RECT target, out double x)
        {
            x = 0;
            if (wall == IntPtr.Zero || !CanSupportPet(wall) ||
                !TryGetPetWindowRect(wall, out NativeMethods.RECT neighbor))
                return false;
            int gap = climbSide < 0 ? target.Left - neighbor.Right : neighbor.Left - target.Right;
            x = climbSide < 0 ? neighbor.Right + 1 : neighbor.Left - Width - 1;
            return gap >= Width + 16 && gap <= Width * 2.55 && neighbor.Top <= target.Top &&
                neighbor.Bottom >= climbStartFeet - Height * .35 &&
                ClimbEdgeVisible(wall, neighbor, -climbSide, (int)(PositionY + Height * .58));
        }

        private void SelectClimbFrame(int frame)
        {
            Image image = GetSprite(SkinOffset(frame));
            if (pictureBox1.Image != image) pictureBox1.Image = image;
        }

        private void StartSproutFall(double now, double horizontal = 0)
        {
            CloseClimbRope();
            if (CurrentAnimation.ID == SproutSit || CurrentAnimation.ID == SproutPeek)
            {
                PositionY += OffsetY;
                OffsetY = 0;
            }
            climbWindow = opposingClimbWindow = IntPtr.Zero;
            climbApproachReadyAt = 0;
            hwndWindow = IntPtr.Zero;
            hopTravelDelay = 0;
            RestSprout(now);
            StartSproutFlight(now, horizontal, 0, true);
        }

        private void StartSproutFlight(double now, double horizontal, double vertical, bool rebound)
        {
            CompleteNaturalTransition(Animations.AnimationToss, false);
            TossForce = new Vector2((float)horizontal, 0);
            tossVertVel = vertical;
            tossUpdatedAt = now;
            reboundOnGround = rebound;
            IsTossing = true;
            timer1.Interval = 30;
            ResetMotion();
        }

        private void RestSprout(double now)
        {
            nextRoamAt = now + rand.Next(4200, 7701) * Personality.Pause;
            hopTarget = IntPtr.Zero;
            walkPastWindow = IntPtr.Zero;
            roamScreen = null;
            climbApproachReadyAt = 0;
        }

        private bool CanWalkUnder(NativeMethods.RECT window) => hwndWindow == IntPtr.Zero &&
            PositionY + Height >= ScreenArea.Bottom - 2 && window.Bottom <= ScreenArea.Bottom;

        private bool TryRoamBetweenScreens(double now, int animation)
        {
            if (hwndWindow != IntPtr.Zero || animation == SproutBrake) return false;
            Screen origin = CurrentScreen;
            Screen[] screens = Screen.AllScreens;
            if (roamScreen != null && now >= screenRouteExpiresAt) roamScreen = null;
            if (roamScreen == null)
            {
                if (animation != SproutIdle || now < nextScreenRoamAt) return false;
                nextScreenRoamAt = now + rand.Next(12000, 20001);
                Screen pointerScreen = Screen.FromPoint(Cursor.Position);
                int totalWeight = 4; // Staying local is always an option.
                foreach (Screen screen in screens)
                    if (CanCross(origin, screen)) totalWeight += screen.Equals(pointerScreen) ? 4 : 1;
                int choice = rand.Next(totalWeight) - 4;
                if (choice < 0) return false;
                foreach (Screen screen in screens)
                {
                    if (!CanCross(origin, screen)) continue;
                    choice -= screen.Equals(pointerScreen) ? 4 : 1;
                    if (choice >= 0) continue;
                    roamScreen = screen.DeviceName;
                    screenRouteExpiresAt = now + origin.WorkingArea.Width / 0.055 + 1500;
                    break;
                }
            }

            Screen destination = Array.Find(screens, screen => screen.DeviceName == roamScreen);
            if (destination == null || !CanCross(origin, destination))
            {
                roamScreen = null;
                return false;
            }
            bool left = destination.Bounds.Left < origin.Bounds.Left;
            double seam = left ? origin.WorkingArea.Left : origin.WorkingArea.Right - Width;
            FaceLeft(left);
            if (Math.Abs(seam - PositionX) <= Math.Max(6, Width / 5))
            {
                if (now >= skillAllowedAt && rand.Next(100) < Personality.Curiosity / 2)
                {
                    BeginSproutSkill(SproutSkill.Portal, Cursor.Position);
                    return true;
                }
                StartScreenCrossing(destination, now, left);
                return true;
            }
            if (animation == SproutIdle)
            {
                CompleteNaturalTransition(SproutRun, false);
                return true;
            }
            return false;
        }

        private bool CanCross(Screen from, Screen to)
        {
            Rectangle origin = from.WorkingArea, target = to.WorkingArea;
            bool sharedSide = origin.Right == target.Left || origin.Left == target.Right;
            return sharedSide && Math.Max(origin.Top, target.Top) + Height * 2 <= Math.Min(origin.Bottom, target.Bottom)
                && origin.Bottom - target.Bottom < Width * 4;
        }

        private void StartScreenCrossing(Screen destination, double now, bool left)
        {
            airborneScreen = destination.DeviceName;
            hopTravelDelay = 0;
            double rise = Math.Max(0, ScreenArea.Bottom - destination.WorkingArea.Bottom);
            double lift = rise + Width * .9;
            double vertical = Math.Sqrt(2 * TossGravity * lift);
            double timeToCrest = vertical / TossGravity;
            StartSproutFlight(now, (left ? -1 : 1) * Width * 1.6 / timeToCrest, -vertical, false);
        }

        private Rectangle FlightArea(double nextX)
        {
            if (airborneScreen == null) return ScreenArea;
            Screen[] screens = Screen.AllScreens;
            int index = Array.FindIndex(screens, screen => screen.DeviceName == airborneScreen);
            if (index < 0) { airborneScreen = null; return ScreenArea; }
            Rectangle origin = ScreenArea;
            Rectangle destination = screens[index].WorkingArea;
            if (nextX >= destination.Left && nextX + Width <= destination.Right)
            {
                DisplayIndex = index;
                airborneScreen = null;
            }
            return Rectangle.Union(origin, destination);
        }

        private void StartWindowHop(double now, double distance, double rise)
        {
            double clearance = Math.Max(22, Width * .72);
            double launchSpeed = Math.Sqrt(2 * TossGravity * (rise + clearance));
            double timeAboveEdge = 2 * Math.Sqrt(2 * clearance / TossGravity);
            hopTravelDelay = launchSpeed / TossGravity - timeAboveEdge / 2;
            hwndWindow = IntPtr.Zero;
            hopTarget = IntPtr.Zero;
            StartSproutFlight(now, distance / timeAboveEdge, -launchSpeed, true);
        }

        private void FaceLeft(bool left)
        {
            if (left == IsMovingLeft) return;
            IsMovingLeft = left;
            spritesFlipped = !spritesFlipped;
        }
    }
}
