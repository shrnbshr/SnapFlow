#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using ShareX.HelpersLib;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ShareX.ScreenCaptureLib
{
    public class CropTool : BaseTool
    {
        public override ShapeType ShapeType { get; } = ShapeType.ToolCrop;

        public override bool LimitRectangleToInsideCanvas { get; } = true;

        private ImageEditorButton confirmButton, cancelButton;
        private Size buttonSize = new Size(50, 40);
        private int buttonOffset = 15;
        private int[] originalNodeSizes;

        public override void OnUpdate()
        {
            base.OnUpdate();
            if (Options.UseEditorChrome)
            {
                for (int i = 0; i < 8; i++)
                {
                    ResizeNode node = Manager.ResizeNodes[i];
                    if (node.Visible && (node.IsCursorHover || node.IsDragging))
                    {
                        Manager.Form.Cursor = i == 1 || i == 5 ? Cursors.SizeNS : i == 3 || i == 7 ? Cursors.SizeWE :
                            i == 0 || i == 4 ? Cursors.SizeNWSE : Cursors.SizeNESW;
                        break;
                    }
                }
            }

            if (confirmButton != null && cancelButton != null)
            {
                if (Options.ImageEditorCropFromEdges)
                {
                    float scale = Manager.Form.DeviceDpi / (96f * Manager.Form.ZoomFactor);
                    SizeF size = new SizeF(48 * scale, 36 * scale);
                    confirmButton.FontSize = cancelButton.FontSize = 14 / Manager.Form.ZoomFactor;
                    confirmButton.ButtonDepth = cancelButton.ButtonDepth = (int)System.Math.Ceiling(3 * scale);
                    float gap = 8 * scale;
                    RectangleF viewport = new RectangleF(gap, Manager.Form.ToolbarHeight / Manager.Form.ZoomFactor + gap,
                        Manager.Form.ClientArea.Width / Manager.Form.ZoomFactor - gap * 2,
                        (Manager.Form.ClientArea.Height - Manager.Form.ToolbarHeight) / Manager.Form.ZoomFactor - gap * 2);
                    float totalWidth = size.Width * 2 + gap;
                    float x = System.Math.Max(viewport.Left, System.Math.Min(Rectangle.Right - totalWidth, viewport.Right - totalWidth));
                    float y = Rectangle.Bottom + gap;
                    if (y + size.Height > viewport.Bottom) y = Rectangle.Bottom - size.Height - gap;
                    y = System.Math.Max(viewport.Top, System.Math.Min(y, viewport.Bottom - size.Height));
                    confirmButton.Rectangle = new RectangleF(new PointF(x, y), size);
                    cancelButton.Rectangle = new RectangleF(new PointF(x + size.Width + gap, y), size);
                    return;
                }
                if (Rectangle.Bottom + buttonOffset + buttonSize.Height > Manager.Form.ClientArea.Bottom &&
                    Rectangle.Width > (buttonSize.Width * 2) + (buttonOffset * 3) &&
                    Rectangle.Height > buttonSize.Height + (buttonOffset * 2))
                {
                    confirmButton.Rectangle = new RectangleF(Rectangle.Right - (buttonOffset * 2) - (buttonSize.Width * 2),
                        Rectangle.Bottom - buttonOffset - buttonSize.Height, buttonSize.Width, buttonSize.Height);
                    cancelButton.Rectangle = new RectangleF(Rectangle.Right - buttonOffset - buttonSize.Width,
                        Rectangle.Bottom - buttonOffset - buttonSize.Height, buttonSize.Width, buttonSize.Height);
                }
                else
                {
                    confirmButton.Rectangle = new RectangleF(Rectangle.Right - (buttonSize.Width * 2) - buttonOffset,
                        Rectangle.Bottom + buttonOffset, buttonSize.Width, buttonSize.Height);
                    cancelButton.Rectangle = new RectangleF(Rectangle.Right - buttonSize.Width,
                        Rectangle.Bottom + buttonOffset, buttonSize.Width, buttonSize.Height);
                }
            }
        }

        public override void OnNodePositionUpdate()
        {
            if (Options.ImageEditorCropFromEdges)
            {
                if (originalNodeSizes == null)
                {
                    originalNodeSizes = new int[Manager.ResizeNodes.Length];
                    for (int i = 0; i < originalNodeSizes.Length; i++) originalNodeSizes[i] = Manager.ResizeNodes[i].Size;
                }
                int size = (int)System.Math.Ceiling((Options.UseEditorChrome ? 20 : 12) * Manager.Form.DeviceDpi / (96f * Manager.Form.ZoomFactor));
                foreach (ResizeNode node in Manager.ResizeNodes) node.Size = size;
            }
            base.OnNodePositionUpdate();
            if (Options.UseEditorChrome)
            {
                float scale = Manager.Form.DeviceDpi / (96f * Manager.Form.ZoomFactor);
                for (int i = 0; i < 8; i++)
                {
                    ResizeNode node = Manager.ResizeNodes[i];
                    node.IsCropHandle = true;
                    node.CropPosition = (NodePosition)i;
                    node.CropScale = scale;
                    node.CropAccent = ShareXResources.Theme.LinkColor;
                    bool horizontal = i == 1 || i == 5, vertical = i == 3 || i == 7;
                    SizeF hit = horizontal ? new SizeF(28 * scale, 20 * scale) :
                        vertical ? new SizeF(20 * scale, 28 * scale) : new SizeF(20 * scale, 20 * scale);
                    node.Rectangle = new RectangleF(node.Position.X - hit.Width / 2, node.Position.Y - hit.Height / 2, hit.Width, hit.Height);
                    // Keep the height controls usable on narrow, fitted scrolling screenshots.
                    node.Visible = horizontal || (vertical ? Rectangle.Width >= 24 * scale :
                        Rectangle.Width >= 48 * scale && Rectangle.Height >= 48 * scale);
                }
            }
        }

        public override void OnNodeVisible()
        {
            if (Options.UseEditorChrome) OnNodePositionUpdate();
            else base.OnNodeVisible();
        }

        public override void OnDraw(Graphics g)
        {
            if (IsValidShape)
            {
                if (Options.UseEditorChrome)
                {
                    DrawCropFrame(g);
                    return;
                }
                Manager.DrawRegionArea(g, Rectangle, !Options.UseEditorChrome, !Options.ImageEditorCropFromEdges && Manager.Options.ShowInfo);
                if (!Options.ImageEditorCropFromEdges)
                {
                    g.DrawCross(Pens.Black, Rectangle.Center().Add(-1, -1), 10);
                    g.DrawCross(Pens.White, Rectangle.Center(), 10);
                }
            }
        }

        private void DrawCropFrame(Graphics g)
        {
            float scale = Manager.Form.DeviceDpi / (96f * Manager.Form.ZoomFactor);
            GraphicsState state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Region excluded = new Region(Manager.Form.CanvasRectangle))
            using (Brush shade = new SolidBrush(Color.FromArgb(110, Color.Black)))
            {
                excluded.Exclude(Rectangle);
                g.FillRegion(shade, excluded);
            }
            using (Pen boundary = new Pen(ShareXResources.Theme.LinkColor, 1.2f * scale))
                g.DrawRectangleProper(boundary, Rectangle);
            if (Manager.IsResizing)
            {
                using (Pen grid = new Pen(Color.FromArgb(135, Color.White), scale * .75f))
                    for (int i = 1; i <= 2; i++)
                    {
                        float x = Rectangle.Left + Rectangle.Width * i / 3;
                        float y = Rectangle.Top + Rectangle.Height * i / 3;
                        g.DrawLine(grid, x, Rectangle.Top, x, Rectangle.Bottom);
                        g.DrawLine(grid, Rectangle.Left, y, Rectangle.Right, y);
                    }
            }
            g.Restore(state);
        }

        public override void OnCreated()
        {
            if (Options.UseEditorChrome) return;
            confirmButton = new ImageEditorButton()
            {
                Text = "\u2714",
                ButtonColor = ShareXResources.Theme.LightBackgroundColor,
                IconColor = Color.ForestGreen,
                Rectangle = new Rectangle(new Point(), buttonSize),
                Visible = true
            };
            confirmButton.MouseDown += ConfirmButton_MousePressed;
            confirmButton.MouseEnter += () => Manager.Form.Cursor = Cursors.Hand;
            confirmButton.MouseLeave += () => Manager.Form.SetDefaultCursor();
            Manager.DrawableObjects.Add(confirmButton);

            cancelButton = new ImageEditorButton()
            {
                Text = "\u2716",
                ButtonColor = ShareXResources.Theme.LightBackgroundColor,
                IconColor = Color.FromArgb(227, 45, 45),
                Rectangle = new Rectangle(new Point(), buttonSize),
                Visible = true
            };
            cancelButton.MouseDown += CancelButton_MousePressed;
            cancelButton.MouseEnter += () => Manager.Form.Cursor = Cursors.Hand;
            cancelButton.MouseLeave += () => Manager.Form.SetDefaultCursor();
            Manager.DrawableObjects.Add(cancelButton);
        }

        private void ConfirmButton_MousePressed(object sender, MouseEventArgs e)
        {
            Confirm();
        }

        internal bool Confirm()
        {
            if (!IsValidShape) return false;
            Manager.CropArea(Rectangle);
            Remove();
            return true;
        }

        private void CancelButton_MousePressed(object sender, MouseEventArgs e)
        {
            Remove();
        }

        public override void Remove()
        {
            base.Remove();

            if (Options.ImageEditorCropFromEdges || Options.SwitchToSelectionToolAfterDrawing)
            {
                Manager.CurrentTool = ShapeType.ToolSelect;
            }
        }

        public override void Dispose()
        {
            base.Dispose();
            if (originalNodeSizes != null)
            {
                for (int i = 0; i < originalNodeSizes.Length; i++) Manager.ResizeNodes[i].Size = originalNodeSizes[i];
                originalNodeSizes = null;
            }
            if (Options.UseEditorChrome)
            {
                foreach (ResizeNode node in Manager.ResizeNodes) node.IsCropHandle = false;
                Manager.Form.SetDefaultCursor();
            }

            if ((confirmButton != null && confirmButton.IsCursorHover) || (cancelButton != null && cancelButton.IsCursorHover))
            {
                Manager.Form.SetDefaultCursor();
            }

            if (confirmButton != null)
            {
                Manager.DrawableObjects.Remove(confirmButton);
            }

            if (cancelButton != null)
            {
                Manager.DrawableObjects.Remove(cancelButton);
            }
        }
    }
}
