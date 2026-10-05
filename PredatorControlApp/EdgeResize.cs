using System.Runtime.Versioning;

namespace PredatorControlApp
{
    /// <summary>
    /// Implemented by the borderless main window so child panels that cover
    /// its edges can hand those edge pixels back to it for resize hit-testing.
    /// </summary>
    internal interface IEdgeResizable
    {
        /// <summary>Returns an HT* sizing code for a screen point on the window's resize border, or 0.</summary>
        int EdgeHit(Point screenPoint);
    }

    internal static class HitCodes
    {
        public const int HTTRANSPARENT = -1;
        public const int HTCLIENT = 1;
        public const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13,
                         HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

        public const int WM_NCCALCSIZE = 0x0083;
        public const int WM_NCPAINT = 0x0085;
        public const int WM_NCHITTEST = 0x0084;

        public static Point ScreenPointFromLParam(IntPtr lParam)
        {
            long l = lParam.ToInt64();
            return new Point((short)(l & 0xFFFF), (short)((l >> 16) & 0xFFFF));
        }

        /// <summary>
        /// If the point is on the owning form's resize border, makes this
        /// child report HTTRANSPARENT so the hit-test falls through to the
        /// form (which answers with the sizing code). Returns true if handled.
        /// </summary>
        public static bool PassEdgeToForm(Control c, ref Message m)
        {
            if (c.FindForm() is IEdgeResizable f && f.EdgeHit(ScreenPointFromLParam(m.LParam)) != 0)
            {
                m.Result = (IntPtr)HTTRANSPARENT;
                return true;
            }
            return false;
        }
    }

    /// <summary>Plain panel that lets the form's resize border show through it.</summary>
    [SupportedOSPlatform("windows")]
    internal class ChromePanel : Panel
    {
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == HitCodes.WM_NCHITTEST && HitCodes.PassEdgeToForm(this, ref m)) return;
            base.WndProc(ref m);
        }
    }
}
