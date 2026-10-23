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

namespace ShareX.ScreenCaptureLib
{
    internal class ResizeNode : ImageEditorControl
    {
        public const int DefaultSize = 13;

        private PointF position;

        public PointF Position
        {
            get
            {
                return position;
            }
            set
            {
                position = value;

                Rectangle = new RectangleF(position.X - ((Size - 1) / 2), position.Y - ((Size - 1) / 2), Size, Size);
            }
        }

        public int Size { get; set; }

        public bool AutoSetSize { get; set; } = true;
        internal bool IsCropHandle;
        internal NodePosition CropPosition;
        internal float CropScale;
        internal Color CropAccent;

        private NodeShape shape;

        public NodeShape Shape
        {
            get
            {
                return shape;
            }
            set
            {
                shape = value;

                if (AutoSetSize)
                {
                    if (shape == NodeShape.CustomNode && CustomNodeImage != null)
                    {
                        Size = Math.Max(CustomNodeImage.Width, CustomNodeImage.Height);
                    }
                    else
                    {
                        Size = DefaultSize;
                    }
                }
            }
        }

        public Image CustomNodeImage { get; private set; }

        public ResizeNode(float x = 0, float y = 0)
        {
            Shape = NodeShape.Square;
            Position = new PointF(x, y);
        }

        public void SetCustomNode(Image customNodeImage)
        {
            CustomNodeImage = customNodeImage;
            Shape = NodeShape.CustomNode;
        }

        public override void OnDraw(Graphics g)
        {
            if (IsCropHandle)
            {
                GraphicsState state = g.Save();
                g.SmoothingMode = SmoothingMode.AntiAlias;
                bool horizontal = CropPosition == NodePosition.Top || CropPosition == NodePosition.Bottom;
                bool vertical = CropPosition == NodePosition.Left || CropPosition == NodePosition.Right;
                SizeF size = horizontal ? new SizeF(22 * CropScale, 5 * CropScale) : vertical ?
                    new SizeF(5 * CropScale, 22 * CropScale) : new SizeF(9 * CropScale, 9 * CropScale);
                RectangleF handle = new RectangleF(Position.X - size.Width / 2, Position.Y - size.Height / 2, size.Width, size.Height);
                bool active = IsDragging || IsCursorHover;
                using (Brush fill = new SolidBrush(active ? CropAccent : Color.White))
                using (Pen outline = new Pen(active ? Color.White : CropAccent, CropScale))
                    g.DrawRoundedRectangle(fill, outline, handle, 2 * CropScale);
                g.Restore(state);
                return;
            }
            RectangleF rect = Rectangle.SizeOffset(-1);

            switch (Shape)
            {
                case NodeShape.Square:
                    g.DrawRectangle(Pens.White, rect.Round().Offset(-1));
                    g.DrawRectangle(Pens.Black, rect.Round());
                    break;
                default:
                case NodeShape.Circle:
                    g.DrawEllipse(Pens.White, rect.Offset(-1));
                    g.DrawEllipse(Pens.Black, rect);
                    break;
                case NodeShape.Diamond:
                    g.DrawDiamond(Pens.White, rect.Round().Offset(-1));
                    g.DrawDiamond(Pens.Black, rect.Round());
                    break;
                case NodeShape.CustomNode when CustomNodeImage != null:
                    g.DrawImage(CustomNodeImage, Rectangle);
                    break;
            }
        }
    }
}
