using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KebuzLoncher
{
    // Keep the native sizable/caption styles: Windows still owns Snap, animations,
    // the system menu and the resize borders. Only the caption's client inset changes.
    public class AeroForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool IsZoomed(IntPtr handle);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left, Top, Right, Bottom;
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCCALCSIZE = 0x0083;
            const int WM_NCHITTEST = 0x0084;

            if (m.Msg == WM_NCCALCSIZE)
            {
                // Both message forms start with the proposed window rectangle.
                var window = (NativeRect)Marshal.PtrToStructure(m.LParam, typeof(NativeRect));
                base.WndProc(ref m);
                var client = (NativeRect)Marshal.PtrToStructure(m.LParam, typeof(NativeRect));
                client.Top = IsZoomed(m.HWnd)
                    ? Math.Max(window.Top, Screen.FromRectangle(Rectangle.FromLTRB(window.Left, window.Top, window.Right, window.Bottom)).WorkingArea.Top)
                    : window.Top;
                Marshal.StructureToPtr(client, m.LParam, false);
                m.Result = IntPtr.Zero;
                return;
            }

            base.WndProc(ref m);
            if (m.Msg == WM_NCHITTEST && !IsZoomed(m.HWnd))
            {
                long packed = m.LParam.ToInt64();
                var point = PointToClient(new Point(unchecked((short)packed), unchecked((short)(packed >> 16))));
                int grip = Math.Max(4, SystemInformation.FrameBorderSize.Height);
                if (point.Y >= 0 && point.Y < grip)
                {
                    int hit = point.X < grip ? 13 : point.X >= ClientSize.Width - grip ? 14 : 12;
                    m.Result = (IntPtr)hit; // HTTOPLEFT / HTTOPRIGHT / HTTOP
                }
            }
        }
    }
}
