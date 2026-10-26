using ShareX.HelpersLib;
using ShareX.Roll;
using ShareX.ScreenCaptureLib;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class EditorOptionsRegression
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll")] private static extern bool LogicalToPhysicalPointForPerMonitorDPI(nint window, ref Point point);
    private static int checks;
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        checks++;
        Console.WriteLine("PASS: " + message);
    }
    private static void ExitMenu(ToolStripDropDown menu)
    {
        menu.AutoClose = true;
        typeof(ToolStripDropDown).GetMethod("ProcessDialogKey", Members)!.Invoke(menu, [Keys.Menu]);
        menu.Close();
    }

    private static Rectangle PhysicalBounds(Control control)
    {
        Point start = control.PointToScreen(Point.Empty), end = control.PointToScreen(new Point(control.ClientSize));
        if (!LogicalToPhysicalPointForPerMonitorDPI(control.Handle, ref start) ||
            !LogicalToPhysicalPointForPerMonitorDPI(control.Handle, ref end)) throw new InvalidOperationException("Cannot map field coordinates");
        return Rectangle.FromLTRB(start.X, start.Y, end.X, end.Y);
    }

    internal static async Task RunAsync(MainForm main)
    {
        string output = Path.GetFullPath("artifacts/editor-options-polish/" + main.DeviceDpi);
        Directory.CreateDirectory(output);
        bool motion = UiMotion.UserEnabled;
        try
        {
            UiMotion.UserEnabled = false;
            foreach (CapturePalette palette in new[] { CapturePalette.Light, CapturePalette.Dark })
            foreach (bool transparent in new[] { false, true })
            {
                string theme = palette == CapturePalette.Light ? "light" : "dark";
                palette.ApplyLibraryTheme();
                using Bitmap image = new(600, 360);
                using (Graphics g = Graphics.FromImage(image)) { g.Clear(Color.White); g.FillRectangle(Brushes.CornflowerBlue, 80, 60, 440, 200); }
                using RegionCaptureForm editor = ScreenshotEditor.Create(image, palette, new() { TransparencyEnabled = transparent });
                editor.TopMost = true;
                editor.Show(main);
                editor.ForceActivate();
                await Task.Delay(500);
                ToolStrip tools = EditorRegression.Tools(editor);
                tools.Items["editorCancelCrop"]!.PerformClick();
                tools.PerformLayout();
                await Task.Delay(100);
                ToolStripDropDownItem options = (ToolStripDropDownItem)tools.Items["editorStyle"]!;
                foreach (ShapeType shape in new[] { ShapeType.DrawingFreehand, ShapeType.DrawingRectangle, ShapeType.DrawingArrow,
                    ShapeType.DrawingTextOutline, ShapeType.DrawingStep, ShapeType.EffectBlur, ShapeType.EffectPixelate, ShapeType.DrawingMagnify, ShapeType.ToolSpotlight })
                {
                    tools.Items.Cast<ToolStripItem>().Single(item => Equals(item.Tag, shape)).PerformClick();
                    tools.OverflowButton.ShowDropDown();
                    tools.OverflowButton.DropDown.AutoClose = false;
                    options.ShowDropDown();
                    ToolStripDropDown popup = options.DropDown;
                    popup.AutoClose = false;
                    await Task.Delay(100);
                    Check(popup.Visible, theme + "/" + shape + ": tool options remain reachable");
                    foreach (ToolStripControlHost host in popup.Items.OfType<ToolStripControlHost>().Where(item => item.Available))
                    {
                        Control content = host.Control;
                        Label label = (Label)content.Controls.Find("lblText", true).Single();
                        Control row = content.Controls.Find("editorOptionRow", true).Single();
                        Check(label.Visible && !string.IsNullOrWhiteSpace(label.Text) && label.ForeColor.ToArgb() == palette.Text.ToArgb(),
                            shape + ": parameter labels stay visible and follow the current theme");
                        Check(row.ClientRectangle.Contains(label.Bounds) && popup.ClientRectangle.Contains(host.Bounds),
                            shape + ": label, field and outer whitespace fit inside the popup");
                        using Bitmap rowImage = new(content.Width, content.Height);
                        content.DrawToBitmap(rowImage, new Rectangle(Point.Empty, rowImage.Size));
                        int ink = 0;
                        for (int y = label.Top; y < label.Bottom; y++)
                        for (int x = label.Left; x < label.Right; x++)
                            if (rowImage.GetPixel(x, y).ToArgb() == palette.Text.ToArgb()) ink++;
                        Check(ink > 5, shape + ": rendered parameter name contains readable text");
                        if (transparent)
                        {
                            label.Refresh();
                            DwmFlush();
                            using Bitmap liveLabel = new Screenshot { RemoveOutsideScreenArea = false }.CaptureRectangle(PhysicalBounds(label));
                            int liveInk = 0;
                            for (int y = 0; y < liveLabel.Height; y++)
                            for (int x = 0; x < liveLabel.Width; x++)
                                if (liveLabel.GetPixel(x, y).ToArgb() == palette.Text.ToArgb()) liveInk++;
                            Check(liveInk > 5, shape + ": native glass composition preserves readable parameter labels");
                        }
                        if (host is ToolStripLabeledNumericUpDown number)
                        {
                            NumericUpDown input = content.Controls.Find("nudValue", true).OfType<NumericUpDown>().Single();
                            TextBox edit = input.Controls.OfType<TextBox>().Single();
                            Check(edit.Visible && !string.IsNullOrWhiteSpace(edit.Text) && edit.Width > 24 * editor.DeviceDpi / 96,
                                shape + ": the original numeric editor keeps its visible value and edit area");
                            if (transparent)
                            {
                                edit.Refresh();
                                DwmFlush();
                                using Bitmap liveValue = new Screenshot { RemoveOutsideScreenArea = false }.CaptureRectangle(PhysicalBounds(edit));
                                int valueInk = 0;
                                for (int y = 0; y < liveValue.Height; y++)
                                for (int x = 0; x < liveValue.Width; x++)
                                    if (liveValue.GetPixel(x, y).ToArgb() == palette.Text.ToArgb()) valueInk++;
                                Check(valueInk > 2, shape + ": native glass composition preserves numeric-value text");
                            }
                            if (shape == ShapeType.EffectPixelate)
                            {
                                decimal before = number.Content.Value;
                                number.Content.Value = before + number.Content.Increment;
                                Check(editor.Options.AnnotationOptions.PixelateSize == (int)number.Content.Value,
                                    "rounded number field still updates the original image-editor option");
                                number.Content.Value = before;
                            }
                        }
                        if (host is ToolStripLabeledComboBox combo)
                        {
                            EditorOptionChoice choice = content.Controls.Find("editorOptionChoice", true).OfType<EditorOptionChoice>().Single();
                            ComboBox original = content.Controls.Find("cbList", true).OfType<ComboBox>().Single();
                            if (original.Items.Count > 1)
                            {
                                int before = combo.Content.SelectedIndex;
                                if (shape == ShapeType.DrawingFreehand)
                                {
                                    popup.AutoClose = true;
                                    tools.OverflowButton.DropDown.AutoClose = true;
                                }
                                choice.PerformClick();
                                await Task.Delay(60);
                                ToolStripDropDown list = (ToolStripDropDown)typeof(EditorOptionChoice).GetField("menu", Members)!.GetValue(choice)!;
                                Check(list.Visible && list.Items.Count == original.Items.Count,
                                    shape + ": rounded choices retain every original option");
                                Check(popup.Visible, "opening a field selector retains its parent tool-options menu");
                                popup.AutoClose = false;
                                tools.OverflowButton.DropDown.AutoClose = false;
                                list.AutoClose = false;
                                int next = (before + 1) % original.Items.Count;
                                list.Items[next].PerformClick();
                                Check(combo.Content.SelectedIndex == next && choice.Text == original.GetItemText(original.Items[next]),
                                    shape + ": choosing a value updates the original callback and displayed selection");
                                if (shape == ShapeType.DrawingFreehand)
                                    Check((int)editor.Options.AnnotationOptions.BorderStyle == next,
                                        "border-style choice updates the real annotation engine");
                                ExitMenu(list);
                                combo.Content.SelectedIndex = before;
                                Check(choice.Text == original.GetItemText(original.Items[before]),
                                    "programmatic option changes stay synchronized with the rounded selector");
                                if (shape == ShapeType.DrawingFreehand)
                                {
                                    combo.Content.SelectedIndex = 0;
                                    object[] keyArgs = [default(Message), Keys.Down];
                                    bool handled = (bool)typeof(EditorOptionChoice).GetMethod("ProcessCmdKey", Members)!.Invoke(choice, keyArgs)!;
                                    Check(handled && combo.Content.SelectedIndex == 1 && (int)editor.Options.AnnotationOptions.BorderStyle == 1,
                                        "arrow-key selection retains the native combo's option-update behavior");
                                    combo.Content.SelectedIndex = before;
                                }
                                if (!popup.Visible) options.ShowDropDown();
                                popup.AutoClose = false;
                                await Task.Delay(80);
                                popup.Refresh();
                                DwmFlush();
                            }
                        }
                    }
                    using (Bitmap frame = new(popup.Width, popup.Height))
                    {
                        popup.DrawToBitmap(frame, new Rectangle(Point.Empty, frame.Size));
                        frame.Save(Path.Combine(output, $"{theme}-{transparent}-{shape}.png"), ImageFormat.Png);
                    }
                    popup.Refresh();
                    await Task.Delay(100); // Let hosted child HWNDs finish their own WM_PAINT after the parent refresh.
                    DwmFlush();
                    using (Bitmap live = new Screenshot { RemoveOutsideScreenArea = false }.CaptureRectangle(PhysicalBounds(popup)))
                        live.Save(Path.Combine(output, $"{theme}-{transparent}-{shape}-live.png"), ImageFormat.Png);
                    ExitMenu(popup);
                    ExitMenu(tools.OverflowButton.DropDown);
                }
                using (Bitmap result = editor.GetResultImage())
                    Check(result.Size == image.Size && result.GetPixel(100, 100).ToArgb() == Color.CornflowerBlue.ToArgb(),
                        "field presentation and menu material preserve source-image pixels");
                typeof(RegionCaptureForm).GetMethod("CloseWindow", Members)!.Invoke(editor, [RegionResult.Close]);
                editor.Dispose();
                Check(options.DropDown.IsDisposed, "closing the editor disposes hosted controls and their choice menus");
            }
        }
        finally { UiMotion.UserEnabled = motion; CapturePalette.Current.ApplyLibraryTheme(); }
        Console.WriteLine($"Editor options: {checks} checks passed.");
    }
}
