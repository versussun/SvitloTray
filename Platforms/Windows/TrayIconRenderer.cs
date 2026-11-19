using System.Runtime.InteropServices;
using SvitloTray.Core;
using SD = System.Drawing;

namespace SvitloTray.WinUI;

/// <summary>
/// Draws the tray icon: a square colored by state
/// (green — power on, red — off, yellow — possible outage) and a number:
/// minutes until the state changes, or hours above 99 minutes ("3h").
/// </summary>
internal static class TrayIconRenderer
{
    const int SM_CXSMICON = 49;

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int index);

    /// <returns>HICON — release with DestroyIcon (NativeTrayIcon does it).</returns>
    public static IntPtr Render(string text, PowerStatus? status)
    {
        int size = Math.Max(16, GetSystemMetrics(SM_CXSMICON));

        var background = status switch
        {
            PowerStatus.On => SD.Color.FromArgb(255, 34, 154, 70),
            PowerStatus.Off => SD.Color.FromArgb(255, 214, 48, 49),
            PowerStatus.Maybe => SD.Color.FromArgb(255, 214, 150, 20),
            _ => SD.Color.FromArgb(255, 110, 118, 129),
        };

        using var bmp = new SD.Bitmap(size, size, SD.Imaging.PixelFormat.Format32bppArgb);
        using (var g = SD.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = SD.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(SD.Color.Transparent);

            using (var path = RoundedRect(new SD.RectangleF(0, 0, size - 1, size - 1), size * 0.22f))
            using (var brush = new SD.SolidBrush(background))
            {
                g.FillPath(brush, path);
            }

            using var font = FitFont(g, text, size);
            using var format = new SD.StringFormat
            {
                Alignment = SD.StringAlignment.Center,
                LineAlignment = SD.StringAlignment.Center,
                FormatFlags = SD.StringFormatFlags.NoWrap,
            };
            using var white = new SD.SolidBrush(SD.Color.White);
            g.DrawString(text, font, white, new SD.RectangleF(-size, 0, size * 3f, size + size * 0.06f), format);
        }

        return bmp.GetHicon();
    }

    /// <summary>Largest bold font at which the text fits into the icon.</summary>
    static SD.Font FitFont(SD.Graphics g, string text, int size)
    {
        float px = text.Length <= 1 ? size * 0.85f : size * 0.72f;
        while (true)
        {
            var font = new SD.Font("Segoe UI", px, SD.FontStyle.Bold, SD.GraphicsUnit.Pixel);
            var measured = g.MeasureString(text, font, SD.PointF.Empty, SD.StringFormat.GenericTypographic);
            if (measured.Width <= size * 1.02f || px <= size * 0.35f) return font;
            font.Dispose();
            px -= 0.5f;
        }
    }

    static SD.Drawing2D.GraphicsPath RoundedRect(SD.RectangleF r, float radius)
    {
        float d = radius * 2;
        var p = new SD.Drawing2D.GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
