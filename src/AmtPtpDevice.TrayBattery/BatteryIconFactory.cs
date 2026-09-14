using System.Runtime.InteropServices;

namespace AmtPtpDevice.TrayBattery
{
    /// <summary>
    /// Renders the battery percentage as text onto a small bitmap and turns it into a
    /// tray icon, since NotifyIcon has no built-in way to show a number.
    /// </summary>
    internal static class BatteryIconFactory
    {
        private const int Size = 32;
        private const int CornerRadius = 4;

        public static Icon CreatePercentIcon(int percent) => Render(Math.Clamp(percent, 0, 100).ToString());

        public static Icon CreateUnknownIcon() => Render("--");

        private static Icon Render(string text)
        {
            using var bitmap = new Bitmap(Size, Size);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                g.Clear(Color.Transparent);

                using var backgroundBrush = new SolidBrush(Color.Black);
                using var path = RoundedRect(0, 0, Size, Size, CornerRadius);
                g.FillPath(backgroundBrush, path);

                var fontSize = text.Length > 2 ? 17f : 24f;
                using var format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                using var textBrush = new SolidBrush(Color.White);
                var bounds = new RectangleF(-1, 0, Size + 2, Size);

                // Shrink the font until the text fits the icon's width, so two- and
                // three-digit percentages both fill as much of the square as possible.
                using var font = FitFont(g, text, fontSize, bounds.Width);
                g.DrawString(text, font, textBrush, bounds, format);
            }

            var hIcon = bitmap.GetHicon();
            try
            {
                using var temp = Icon.FromHandle(hIcon);
                return (Icon)temp.Clone();
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }

        private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(float x, float y, float width, float height, float radius)
        {
            var diameter = radius * 2;
            var path = new System.Drawing.Drawing2D.GraphicsPath();

            path.AddArc(x, y, diameter, diameter, 180, 90);
            path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
            path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
            path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();

            return path;
        }

        private static Font FitFont(Graphics g, string text, float startSize, float maxWidth)
        {
            var size = startSize;
            while (size > 8f)
            {
                var font = new Font("Tahoma", size, FontStyle.Bold, GraphicsUnit.Pixel);
                if (g.MeasureString(text, font).Width <= maxWidth) return font;
                font.Dispose();
                size -= 1f;
            }

            return new Font("Tahoma", 8f, FontStyle.Bold, GraphicsUnit.Pixel);
        }

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);
    }
}
