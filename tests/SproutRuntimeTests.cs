using System;
using System.Collections;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Windows.Forms;

class SproutRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    static void InFront(Form window) => SetWindowPos(window.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13);

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--agent-status")
            {
                Console.WriteLine(SendAgentCommand("status"));
                return 0;
            }
            var app = Assembly.LoadFrom("DesktopPet.exe");
            if (args.Length == 1 && args[0] == "--gemini-smoke") return TestLiveGemini(app);
            TestMemory(app);
            TestGeminiTransport(app);
            TestAutonomyBudget(app);
            var data = app.GetType("DesktopPet.Program", true).GetField("MyData", Members).GetValue(null);
            Call(data, "SetXml", "", "sprout-tests");
            Call(data, "SetIntValue", "Smooth", 1);
            var xml = Activator.CreateInstance(app.GetType("DesktopPet.Xml", true), 1);
            Check((bool)Call(xml, "ReadXML"), "Embedded pet failed to load");
            var animations = Activator.CreateInstance(app.GetType("DesktopPet.Animations", true), xml);
            Call(xml, "LoadAnimations", animations);
            for (int i = 0; i < Screen.AllScreens.Length; i++)
                ExerciseDisplay(app, xml, animations, i);
            Console.WriteLine("PASS Sprout runtime on {0} display(s)", Screen.AllScreens.Length);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    static void ExerciseDisplay(Assembly app, object xml, object animations, int display)
    {
        Rectangle area = Screen.AllScreens[display].WorkingArea;
        using (var window = new Form())
        using (var pet = (Form)Activator.CreateInstance(app.GetType("DesktopPet.FormPet", true), animations, xml))
        {
            window.Text = "DesktopPet Sprout test";
            window.TopMost = true;
            window.StartPosition = FormStartPosition.Manual;
            int windowX = Cursor.Position.X < area.Left + area.Width / 2 ?
                area.Left + area.Width * 2 / 3 : area.Left + area.Width / 3;
            window.Bounds = new Rectangle(windowX, area.Bottom - 76, 310, 180);
            window.Show();
            foreach (Image sprite in (IList)Get(xml, "sprites")) Call(pet, "AddImage", sprite);
            pet.GetType().GetMethod("Show", new[] { typeof(int), typeof(int) }).Invoke(pet, new[] { Get(xml, "spriteWidth"), Get(xml, "spriteHeight") });
            Set(pet, "DisplayIndex", display);
            Call(pet, "Play", true, -1);
            Set(pet, "firstObservationAt", DateTime.MaxValue);
            Set(pet, "DisplayIndex", display);
            Set(pet, "readSessionIdle", new Func<uint>(() => 0));
            Set(pet, "skillAllowedAt", double.MaxValue);
            Set(pet, "nextAudioAt", double.MaxValue);
            Check(pet.Width == 76 && pet.Height == 76 && ((IList)Get(xml, "sprites")).Count == 130,
                "High-resolution source did not retain the 76 pixel desktop size and both skin sheets");
            var picture = (PictureBox)Get(pet, "pictureBox1");
            Check((int)Get(pet, "CurrentSkin") == 1, "Pet did not start with the robot skin");
            Call(pet, "ChangeSproutSkin", 1);
            Image robot = picture.Image;
            Call(pet, "ChangeSproutSkin", 0);
            Check(picture.Image != robot, "Switching to the starfish did not replace the sprite");
            Call(pet, "ChangeSproutSkin", 1);
            Check(picture.Image == robot, "Switching back did not restore the robot sprite");
            var timer = (System.Windows.Forms.Timer)Get(pet, "timer1");
            timer.Stop();
            Place(pet, window.Left - pet.Width - 17, area.Bottom - pet.Height);
            Set(pet, "nextCursorReactionAt", double.MaxValue);
            int idleX = pet.Left;
            timer.Interval = 1;
            timer.Start();
            bool idleMoved = WaitUntil(() => pet.Left != idleX || (int)Get(Get(pet, "CurrentAnimation"), "ID") != 1, 1300);
            Check(!idleMoved, string.Format("Autonomous movement ignored its idle period, x={0}, initial={1}, animation={2}, display={3}",
                pet.Left, idleX, Get(Get(pet, "CurrentAnimation"), "ID"), Get(pet, "DisplayIndex")));
            Set(pet, "nextRoamAt", 0.0);
            InFront(window);
            Set(pet, "layersCheckedAt", -100.0);
            // Keep this trajectory on the test fixture even when a real user's windows are nearby.
            Set(pet, "hopTarget", window.Handle);
            Set(pet, "nextWindowSearchAt", double.MaxValue);
            object[] planned = { window.Handle, 0.0, 0.0 };
            bool available = (bool)Call(pet, "TryGetHopTarget", planned);
            Check(available, string.Format("Low test window was not available: {0}, target={1}, pet={2}, area={3}, distance={4}, rise={5}",
                window.Handle, VisibleFrame(pet, window), pet.Bounds, area, planned[1], planned[2]));
            timer.Interval = 1;
            timer.Start();

            bool ran = false, braked = false, jumped = false;
            bool landedOnFixture = WaitUntil(() =>
            {
                int id = (int)Get(Get(pet, "CurrentAnimation"), "ID");
                ran |= id == 2;
                braked |= id == 13;
                jumped |= (bool)Get(pet, "IsTossing");
                return (IntPtr)Get(pet, "hwndWindow") == window.Handle;
            }, 4000);
            Check(landedOnFixture, string.Format("Display {0}: no landing at {1}; target={2}, ran={3}, braked={4}, jumped={5}",
                display, pet.Location, VisibleFrame(pet, window), ran, braked, jumped));
            Check(ran && braked && jumped, "Window approach skipped part of its animation");
            Check(pet.Top + pet.Height == window.Top, "Feet missed the top of the support");
            Point landed = pet.Location;
            window.Location += new Size(42, -20);
            Check(WaitUntil(() => pet.Location == landed + new Size(42, -20), 450), "Pet did not follow the moving window");

            window.WindowState = FormWindowState.Minimized;
            Check(WaitUntil(() => (IntPtr)Get(pet, "hwndWindow") == IntPtr.Zero, 1000), "Pet stayed attached to a minimized window");
            Check((bool)Get(pet, "IsTossing"), "Window loss did not start continuous gravity");
            int fallStart = pet.Top;
            Check(WaitUntil(() => pet.Top > fallStart + 6 && pet.Top < area.Bottom - pet.Height - 18, 450),
                "Falling had no intermediate rendered position");
            Check(WaitUntil(() => (double)Get(pet, "tossVertVel") < 0, 900),
                "Taskbar impact stopped without a rebound");
            Check((bool)Get(pet, "IsTossing") && pet.Bottom <= area.Bottom, "Bounce went below the taskbar");
            Check(WaitUntil(() => pet.Bottom == area.Bottom, 1500), "Pet failed to land after minimizing");
            Check(WaitUntil(() => !(bool)Get(pet, "IsTossing"), 1500), "Bouncing did not settle");
            Console.WriteLine("PASS display {0}: idle, approach, jump, follow, continuous fall and taskbar rebound", display);

            timer.Stop();
            Call(pet, "SetNewAnimation", 1);
            Set(pet, "nextCursorReactionAt", 0.0);
            Set(pet, "cursorSampleAt", 0.0);
            Point center = new Point(pet.Left + pet.Width / 2, pet.Top + pet.Height / 2);
            Check(!(bool)Call(pet, "ReactToCursor", center + new Size(70, 0), 1000.0, 1), "First sample reacted");
            Check((bool)Call(pet, "ReactToCursor", center + new Size(50, 0), 1100.0, 1), "Slow approach did not trigger look");
            Check((int)Get(Get(pet, "CurrentAnimation"), "ID") == 11, "Slow approach triggered the wrong pose");
            Set(pet, "nextCursorReactionAt", 0.0);
            Call(pet, "ReactToCursor", center + new Size(-120, 0), 2000.0, 1);
            Check((bool)Call(pet, "ReactToCursor", center + new Size(120, 0), 2040.0, 1), "Fast pass did not trigger dodge");
            Check((int)Get(Get(pet, "CurrentAnimation"), "ID") == 12, "Fast pass triggered the wrong pose");
            Console.WriteLine("PASS display {0}: slow pointer approach and fast pointer sweep", display);

            TestTallWindow(pet, window, area);
            TestWalkUnder(pet, window, area);

            Set(pet, "hwndWindow", IntPtr.Zero);
            Place(pet, area.Left + area.Width / 3, area.Bottom - pet.Height - 4);
            double flightStart = ((Stopwatch)Get(pet, "motionClock")).Elapsed.TotalMilliseconds;
            double flightX = (double)Get(pet, "PositionX");
            Call(pet, "StartSproutFlight", flightStart, 9.0, 20.0, true);
            Call(pet, "AdvanceToss", flightStart + 30);
            Check((double)Get(pet, "tossVertVel") < 0, "A toss towards the taskbar did not rebound");
            double bounceX = (double)Get(pet, "PositionX");
            Call(pet, "AdvanceToss", flightStart + 60);
            double laterX = (double)Get(pet, "PositionX");
            Check(bounceX > flightX && laterX > bounceX && laterX - bounceX < bounceX - flightX,
                "Horizontal inertia stopped or failed to attenuate at impact");
            Check(WaitUntil(() => !(bool)Get(pet, "IsTossing"), 1800), "Toss never came to rest");
            Console.WriteLine("PASS taskbar rebound retains horizontal momentum");

            if (Screen.AllScreens.Length > 1)
            {
                int destination = (display + 1) % Screen.AllScreens.Length;
                Rectangle nextArea = Screen.AllScreens[destination].WorkingArea;
                var drop = new Point(nextArea.Left + 140, nextArea.Top + 180);
                Set(pet, "IsDragging", true);
                Set(pet, "dragAnchor", Point.Empty);
                double releasedAt = ((Stopwatch)Get(pet, "motionClock")).Elapsed.TotalMilliseconds;
                Call(Get(pet, "dragVelocity"), "Reset", (double)drop.X, (double)drop.Y, releasedAt - 100);
                Call(pet, "ReleaseDrag", drop, releasedAt);
                Check((int)Get(pet, "DisplayIndex") == destination && pet.Location == drop,
                    "Release across displays used the original screen or changed the drop point");
                Check((bool)Get(pet, "IsTossing") && !(bool)Get(pet, "IsDragging") &&
                    (double)Get(pet, "tossVertVel") == 0, "A stationary release did not start free fall");
                Check(WaitUntil(() => !(bool)Get(pet, "IsTossing"), 4000), "Cross-screen drop never came to rest");
                Console.WriteLine("PASS display {0} to {1}: drag release switches display", display, destination);
                if ((bool)Call(pet, "CanCross", Screen.AllScreens[display], Screen.AllScreens[destination]))
                {
                    window.Hide();
                    Set(pet, "DisplayIndex", display);
                    Set(pet, "hwndWindow", IntPtr.Zero);
                    bool left = nextArea.Left < area.Left;
                    int seam = left ? area.Left + 6 : area.Right - pet.Width - 6;
                    Place(pet, seam, area.Bottom - pet.Height);
                    Call(pet, "SetNewAnimation", 2);
                    Set(pet, "roamScreen", Screen.AllScreens[destination].DeviceName);
                    Set(pet, "screenRouteExpiresAt", double.MaxValue);
                    Check((bool)Call(pet, "TrySproutAction"), "Autonomous route did not take off at the monitor seam");
                    Check(WaitUntil(() => !(bool)Get(pet, "IsTossing"), 1800), "Cross-screen jump did not settle");
                    Check((int)Get(pet, "DisplayIndex") == destination &&
                        nextArea.Contains(pet.Bounds) && pet.Bottom == nextArea.Bottom, "Cross-screen jump landed on the wrong screen");
                    Console.WriteLine("PASS display {0} to {1}: autonomous crossing", display, destination);
                }
            }
            TestSkills(pet, area, display);
            TestSleep(pet, area, display);
            Call(pet, "SpeakSprout", "你好，我是小芽！");
            var speech = (Form)Get(pet, "speechBubble");
            Check(speech.Visible && area.Contains(speech.Bounds), "Chat speech bubble was not visible inside the display");
            Point original = speech.Location;
            pet.Left += 160;
            Check(speech.Left > original.X, "Chat speech bubble did not follow the pet");
            Call(pet, "CloseSpeech");
            Check(speech.IsDisposed, "Chat speech bubble did not close");
            using (var transcript = new FlowLayoutPanel { Width = 360 })
            {
                Call(pet, "AddChatRow", transcript, "用户消息", true, true);
                int outgoing = transcript.Controls[0].Controls[0].Left;
                Call(pet, "AddChatRow", transcript, "小芽回复", false, true);
                Check(outgoing > 100 && transcript.Controls[1].Controls[0].Left < 20, "Chat messages did not align right and left");
            }
            if (display == 0) TestAgent(pet);
        }
    }

    static void TestTallWindow(Form pet, Form window, Rectangle area)
    {
        window.WindowState = FormWindowState.Normal;
        window.Bounds = new Rectangle(area.Left + area.Width / 2, area.Bottom - 410, 330, 440);
        window.Show();
        InFront(window);
        Rectangle visible = VisibleFrame(pet, window);
        Set(pet, "nextCursorReactionAt", double.MaxValue);
        foreach (int roll in new[] { 0, 75, 99 })
        {
            InFront(window);
            Set(pet, "hwndWindow", IntPtr.Zero);
            Set(pet, "layersCheckedAt", -100.0);
            int x = roll == 75 ? visible.Right + 2 : visible.Left - pet.Width - 2;
            Place(pet, x, area.Bottom - pet.Height);
            Call(pet, "SetNewAnimation", 1);
            object[] target = { window.Handle, 0.0, 0.0 };
            bool eligible = (bool)Call(pet, "TryGetHopTarget", target);
            Check(eligible && (double)target[2] >= 400, string.Format(
                "Tall window was unavailable: roll={0}, window={1}, pet={2}, distance={3}, rise={4}",
                roll, window.Bounds, pet.Bounds, target[1], target[2]));
            double now = ((Stopwatch)Get(pet, "motionClock")).Elapsed.TotalMilliseconds;
            Check((bool)Call(pet, "StartWindowClimb", now, window.Handle, roll), "Climb rejected contact with the visible edge");
            IntPtr opposite = (IntPtr)Get(pet, "opposingClimbWindow");
            var motionTimer = (System.Windows.Forms.Timer)Get(pet, "motionTimer");
            var spriteTimer = (System.Windows.Forms.Timer)Get(pet, "timer1");
            motionTimer.Stop();
            spriteTimer.Stop();
            double duration = (double)Get(pet, "climbDuration");
            Check(duration >= 14000 && opposite == IntPtr.Zero, "Side climb was too quick or selected an unrelated opposing wall");
            Call(pet, "AdvanceClimb", now + 110);
            int braced = pet.Bottom;
            var image = ((PictureBox)Get(pet, "pictureBox1")).Image;
            Call(pet, "AdvanceClimb", now + 185);
            Check(pet.Bottom == braced && ((PictureBox)Get(pet, "pictureBox1")).Image != image,
                "Pet did not reach for a new hold before raising its body");
            Call(pet, "AdvanceClimb", now + 630);
            Check(pet.Bottom < braced && pet.Bottom >= braced - 19, "Pet did not climb by one measured step");
            Call(pet, "AdvanceClimb", now + duration * .55);
            Check(pet.Bottom < area.Bottom - 120, "Pet did not climb above a short hop");
            Call(pet, "AdvanceClimb", now + duration + 60);
            int lipX = pet.Left;
            int lipY = pet.Top;
            var hanging = ((PictureBox)Get(pet, "pictureBox1")).Image;
            Call(pet, "AdvanceClimb", now + duration + 330);
            Check(pet.Left == lipX && pet.Top < lipY && ((PictureBox)Get(pet, "pictureBox1")).Image != hanging,
                "Pet slid horizontally instead of pulling up by its hands");
            Call(pet, "AdvanceClimb", now + duration + 960);
            Check(Math.Abs(pet.Left - lipX) > 15 && (IntPtr)Get(pet, "hwndWindow") == IntPtr.Zero,
                "Pet did not plant a knee before standing");
            Call(pet, "AdvanceClimb", now + duration + 1851);
            Check((IntPtr)Get(pet, "climbWindow") == IntPtr.Zero, "Climbing did not finish");
            spriteTimer.Start();
            Check((IntPtr)Get(pet, "hwndWindow") == window.Handle && pet.Bottom == window.Top,
                "Ordinary climb failed to attach to the window");
            if (roll == 0)
            {
                    Set(pet, "nextSitAt", 0.0);
                    Call(pet, "SetNewAnimation", 1);
                    Check((bool)Call(pet, "TrySproutAction"), "An idle pet on a window did not sit");
                    Check((int)Get(Get(pet, "CurrentAnimation"), "ID") == 16 && pet.Bottom > window.Top &&
                        (double)Get(pet, "PositionY") + pet.Height == window.Top, "Seated legs did not hang below the window edge");
                    Point seated = pet.Location;
                    window.Left += 31;
                    Check(WaitUntil(() => pet.Location == seated + new Size(31, 0), 450), "Seated pet did not follow the window");
                    window.Left -= 31;
                    Check(WaitUntil(() => VisibleFrame(pet, window) == visible, 600), "Window did not return to its prior visible bounds");
                    Set(pet, "nextSitAt", double.MaxValue);
            }
        }
        using (var other = new Form())
        {
            other.Text = "Opposing wall fixture";
            other.TopMost = true;
            other.StartPosition = FormStartPosition.Manual;
            other.Bounds = new Rectangle(window.Left - 280, window.Top, 190, window.Height);
            other.Show();
            InFront(window);
            visible = VisibleFrame(pet, window);
            Set(pet, "hwndWindow", IntPtr.Zero);
            Place(pet, visible.Left - pet.Width - 2, area.Bottom - pet.Height);
            Call(pet, "SetNewAnimation", 1);
            Set(pet, "refreshCollisionWindowsAt", 0.0);
            Set(pet, "layersCheckedAt", -100.0);
            double now = ((Stopwatch)Get(pet, "motionClock")).Elapsed.TotalMilliseconds;
            Check((bool)Call(pet, "StartWindowClimb", now, window.Handle, 0),
                string.Format("Could not start opposing wall climb: window={0}, other={1}, pet={2}", visible, VisibleFrame(pet, other), pet.Bounds));
            Check((IntPtr)Get(pet, "opposingClimbWindow") == other.Handle, "Climb did not recognize the second real wall");
            double climbTime = (double)Get(pet, "climbDuration");
            double legs = 2 * Math.Ceiling(((double)Get(pet, "climbStartFeet") - visible.Top) / (pet.Width * 1.7));
            Call(pet, "AdvanceClimb", now + climbTime / legs);
            int otherRight = VisibleFrame(pet, other).Right;
            Check(pet.Left <= otherRight + 3 && pet.Left >= otherRight, "First wall jump did not touch the other window");
            Call(pet, "AdvanceClimb", now + climbTime * 2 / legs);
            Check(pet.Right >= visible.Left - 3 && pet.Right <= visible.Left, "Second wall jump did not return to the window");
            Call(pet, "AdvanceClimb", now + climbTime + 1851);
            Check((IntPtr)Get(pet, "hwndWindow") == window.Handle, "Successful wall jumps did not reach the top");
            Set(pet, "climbWindow", IntPtr.Zero);

            foreach (int roll in new[] { 85, 95 })
            {
                Set(pet, "hwndWindow", IntPtr.Zero);
                Place(pet, visible.Left - pet.Width - 2, area.Bottom - pet.Height);
                Check((bool)Call(pet, "StartWindowClimb", now, window.Handle, roll), "Could not start wall jump result test");
                Call(pet, "AdvanceClimb", now + climbTime / 2);
                int bracedY = pet.Top;
                Call(pet, "AdvanceClimb", now + climbTime / 2 + 420);
                if (roll == 85)
                {
                    Check(pet.Top > bracedY + 20 && !(bool)Get(pet, "IsTossing"), "Near miss did not sag before regripping");
                    Call(pet, "AdvanceClimb", now + climbTime + 850 + 1851);
                    Check((IntPtr)Get(pet, "hwndWindow") == window.Handle, "Near miss did not recover");
                }
                else
                {
                    Call(pet, "AdvanceClimb", now + climbTime * (.5 + .85 / legs));
                    Check((bool)Get(pet, "IsTossing") && (IntPtr)Get(pet, "hwndWindow") == IntPtr.Zero, "A failed wall jump did not fall");
                    Set(pet, "IsTossing", false);
                }
            }
            for (int roll = 0; roll < 100; roll++)
            {
                string result = Call(pet, "JumpOutcome", other.Handle, roll).ToString();
                Check(result == (roll < 80 ? "Success" : roll < 90 ? "Catch" : "Slip"), "Jump result did not follow 80/10/10 rates");
            }

            other.Bounds = new Rectangle(visible.Left - 100, visible.Top + 32, 165, 180);
            InFront(other);
            Set(pet, "layersCheckedAt", -100.0);
            Place(pet, visible.Left - pet.Width, area.Bottom - pet.Height);
            Check(!(bool)Call(pet, "StartWindowClimb", now, window.Handle, 0), "Climb used an edge hidden behind another window");
            other.Hide();
        }
        Place(pet, visible.Left - pet.Width - 30, area.Bottom - pet.Height);
        Set(pet, "layersCheckedAt", -100.0);
        Check(!(bool)Call(pet, "StartWindowClimb", 0.0, window.Handle, 0), "Climb grabbed the edge from a distance");
        window.Height -= 84;
        InFront(window);
        Set(pet, "layersCheckedAt", -100.0);
        int runway = (int)Math.Round(pet.Width * 1.55);
        Place(pet, visible.Left - pet.Width - runway, area.Bottom - pet.Height);
        double leapAt = ((Stopwatch)Get(pet, "motionClock")).Elapsed.TotalMilliseconds;
        Check((bool)Call(pet, "StartWindowClimb", leapAt, window.Handle, 0), "Pet rejected a reachable elevated window edge");
        double grabAt = (double)Get(pet, "climbReachAt");
        int beforeRun = pet.Left;
        Call(pet, "AdvanceClimb", leapAt + 440);
        Check(pet.Left > beforeRun + 26 && pet.Bottom == area.Bottom, "Pet did not take a grounded running start");
        Call(pet, "AdvanceClimb", leapAt + 640);
        Check(pet.Right < visible.Left && pet.Bottom < area.Bottom && (int)Get(Get(pet, "CurrentAnimation"), "ID") != 14,
            "The jump climbed in midair instead of approaching the ledge");
        Call(pet, "AdvanceClimb", grabAt + 1);
        Check(pet.Top + pet.Height * .58 < VisibleFrame(pet, window).Bottom && (int)Get(Get(pet, "CurrentAnimation"), "ID") == 14,
            "The reach hop did not make contact before climbing");
        Set(pet, "climbWindow", IntPtr.Zero);
        window.Height -= 120;
        InFront(window);
        Set(pet, "layersCheckedAt", -100.0);
        Place(pet, visible.Left - pet.Width - (int)Math.Round(pet.Width * .20), area.Bottom - pet.Height);
        double castAt = ((Stopwatch)Get(pet, "motionClock")).Elapsed.TotalMilliseconds;
        Check((bool)Call(pet, "StartWindowClimb", castAt, window.Handle, 0), "Pet could not use a hook for a high window");
        var rope = (Form)Get(pet, "climbRope");
        Call(pet, "AdvanceClimb", castAt + 625);
        Check(rope != null && rope.Visible && pet.Bottom == area.Bottom, "Hook was not thrown before climbing");
        int inFlight = rope.Top;
        Call(pet, "AdvanceClimb", castAt + 1100);
        Check(rope.Top < inFlight && pet.Bottom == area.Bottom, "Hook did not fly to the edge and take tension");
        Call(pet, "AdvanceClimb", castAt + 1151);
        Call(pet, "AdvanceClimb", castAt + 2650);
        Check(pet.Bottom < area.Bottom && rope.Visible, "Pet did not begin climbing a taut rope");
        window.Hide();
        Call(pet, "AdvanceClimb", castAt + 2660);
        Check(rope.IsDisposed && (bool)Get(pet, "IsTossing"), "The rope did not release when its anchor disappeared");
        Set(pet, "tossUpdatedAt", ((Stopwatch)Get(pet, "motionClock")).Elapsed.TotalMilliseconds);
        Check(WaitUntil(() => !(bool)Get(pet, "IsTossing"), 1600), "Pet did not land after the rope released");
        Console.WriteLine("PASS tall window: slow step, occlusion, wall jumps, run-up and grab, throwing and releasing the hook");
    }

    static void TestWalkUnder(Form pet, Form window, Rectangle area)
    {
        window.Bounds = new Rectangle(area.Left + area.Width / 2, area.Bottom - 300, 190, 290);
        window.Show();
        InFront(window);
        Rectangle visible = VisibleFrame(pet, window);
        Set(pet, "hwndWindow", IntPtr.Zero);
        Set(pet, "nextCursorReactionAt", double.MaxValue);
        Set(pet, "nextRoamAt", 0.0);
        Set(pet, "walkPastWindow", window.Handle);
        Set(pet, "walkPastLeft", false);
        Place(pet, visible.Left - pet.Width - 7, area.Bottom - pet.Height);
        Call(pet, "SetNewAnimation", 2);
        ((System.Windows.Forms.Timer)Get(pet, "timer1")).Start();
        Check(WaitUntil(() => pet.Right > visible.Left + 16, 1100) && pet.Bottom == area.Bottom &&
            (IntPtr)Get(pet, "hwndWindow") == IntPtr.Zero, "Pet ran into a window just above the taskbar");
        window.Hide();
        Set(pet, "walkPastWindow", IntPtr.Zero);
        Console.WriteLine("PASS taskbar path below a nearly touching window");
    }

    static void TestSkills(Form pet, Rectangle area, int display)
    {
        Set(pet, "DisplayIndex", display);
        Set(pet, "hwndWindow", IntPtr.Zero);
        Place(pet, area.Left + area.Width / 2, area.Bottom - pet.Height - 70);
        Call(pet, "SetNewAnimation", 1);
        var spriteTimer = (System.Windows.Forms.Timer)Get(pet, "timer1");
        var motionTimer = (System.Windows.Forms.Timer)Get(pet, "motionTimer");
        var pointer = new Point(pet.Left + 58, pet.Top - 140);
        Check((Point)Call(pet, "PointerDestination", pointer, pointer + new Size(100, 0), 7, area) == pointer + new Size(7, 0),
            "Lasso pull did not move the pointer toward the pet");
        Check((Point)Call(pet, "PointerDestination", pointer, pointer + new Size(100, 0), -26, area) == pointer + new Size(-26, 0),
            "The swat did not move the pointer away from the pet");
        Type skill = pet.GetType().GetNestedType("SproutSkill", BindingFlags.NonPublic);
        foreach (string name in new[] { "Fireworks", "Lasso", "Swing", "Portal" })
        {
            Call(pet, "BeginSproutSkill", Enum.Parse(skill, name), pointer);
            motionTimer.Stop();
            spriteTimer.Stop();
            double started = (double)Get(pet, "skillStartedAt");
            var effect = (Form)Get(pet, "skillEffect");
            Check(effect != null && effect.Visible, name + " did not create a visible effect");
            Point before = pet.Location;
            Call(pet, "AdvanceSproutSkill", started + (name == "Portal" ? 760 : 540), pointer);
            if (name == "Swing" || name == "Portal")
                Check(pet.Location != before, name + " did not move the pet");
            else
                Check(pet.Location == before, name + " unexpectedly moved the pet");
            if (name == "Portal")
                Check(pet.Location == (Point)Get(pet, "portalDestination") && ((Form)Get(pet, "exitEffect")).Visible,
                    "Portal exit did not match the pet's destination");
            Call(pet, "AdvanceSproutSkill", started + 2500, pointer);
            Check(effect.IsDisposed && Get(pet, "skillEffect") == null && Get(pet, "exitEffect") == null,
                name + " left effect windows open");
            if (name == "Swing")
            {
                Check((bool)Get(pet, "IsTossing"), "Swing did not release into flight");
                Set(pet, "IsTossing", false);
                motionTimer.Stop();
            }
        }
        Console.WriteLine("PASS fireworks, lasso, swing, teleport and effect cleanup");
    }

    static void TestSleep(Form pet, Rectangle area, int display)
    {
        uint inactive = 180000;
        Set(pet, "readSessionIdle", new Func<uint>(() => inactive));
        Set(pet, "DisplayIndex", display);
        Set(pet, "hwndWindow", IntPtr.Zero);
        Place(pet, area.Left + 12, area.Bottom - pet.Height);
        Call(pet, "SetNewAnimation", 2);
        Check((bool)Call(pet, "TrySproutAction") && (int)Get(Get(pet, "CurrentAnimation"), "ID") == 1,
            "Three minutes did not stop the pet from roaming");
        inactive = 599999;
        Check(!(bool)Call(pet, "TrySproutAction"), "Pet slept before ten minutes");
        inactive = 600000;
        Check((bool)Call(pet, "TrySproutAction") && (int)Get(Get(pet, "CurrentAnimation"), "ID") == 17,
            "Ten minutes did not put the pet to sleep");
        inactive = 0;
        Check((bool)Call(pet, "TrySproutAction") && (int)Get(Get(pet, "CurrentAnimation"), "ID") == 1,
            "Keyboard or mouse input did not wake the pet");
        Console.WriteLine("PASS three-minute quiet, ten-minute sleep and wake on input");
    }

    static void TestAgent(Form pet)
    {
        Check(Get(pet, "agentPipe") == null, "Agent interface was enabled by default");
        Call(pet, "StartAgentPipe");
        try
        {
            using (Process client = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--agent-status")
            {
                CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true
            }))
            {
                Check(WaitUntil(() => client.HasExited, 4500), "External Agent client did not exit");
                Check(client.ExitCode == 0 && client.StandardOutput.ReadToEnd().Contains("\"version\":1"),
                    "Agent status was not delivered to the external client");
            }
            Check(AgentRequest("sit").Contains("needs_window"), "Agent sit did not return its window requirement");
            Check(AgentRequest("capabilities").Contains("\"actions\":[\"sit\",\"fireworks\"]"), "Agent could not discover supported actions");
            string context = AgentRequest("context");
            Check(context.Contains("\"foreground\"") && context.Contains("\"pointer\"") && !context.Contains("title"),
                "Agent was not given the limited desktop context");
            Set(pet, "lastSpokenAt", DateTime.MinValue);
            Check(AgentRequest("say 你好呀").Contains("\"ok\":true") && (string)Get(pet, "speechText") == "你好呀",
                "Local agent could not display a UTF-8 message");
            Check(AgentRequest("say 第二句").Contains("cooldown"), "Speech cooldown did not prevent consecutive messages");
            Check(AgentRequest("move_pointer").Contains("unknown_command"), "Agent accepted a command outside its protocol");
            using (var stall = new NamedPipeClientStream(".", "DesktopPet.Sprout.v1", PipeDirection.InOut))
            {
                stall.Connect(3000);
                Check(AgentRequest("status").Contains("\"ok\":true"), "An incomplete request blocked later clients");
            }
            Set(pet, "skillAllowedAt", 0.0);
            Check(AgentRequest("fireworks").Contains("\"ok\":true") && Get(pet, "skillEffect") != null,
                "Agent fireworks did not trigger on the pet's UI thread");
        }
        finally { Call(pet, "StopAgentPipe"); }
        Check(Get(pet, "agentPipe") == null, "Agent server did not stop");
        Console.WriteLine("PASS local Agent status, commands, stalled client recovery and opt-in lifecycle");
    }

    static void TestGeminiTransport(Assembly app)
    {
        Type gemini = app.GetType("DesktopPet.SproutGemini", true);
        var history = Array.CreateInstance(gemini.GetNestedType("Turn", Members), 0);
        using (var transport = new HttpClient(new StubGemini()))
        {
            var task = (Task)gemini.GetMethod("AskThrough", Members).Invoke(null,
                new object[] { transport, "dummy-api-key", "好奇", history, "你好", CancellationToken.None, false, "过去聊过海星", "刚爬到窗顶" });
            task.GetAwaiter().GetResult();
            object reply = task.GetType().GetProperty("Result").GetValue(task);
            Check((string)Get(reply, "Text") == "你好呀！" && (string)Get(reply, "Action") == "fireworks",
                "Gemini result did not recover the text and action");
        }
        Console.WriteLine("PASS Gemini request signing and reply parsing with a stub HTTP response");
    }

    static int TestLiveGemini(Assembly app)
    {
        try
        {
            var keyStore = app.GetType("DesktopPet.SproutGeminiKey", true);
            string key = (string)keyStore.GetMethod("Load", Members).Invoke(null, null);
            if (string.IsNullOrEmpty(key)) { Console.WriteLine("Gemini key is not configured in the app"); return 3; }
            Type gemini = app.GetType("DesktopPet.SproutGemini", true);
            var task = (Task)gemini.GetMethod("Observe", Members).Invoke(null,
                new object[] { key, "好奇", "这是连通测试。可保持安静。", CancellationToken.None });
            task.GetAwaiter().GetResult();
            Console.WriteLine("PASS live Gemini response through port 7890");
            return 0;
        }
        catch (Exception error)
        {
            Type kind = error.GetType();
            object status = kind.GetProperty("Status", Members)?.GetValue(error);
            Console.WriteLine("Gemini smoke test failed: {0} {1}", kind.Name, status);
            return 1;
        }
    }

    static void TestAutonomyBudget(Assembly app)
    {
        var budget = app.GetType("DesktopPet.SproutAutoSpeech", true);
        var canRequest = budget.GetMethod("CanRequest", Members);
        var record = budget.GetMethod("Record", Members);
        DateTime start = DateTime.UtcNow;
        record.Invoke(null, new object[] { start });
        Check(!(bool)canRequest.Invoke(null, new object[] { start.AddSeconds(44) }) &&
            (bool)canRequest.Invoke(null, new object[] { start.AddSeconds(45) }), "Autonomous requests did not respect the pacing window");
        budget.GetMethod("RateLimited", Members).Invoke(null, new object[] { start.AddSeconds(1), TimeSpan.FromMinutes(2) });
        Check(!(bool)canRequest.Invoke(null, new object[] { start.AddSeconds(120) }) &&
            (bool)canRequest.Invoke(null, new object[] { start.AddSeconds(121) }), "Retry-After was not honored");
        Console.WriteLine("PASS autonomous pacing and Gemini Retry-After");
    }

    static void TestMemory(Assembly app)
    {
        Type type = app.GetType("DesktopPet.SproutMemory", true);
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "event-fixture-" + Guid.NewGuid().ToString("N") + ".dat");
        object memory = Activator.CreateInstance(type, Members, null, new object[] { path }, null);
        type.GetProperty("Local", Members).SetValue(null, memory);
        try
        {
            Call(memory, "AddChat", "user", "我想用原创蜜桃海星素材");
            for (int i = 0; i < 12; i++) Call(memory, "AddChat", "model", "消息 " + i);
            Call(memory, "AddEvent", "小芽成功爬到了窗顶");
            object restored = Activator.CreateInstance(type, Members, null, new object[] { path }, null);
            Check(((IList)type.GetProperty("Chat", Members).GetValue(restored)).Count == 13 &&
                ((string)Call(restored, "Recall", "还记得蜜桃海星素材吗")).Contains("原创蜜桃海星") &&
                ((string)Call(restored, "EventContext")).Contains("爬到了窗顶"), "Chat or event memory did not survive reopening");
            Check(!Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("蜜桃海星"), "Chat memory was stored as plaintext");
            var gemini = app.GetType("DesktopPet.SproutGemini", true);
            var intent = gemini.GetMethod("IsAssetSearch", Members);
            Check((bool)intent.Invoke(null, new object[] { "那你帮我找一下", "想给你换个素材，网上有开源的吗？" }) &&
                !(bool)intent.Invoke(null, new object[] { "帮我查文件", "我刚才放了烟花" }), "Asset search routing did not understand the follow-up");
            Call(restored, "Clear");
            Check(!File.Exists(path), "Clearing memories did not delete local storage");
        }
        finally { Call(memory, "Clear"); }
        Console.WriteLine("PASS protected local history, relevant recall, event memory and asset search routing");
    }

    sealed class StubGemini : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string payload = await request.Content.ReadAsStringAsync();
            Check(request.Method == HttpMethod.Post && request.RequestUri.Host == "generativelanguage.googleapis.com" &&
                request.RequestUri.AbsolutePath.EndsWith("gemini-3.5-flash-lite:generateContent") &&
                request.Headers.Contains("x-goog-api-key") && !payload.Contains("dummy-api-key") &&
                payload.Contains("你好") && payload.Contains("responseSchema") && payload.Contains("过去聊过海星") &&
                payload.Contains("刚爬到窗顶") && payload.Contains("不要复述问题"), "Gemini request was malformed");
            string response = "{\"candidates\":[{\"content\":{\"parts\":[{\"thought\":true,\"text\":\"discard\"}," +
                "{\"text\":\"{\\\"text\\\":\\\"你好呀！\\\",\\\"action\\\":\\\"fireworks\\\"}\"}]}}]}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }

    static string AgentRequest(string command)
    {
        var result = Task.Run(() => SendAgentCommand(command));
        Check(WaitUntil(() => result.IsCompleted, 4500), "Agent request timed out: " + command);
        return result.GetAwaiter().GetResult() ?? "";
    }

    static string SendAgentCommand(string command)
    {
        using (var client = new NamedPipeClientStream(".", "DesktopPet.Sprout.v1", PipeDirection.InOut))
        {
            client.Connect(3500);
            using (var reader = new StreamReader(client))
            using (var writer = new StreamWriter(client) { AutoFlush = true })
            {
                writer.WriteLine(command);
                return reader.ReadLine();
            }
        }
    }

    static Rectangle VisibleFrame(Form pet, Form window)
    {
        object[] arguments = { window.Handle, null };
        Check((bool)Call(pet, "TryGetPetWindowRect", arguments), "Could not read visible window bounds");
        object rect = arguments[1];
        return Rectangle.FromLTRB((int)Get(rect, "Left"), (int)Get(rect, "Top"), (int)Get(rect, "Right"), (int)Get(rect, "Bottom"));
    }

    static void Place(Form pet, int x, int y)
    {
        pet.Location = new Point(x, y);
        Set(pet, "PositionX", (double)x);
        Set(pet, "PositionY", (double)y);
        Call(pet, "ResetMotion");
    }

    static bool WaitUntil(Func<bool> condition, int timeoutMilliseconds)
    {
        var clock = Stopwatch.StartNew();
        do
        {
            Application.DoEvents();
            if (condition()) return true;
            Thread.Sleep(5);
        } while (clock.ElapsedMilliseconds < timeoutMilliseconds);
        return false;
    }

    static object Get(object instance, string name) => instance.GetType().GetField(name, Members).GetValue(instance);
    static void Set(object instance, string name, object value) => instance.GetType().GetField(name, Members).SetValue(instance, value);
    static object Call(object instance, string name, params object[] args) => instance.GetType().GetMethod(name, Members).Invoke(instance, args);
    static void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
    }
}
