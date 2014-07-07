using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace osu__Background_Overlay
{
    class GraphicsHelper
    {
        private enum TileMode
        {
            NONE =      0,
            TILE =      1,
            STRETCH =   2,
            FIT =       6,
            FILL =      10,
            SPAN =      22
        }

        /// <summary>
        /// Generates an image containing the user's screen backgrounds.
        /// </summary>
        public static Bitmap CopyScreen()
        {
            StringBuilder sb = new StringBuilder(500);
            WinApiHelper.SystemParametersInfo(0x73, (uint)sb.Capacity, sb, 0);
            string cWallpaper = sb.ToString();

            string[] files;

            if (cWallpaper.Substring(cWallpaper.LastIndexOf('\\') + 1) == "TranscodedWallpaper")
                files = Directory.GetFiles(cWallpaper.Substring(0, cWallpaper.LastIndexOf('\\')), "Transcoded_*").OrderByDescending(fName => fName).Reverse().Union(new[] { cWallpaper }).ToArray();
            else
            {
                files = new string[1];
                files[0] = cWallpaper;
            }

            //Check if the user has a background
            if (files.Length == 0)
                return new Bitmap(1, 1);

            //Get the background fill mode
            TileMode imageTileMode = (TileMode)Convert.ToInt32((string)Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop").GetValue("WallpaperStyle"));
            imageTileMode |= (TileMode)Convert.ToInt32((string)Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop").GetValue("TileWallpaper"));

            //Get the max screen bounds
            Rectangle maxBounds = SystemInformation.VirtualScreen;
            Screen[] screens = Screen.AllScreens;

            Bitmap screenBitmap = new Bitmap(maxBounds.Width - maxBounds.X, maxBounds.Height - maxBounds.Y);
            Graphics screenGraphics = Graphics.FromImage(screenBitmap);

            //Add each bitmap to the generated background image
            Bitmap tBitmap = new Bitmap(1, 1);
            switch (imageTileMode)
            {
                //Cases where multiple images may be displayed
                //at any one time
                case TileMode.NONE: case TileMode.STRETCH: case TileMode.FIT: case TileMode.FILL: case TileMode.TILE:
                    int currentFile = 0;
                    foreach (string file in files)
                    {
                        if (file != files.Last() || files.Length == 1)
                        {
                            //Clear and re-fill our temporary bitmap
                            tBitmap.Dispose();
                            using (FileStream stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                tBitmap = new Bitmap(Image.FromStream(stream));
                            switch (imageTileMode)
                            {
                                case TileMode.NONE:
                                    DrawImageUnscaledClippedCentered(screenGraphics, tBitmap, files.Length == 1 ? maxBounds : screens[currentFile].Bounds);
                                    break;
                                case TileMode.STRETCH:
                                    screenGraphics.DrawImage(tBitmap, files.Length == 1 ? maxBounds : screens[currentFile].Bounds, 0, 0, tBitmap.Width, tBitmap.Height, GraphicsUnit.Pixel);
                                    break;
                                case TileMode.FIT:
                                    DrawImageScaledandFit(screenGraphics, tBitmap, files.Length == 1 ? maxBounds : screens[currentFile].Bounds);
                                    break;
                                case TileMode.FILL:
                                    DrawImageScaledandFit(screenGraphics, tBitmap, files.Length == 1 ? maxBounds : screens[currentFile].Bounds, true);
                                    break;
                                case TileMode.TILE:
                                    throw new NotImplementedException("osu!BGO does not currently support tiled wallpapers. Sorry! :C");
                            }
                            currentFile += 1;
                        }
                    }
                    tBitmap.Dispose();
                    break;

                //Cases where only one image is displayed
                //at any one time
                case TileMode.SPAN:
                    tBitmap.Dispose();
                    using (FileStream stream = new FileStream(files[0], FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        tBitmap = new Bitmap(Image.FromStream(stream));
                    DrawImageScaledandFit(screenGraphics, tBitmap, maxBounds, true);
                    break;
            }
            return screenBitmap;
        }

        /// <summary>
        /// Implements centering in the .NET method DrawImageUnscaledandClipped.
        /// </summary>
        /// <param name="targetGraphics">The target garphics object where the imgae will be drawn.</param>
        /// <param name="sourceImage">The image to fill the target graphics with.</param>
        /// <param name="bounds">The maximum bounds of the desired target image area in the graphics object.</param>
        private static void DrawImageUnscaledClippedCentered(Graphics targetGraphics, Image sourceImage, Rectangle bounds)
        {
            if (sourceImage == null) 
                throw new ArgumentNullException("sourceImage");

            int width = Math.Min(bounds.Width, sourceImage.Width);
            int height = Math.Min(bounds.Height, sourceImage.Height);

            targetGraphics.DrawImage(sourceImage, 
                new Rectangle(bounds.Width / 2 - width / 2, bounds.Height / 2 - height / 2, width, height),
                new Rectangle(0, 0, sourceImage.Width, sourceImage.Height),
                GraphicsUnit.Pixel);
        }
 
        /// <summary>
        /// Draws the source image to the destination graphics object with either fit or fill scaling.
        /// Fit scaling -> All of the image will be visible on the screen with black borders for unmatching aspect ratios.
        /// Fill scaling -> An amount of the image enough to remove any black borders will be visible on the screen.
        /// </summary>
        /// <param name="targetGraphics">The target garphics object where the imgae will be drawn.</param>
        /// <param name="sourceImage">The image to fill the target graphics with.</param>
        /// <param name="bounds">The maximum bounds of the desired target image area in the graphics object.</param>
        /// <param name="fill">Specifies if the fill algorithm should be used instead of fit.</param>
        private static void DrawImageScaledandFit(Graphics targetGraphics, Image sourceImage, Rectangle bounds, bool fill = false)
        {
            float widthRatio = bounds.Width / (float)sourceImage.Width;
            float heightRatio = bounds.Height / (float)sourceImage.Height;

            //Bigger ratio -> Smaller side
            float scaleRatio = fill ? Math.Max(widthRatio, heightRatio) : Math.Min(widthRatio, heightRatio);
            int width = (int)(sourceImage.Width * scaleRatio);
            int height = (int)(sourceImage.Height * scaleRatio);

            targetGraphics.DrawImage(sourceImage, 
                new Rectangle(bounds.Width / 2 - width / 2, bounds.Height / 2 - height / 2, width, height), 
                new Rectangle(0, 0, sourceImage.Width, sourceImage.Height), 
                GraphicsUnit.Pixel);
        }

        private static void DrawImageTiled(Graphics targetGraphics, Image sourceImage, Rectangle bounds)
        {
            float fillRatio = bounds.Width / (float)sourceImage.Width;

        }
    }
}
