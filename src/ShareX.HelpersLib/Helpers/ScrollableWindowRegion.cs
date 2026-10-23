// SnapFlow, GPL-3.0-or-later. Finds the actual browser document under a capture selection.
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace ShareX.HelpersLib
{
    public static class ScrollableWindowRegion
    {
        public static Rectangle? FindBrowserDocument(IntPtr handle, Point point, Rectangle selection)
        {
            string windowClass = new WindowInfo(handle).ClassName;
            if (windowClass == null || (!windowClass.StartsWith("Chrome_WidgetWin", StringComparison.Ordinal) && windowClass != "MozillaWindowClass"))
                return null;

            try
            {
                AutomationElement root = AutomationElement.FromHandle(handle);
                var condition = new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document),
                    new PropertyCondition(AutomationElement.IsScrollPatternAvailableProperty, true));
                Rectangle? best = null;
                foreach (AutomationElement element in root.FindAll(TreeScope.Descendants, condition))
                {
                    if (element.Current.IsOffscreen || !element.TryGetCurrentPattern(ScrollPattern.Pattern, out object value) ||
                        !((ScrollPattern)value).Current.VerticallyScrollable) continue;

                    var bounds = element.Current.BoundingRectangle;
                    if (bounds.IsEmpty || !double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) ||
                        !double.IsFinite(bounds.Right) || !double.IsFinite(bounds.Bottom)) continue;
                    Rectangle rectangle = Rectangle.FromLTRB((int)Math.Ceiling(bounds.Left), (int)Math.Ceiling(bounds.Top),
                        (int)Math.Floor(bounds.Right), (int)Math.Floor(bounds.Bottom));
                    if (!rectangle.Contains(point)) continue;
                    rectangle = Rectangle.Intersect(rectangle, selection);
                    if (rectangle.Width < 64 || rectangle.Height < 64) continue;
                    if (best == null || (long)rectangle.Width * rectangle.Height < (long)best.Value.Width * best.Value.Height)
                        best = rectangle;
                }
                return best;
            }
            catch (Exception ex) when (ex is ElementNotAvailableException || ex is COMException || ex is InvalidOperationException || ex is ArgumentException)
            {
                return null; // Preserve native/manual selection when accessibility is unavailable.
            }
        }
    }
}
