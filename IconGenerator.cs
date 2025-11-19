using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace SleepMngr
{
    public static class IconGenerator
    {
        public static Icon CreateColoredIcon(Color color)
        {
            int size = 32;
            using (Bitmap bitmap = new Bitmap(size, size))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                // Draw filled circle
                using (SolidBrush brush = new SolidBrush(color))
                {
                    g.FillEllipse(brush, 2, 2, size - 4, size - 4);
                }

                // Draw border
                using (Pen pen = new Pen(Color.FromArgb(200, Color.White), 2))
                {
                    g.DrawEllipse(pen, 2, 2, size - 4, size - 4);
                }

                // Draw moon symbol (for sleep allowed - blue icon)
                if (color.B > color.R) // Blue-ish color
                {
                    using (SolidBrush whiteBrush = new SolidBrush(Color.White))
                    {
                        // Crescent moon shape
                        g.FillEllipse(whiteBrush, 10, 8, 14, 14);
                        using (SolidBrush blueBrush = new SolidBrush(color))
                        {
                            g.FillEllipse(blueBrush, 14, 8, 14, 14);
                        }
                    }
                }
                else // Yellow-ish color - draw shield/lock symbol
                {
                    using (Pen whitePen = new Pen(Color.White, 2))
                    {
                        // Shield shape
                        Point[] points = new Point[]
                        {
                            new Point(16, 9),
                            new Point(12, 9),
                            new Point(12, 14),
                            new Point(10, 16),
                            new Point(10, 20),
                            new Point(16, 23),
                            new Point(22, 20),
                            new Point(22, 16),
                            new Point(20, 14),
                            new Point(20, 9),
                            new Point(16, 9)
                        };
                        g.DrawLines(whitePen, points);
                    }
                }

                IntPtr hIcon = bitmap.GetHicon();
                Icon icon = Icon.FromHandle(hIcon);
                return (Icon)icon.Clone();
            }
        }

        public static Icon CreateBlueIcon()
        {
            return CreateColoredIcon(Color.FromArgb(0, 120, 215)); // Windows blue - автоматический засыпать
        }

        public static Icon CreateYellowIcon()
        {
            return CreateColoredIcon(Color.FromArgb(255, 185, 0)); // Warning yellow - автоматический не засыпать
        }
        
        public static Icon CreateDarkBlueIcon()
        {
            return CreateColoredIcon(Color.FromArgb(0, 60, 110)); // Dark blue - ручной засыпать
        }
        
        public static Icon CreateDarkYellowIcon()
        {
            return CreateColoredIcon(Color.FromArgb(180, 130, 0)); // Dark yellow/orange - ручной не засыпать
        }
    }
}
