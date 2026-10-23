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
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ShareX.ScreenCaptureLib
{
    internal class ScrollingCaptureManager : IDisposable
    {
        public ScrollingCaptureOptions Options { get; private set; }
        public Bitmap Result { get; private set; }
        public bool IsCapturing { get; private set; }
        public bool HasContentChanged { get; private set; }

        private Bitmap lastScreenshot;
        private Bitmap previousScreenshot;
        private bool stopRequested;
        private ScrollingCaptureStatus status;
        private int bestMatchCount, bestMatchIndex, bestIgnoreBottomOffset;
        private int comparisonSidePercent = 5;
        private WindowInfo selectedWindow;
        private Rectangle selectedRectangle;
        private Point scrollPoint;

        public ScrollingCaptureManager(ScrollingCaptureOptions options)
        {
            Options = options;
        }

        public void Dispose()
        {
            Reset();
        }

        private void Reset(bool keepResult = false)
        {
            if (lastScreenshot != null)
            {
                lastScreenshot.Dispose();
                lastScreenshot = null;
            }

            if (previousScreenshot != null)
            {
                previousScreenshot.Dispose();
                previousScreenshot = null;
            }

            if (!keepResult && Result != null)
            {
                Result.Dispose();
                Result = null;
            }
        }

        public async Task<ScrollingCaptureStatus> StartCapture()
        {
            if (!IsCapturing && selectedWindow != null && !selectedRectangle.IsEmpty)
            {
                IsCapturing = true;
                HasContentChanged = false;
                int unchangedAttempts = 0;
                stopRequested = false;
                status = ScrollingCaptureStatus.Failed;
                bestMatchCount = 0;
                bestMatchIndex = 0;
                bestIgnoreBottomOffset = 0;
                comparisonSidePercent = 5;
                Reset();

                ScrollingCaptureRegionForm regionForm = null;

                if (Options.ShowRegion)
                {
                    regionForm = new ScrollingCaptureRegionForm(selectedRectangle);
                    regionForm.Show();
                }

                try
                {
                    selectedWindow.Activate();

                    await Task.Delay(Options.StartDelay);

                    if (Options.AutoScrollTop)
                    {
                        InputHelpers.SendKeyPress(VirtualKeyCode.HOME);
                        NativeMethods.SendMessage(selectedWindow.Handle, (int)WindowsMessages.VSCROLL, (int)ScrollBarCommands.SB_TOP, 0);

                        await Task.Delay(Options.ScrollDelay);
                    }

                    Screenshot screenshot = new Screenshot()
                    {
                        CaptureCursor = false
                    };

                    while (!stopRequested)
                    {
                        lastScreenshot = screenshot.CaptureRectangle(selectedRectangle);

                        if (CompareLastTwoImages())
                        {
                            // A missed wheel event or delayed repaint is not proof that we reached the bottom.
                            lastScreenshot.Dispose();
                            lastScreenshot = null;
                            if (++unchangedAttempts < 3 && !stopRequested)
                            {
                                SendScrollInput();
                                await Task.Delay(Math.Max(Options.ScrollDelay, 300));
                                continue;
                            }

                            if (!HasContentChanged) status = ScrollingCaptureStatus.Failed;
                            break;
                        }

                        if (previousScreenshot != null) HasContentChanged = true;
                        unchangedAttempts = 0;
                        SendScrollInput();

                        Stopwatch timer = Stopwatch.StartNew();

                        if (lastScreenshot != null)
                        {
                            Bitmap newResult = await CombineImagesAsync(Result, lastScreenshot);

                            if (newResult != null)
                            {
                                Result?.Dispose();
                                Result = newResult;
                            }
                            else
                            {
                                break;
                            }
                        }

                        if (stopRequested)
                        {
                            break;
                        }

                        if (lastScreenshot != null)
                        {
                            if (previousScreenshot != null)
                            {
                                previousScreenshot.Dispose();
                            }

                            previousScreenshot = lastScreenshot;
                            lastScreenshot = null;
                        }

                        int delay = Options.ScrollDelay - (int)timer.ElapsedMilliseconds;

                        if (delay > 0)
                        {
                            await Task.Delay(delay);
                        }
                    }
                }
                finally
                {
                    regionForm?.Close();

                    Reset(true);
                    IsCapturing = false;
                }
            }

            return status;
        }

        public void StopCapture()
        {
            if (IsCapturing)
            {
                stopRequested = true;
            }
        }

        public async Task<bool> SelectWindowAsync()
        {
            bool selected = RegionCaptureTasks.GetRectangleRegion(out selectedRectangle, out selectedWindow, new RegionCaptureOptions());
            if (selected)
            {
                Point cursor = System.Windows.Forms.Cursor.Position;
                scrollPoint = selectedRectangle.Contains(cursor) ? cursor : new Point(
                    selectedRectangle.Left + selectedRectangle.Width / 2,
                    selectedRectangle.Top + selectedRectangle.Height / 2);
                if (selectedWindow != null)
                {
                    IntPtr handle = selectedWindow.Handle;
                    Point point = scrollPoint;
                    Rectangle selection = selectedRectangle;
                    try
                    {
                        Rectangle? document = await Task.Run(() => ScrollableWindowRegion.FindBrowserDocument(
                            handle, point, selection)).WaitAsync(TimeSpan.FromMilliseconds(1500));
                        if (document.HasValue) selectedRectangle = document.Value;
                    }
                    catch (TimeoutException) { } // Do not block capture on an unresponsive accessibility provider.
                }
                if (!selectedRectangle.Contains(scrollPoint)) scrollPoint = new Point(
                    selectedRectangle.Left + selectedRectangle.Width / 2,
                    selectedRectangle.Top + selectedRectangle.Height / 2);
            }
            return selected;
        }

        private void SendScrollInput()
        {
            if (!selectedWindow.IsActive) selectedWindow.Activate();
            switch (Options.ScrollMethod)
            {
                case ScrollMethod.MouseWheel:
                    // SendInput routes wheel input using the current pointer location.
                    // Return it to the selected pane before each wheel event, including retries.
                    if (!InputHelpers.SendMouseMove(scrollPoint) || !InputHelpers.SendMouseWheel(-120 * Options.ScrollAmount))
                        throw new InvalidOperationException("Unable to send scrolling input to the selected window.");
                    break;
                case ScrollMethod.DownArrow:
                    for (int i = 0; i < Options.ScrollAmount; i++) InputHelpers.SendKeyPress(VirtualKeyCode.DOWN);
                    break;
                case ScrollMethod.PageDown:
                    InputHelpers.SendKeyPress(VirtualKeyCode.NEXT);
                    break;
                case ScrollMethod.ScrollMessage:
                    for (int i = 0; i < Options.ScrollAmount; i++)
                        NativeMethods.SendMessage(selectedWindow.Handle, (int)WindowsMessages.VSCROLL, (int)ScrollBarCommands.SB_LINEDOWN, 0);
                    break;
            }
        }

        private bool IsScrollReachedBottom(IntPtr handle)
        {
            SCROLLINFO scrollInfo = new SCROLLINFO();
            scrollInfo.cbSize = (uint)Marshal.SizeOf(scrollInfo);
            scrollInfo.fMask = (uint)(ScrollInfoMask.SIF_RANGE | ScrollInfoMask.SIF_PAGE | ScrollInfoMask.SIF_TRACKPOS);

            if (NativeMethods.GetScrollInfo(handle, (int)SBOrientation.SB_VERT, ref scrollInfo))
            {
                return scrollInfo.nMax == scrollInfo.nTrackPos + scrollInfo.nPage - 1;
            }

            return CompareLastTwoImages();
        }

        private bool CompareLastTwoImages()
        {
            if (lastScreenshot != null && previousScreenshot != null)
            {
                return ImageHelpers.CompareImages(lastScreenshot, previousScreenshot);
            }

            return false;
        }

        private async Task<Bitmap> CombineImagesAsync(Bitmap result, Bitmap currentImage)
        {
            return await Task.Run(() => CombineImages(result, currentImage, comparisonSidePercent));
        }

        private Bitmap CombineImages(Bitmap result, Bitmap currentImage, int ignoreSidePercent = 5)
        {
            if (result == null)
            {
                status = ScrollingCaptureStatus.Successful;

                return (Bitmap)currentImage.Clone();
            }

            int matchCount = 0;
            int matchIndex = 0;
            int matchLimit = currentImage.Height / 2;

            int ignoreSideOffset = Math.Max(50, currentImage.Width * ignoreSidePercent / 100);
            ignoreSideOffset = Math.Min(ignoreSideOffset, currentImage.Width / 3);

            Rectangle rect = new Rectangle(ignoreSideOffset, result.Height - currentImage.Height, currentImage.Width - ignoreSideOffset * 2, currentImage.Height);

            BitmapData bdResult = result.LockBits(new Rectangle(0, 0, result.Width, result.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData bdCurrentImage = currentImage.LockBits(new Rectangle(0, 0, currentImage.Width, currentImage.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int stride = bdResult.Stride;
            int pixelSize = stride / result.Width;
            IntPtr resultScan0 = bdResult.Scan0 + pixelSize * ignoreSideOffset;
            IntPtr currentImageScan0 = bdCurrentImage.Scan0 + pixelSize * ignoreSideOffset;
            int compareLength = pixelSize * rect.Width;

            int ignoreBottomOffsetMax = currentImage.Height / 3;
            int ignoreBottomOffset = Math.Max(50, currentImage.Height / 10);

            if (Options.AutoIgnoreBottomEdge)
            {
                IntPtr resultScan0Last = resultScan0 + (result.Height - 1) * stride;
                IntPtr currentImageScan0Last = currentImageScan0 + (currentImage.Height - 1) * stride;

                for (int i = 0; i <= ignoreBottomOffsetMax; i++)
                {
                    if (NativeMethods.memcmp(resultScan0Last - i * stride, currentImageScan0Last - i * stride, compareLength) != 0)
                    {
                        ignoreBottomOffset += i;
                        break;
                    }
                }

                ignoreBottomOffset = Math.Max(ignoreBottomOffset, bestIgnoreBottomOffset);
            }

            ignoreBottomOffset = Math.Min(ignoreBottomOffset, ignoreBottomOffsetMax);

            int rectBottom = rect.Bottom - ignoreBottomOffset - 1;

            for (int currentImageY = currentImage.Height - 1; currentImageY >= 0 && matchCount < matchLimit; currentImageY--)
            {
                int currentMatchCount = 0;

                for (int y = 0; currentImageY - y >= 0 && currentMatchCount < matchLimit; y++)
                {
                    if (NativeMethods.memcmp(resultScan0 + ((rectBottom - y) * stride), currentImageScan0 + ((currentImageY - y) * stride), compareLength) == 0)
                    {
                        currentMatchCount++;
                    }
                    else
                    {
                        break;
                    }
                }

                if (currentMatchCount > matchCount)
                {
                    matchCount = currentMatchCount;
                    matchIndex = currentImageY;
                }
            }

            result.UnlockBits(bdResult);
            currentImage.UnlockBits(bdCurrentImage);

            // Fixed browser toolbars can be wider than the original 5% margin.
            // Retry the same upstream row matcher over the central 70%, then
            // central 50%, before reusing a previous frame's guessed offset.
            if (matchCount == 0 && ignoreSidePercent < 25)
            {
                return CombineImages(result, currentImage, ignoreSidePercent + 10);
            }

            bool bestGuess = false;

            if (matchCount == 0 && bestMatchCount > 0)
            {
                matchCount = bestMatchCount;
                matchIndex = bestMatchIndex;
                ignoreBottomOffset = bestIgnoreBottomOffset;
                bestGuess = true;
            }

            if (matchCount > 0)
            {
                int matchHeight = currentImage.Height - matchIndex - 1;

                if (matchHeight > 0)
                {
                    comparisonSidePercent = Math.Max(comparisonSidePercent, ignoreSidePercent);
                    if (matchCount > bestMatchCount)
                    {
                        bestMatchCount = matchCount;
                        bestMatchIndex = matchIndex;
                        bestIgnoreBottomOffset = ignoreBottomOffset;
                    }

                    Bitmap newResult = new Bitmap(result.Width, result.Height - ignoreBottomOffset + matchHeight);

                    int keepHeight = result.Height - ignoreBottomOffset;
                    int sourceTop = matchIndex + 1;
                    if (ignoreSidePercent > 5 && !bestGuess)
                    {
                        // For fixed side controls, join in the upper third of the verified overlap.
                        // This replaces the old frame's covered lower content with the next
                        // frame's unobstructed upper content, preserving the measured geometry.
                        int currentTop = newResult.Height - currentImage.Height;
                        if (currentTop >= 0 && currentTop < result.Height)
                        {
                            keepHeight = currentTop + (result.Height - currentTop) / 3;
                            sourceTop = keepHeight - currentTop;
                        }
                    }
                    int appendHeight = newResult.Height - keepHeight;

                    using (Graphics g = Graphics.FromImage(newResult))
                    {
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;

                        g.DrawImage(result, new Rectangle(0, 0, result.Width, keepHeight),
                            new Rectangle(0, 0, result.Width, keepHeight), GraphicsUnit.Pixel);
                        g.DrawImage(currentImage, new Rectangle(0, keepHeight, currentImage.Width, appendHeight),
                            new Rectangle(0, sourceTop, currentImage.Width, appendHeight), GraphicsUnit.Pixel);
                    }

                    if (bestGuess)
                    {
                        status = ScrollingCaptureStatus.PartiallySuccessful;
                    }
                    else if (status != ScrollingCaptureStatus.PartiallySuccessful)
                    {
                        status = ScrollingCaptureStatus.Successful;
                    }

                    return newResult;
                }
            }

            status = ScrollingCaptureStatus.Failed;

            return null;
        }
    }
}
