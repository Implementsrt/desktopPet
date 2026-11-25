using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;

namespace DesktopPet
{
    public partial class FormPet
    {
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
        private static extern int GetDwmFrame(IntPtr window, int attribute, out NativeMethods.RECT rect, int size);
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
        private static extern int GetDwmFlag(IntPtr window, int attribute, out int flag, int size);
        [DllImport("user32.dll")]
        private static extern bool PhysicalToLogicalPointForPerMonitorDPI(IntPtr window, ref Point point);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int ReadWindowStyle(IntPtr window, int index);

        private readonly List<KeyValuePair<IntPtr, Rectangle>> sproutLayers =
            new List<KeyValuePair<IntPtr, Rectangle>>();
        private double layersCheckedAt = -100;

        private bool TryGetPetWindowRect(IntPtr window, out NativeMethods.RECT rect)
        {
            if (IsSprout && GetDwmFrame(window, 9, out rect, 16) == 0 &&
                rect.Right > rect.Left && rect.Bottom > rect.Top)
            {
                Point leftTop = new Point(rect.Left, rect.Top), rightBottom = new Point(rect.Right, rect.Bottom);
                if (PhysicalToLogicalPointForPerMonitorDPI(Handle, ref leftTop) &&
                    PhysicalToLogicalPointForPerMonitorDPI(Handle, ref rightBottom))
                    rect = new NativeMethods.RECT { Left = leftTop.X, Top = leftTop.Y, Right = rightBottom.X, Bottom = rightBottom.Y };
                return true;
            }
            return NativeMethods.GetWindowRect(new HandleRef(this, window), out rect);
        }

        private bool IsVisibleStrip(IntPtr target, Rectangle strip)
        {
            double now = motionClock.Elapsed.TotalMilliseconds;
            if (now - layersCheckedAt >= 80)
            {
                layersCheckedAt = now;
                sproutLayers.Clear();
                NativeMethods.EnumWindows((window, unused) =>
                {
                    if (window != Handle && CanSupportPet(window) && (ReadWindowStyle(window, -20) & 0x20) == 0 &&
                        (GetDwmFlag(window, 14, out int cloaked, sizeof(int)) != 0 || cloaked == 0) &&
                        TryGetPetWindowRect(window, out NativeMethods.RECT bounds))
                        sproutLayers.Add(new KeyValuePair<IntPtr, Rectangle>(window,
                            Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom)));
                    return true;
                }, IntPtr.Zero);
            }
            foreach (var layer in sproutLayers)
            {
                if (layer.Key == target) return true;
                if (layer.Value.IntersectsWith(strip)) return false;
            }
            return false;
        }

        private bool ClimbEdgeVisible(IntPtr window, NativeMethods.RECT rect, int side, int handY)
        {
            int insideX = side < 0 ? rect.Left : rect.Right - 2;
            return IsVisibleStrip(window, Rectangle.FromLTRB(insideX, rect.Top, insideX + 2,
                Math.Max(rect.Top + 1, Math.Min(rect.Bottom, handY + Math.Max(8, Height / 6)))));
        }
    }
}
