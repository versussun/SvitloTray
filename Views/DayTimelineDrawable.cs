using SvitloTray.Core;

namespace SvitloTray.Views;

/// <summary>Day bar 00–24 with colored periods and a "now" marker.</summary>
public sealed class DayTimelineDrawable : IDrawable
{
    public IReadOnlyList<DaySegment> Segments { get; set; } = Array.Empty<DaySegment>();

    /// <summary>The "now" minute (only for the current day).</summary>
    public int? NowMinute { get; set; }

    public static Color ColorFor(PowerStatus status) => status switch
    {
        PowerStatus.On => Color.FromArgb("#22A046"),
        PowerStatus.Off => Color.FromArgb("#D63031"),
        _ => Color.FromArgb("#D69614"),
    };

    public void Draw(ICanvas canvas, RectF rect)
    {
        const float labelsHeight = 14;
        float top = rect.Top + 3;
        float height = Math.Max(8, rect.Height - labelsHeight - 6);
        float left = rect.Left + 1;
        float width = rect.Width - 2;

        if (Segments.Count == 0)
        {
            canvas.FillColor = Color.FromArgb("#9E9E9E");
            canvas.FillRoundedRectangle(left, top, width, height, 4);
        }
        else
        {
            foreach (var s in Segments)
            {
                float x = left + width * s.From / ScheduleCalculator.MinutesPerDay;
                float w = width * (s.To - s.From) / ScheduleCalculator.MinutesPerDay;
                canvas.FillColor = ColorFor(s.Status);
                canvas.FillRectangle(x, top, w, height);
            }
        }

        canvas.FontColor = Colors.Gray;
        canvas.FontSize = 10;
        for (int h = 0; h <= 24; h += 3)
        {
            float x = left + width * h / 24f;
            canvas.DrawString($"{h:00}", x - 12, top + height + 2, 24, labelsHeight,
                Microsoft.Maui.Graphics.HorizontalAlignment.Center,
                Microsoft.Maui.Graphics.VerticalAlignment.Top);
        }

        if (NowMinute is int now)
        {
            float x = left + width * now / ScheduleCalculator.MinutesPerDay;
            canvas.StrokeColor = Colors.White;
            canvas.StrokeSize = 4;
            canvas.DrawLine(x, top - 2, x, top + height + 2);
            canvas.StrokeColor = Colors.Black;
            canvas.StrokeSize = 2;
            canvas.DrawLine(x, top - 2, x, top + height + 2);
        }
    }
}
