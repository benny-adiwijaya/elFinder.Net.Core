﻿using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using Xabe.FFmpeg;

namespace elFinder.Net.Core.Services.Drawing
{
    /// <summary>
    /// Represents default pictures editor
    /// </summary>
    public class DefaultPictureEditor : IPictureEditor
    {
        #region Constructors

        public DefaultPictureEditor(Color backgroundColor)
        {
            BackgroundColor = backgroundColor;
        }

        public DefaultPictureEditor()
            : this(Color.Transparent)
        {
        }

        #endregion Constructors

        #region IPictureEditor Members

        public virtual Color BackgroundColor { get; set; }

        public virtual bool CanProcessFile(string fileExtension)
        {
            string ext = fileExtension.ToLower();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" || ext == ".tiff"
                //video ext
                || ext == ".mp4" || ext == ".avi" || ext == ".mxf" || ext == ".webm" || ext == ".mkv" || ext == ".flv"
                || ext == ".mpeg" || ext == ".mov"; 
        }

        public virtual string ConvertThumbnailExtension(string originalImageExtension)
        {
            string ext = originalImageExtension.ToLower();
            if (ext == ".tiff" || ext == ".mp4" || ext == ".avi" || ext == ".mxf" || ext == ".webm" || ext == ".mkv" || ext == ".flv"
                || ext == ".mpeg" || ext == ".mov")
            {
                return ".png";
            }

            if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif")
            {
                return ext;
            }
            else
            {
                throw new ArgumentException(typeof(DefaultPictureEditor).FullName + " does not support thumbnails for '" + originalImageExtension + "' files.");
            }
        }

        public virtual ImageWithMimeType Crop(Stream input, int x, int y, int width, int height, long? quality = null)
        {
            using (var image = Image.FromStream(input))
            {
                return ScaleOrCrop(image, new Rectangle(x, y, width, height), new Rectangle(0, 0, width, height), quality);
            }
        }

        public virtual ImageWithMimeType GenerateThumbnail(Stream input, int size, bool keepAspectRatio)
        {
            using (var inputImage = Image.FromStream(input))
            {
                int targetWidth;
                int targetHeight;

                if (keepAspectRatio)
                {
                    double width = inputImage.Width;
                    double height = inputImage.Height;
                    double percentWidth = width != 0 ? size / width : 0;
                    double percentHeight = height != 0 ? size / height : 0;
                    double percent = percentHeight < percentWidth ? percentHeight : percentWidth;

                    targetWidth = (int)(width * percent);
                    targetHeight = (int)(height * percent);
                }
                else
                {
                    targetWidth = size;
                    targetHeight = size;
                }

                return ScaleOrCrop(
                    inputImage,
                    new Rectangle(0, 0, inputImage.Width, inputImage.Height),
                    new Rectangle(0, 0, targetWidth, targetHeight));
            }
        }

        public virtual async Task<string> GenerateVideoThumbnailAsync(string filePath)
        {
            string output = "";

            try
            {
                output = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");

                IConversion conversion = await FFmpeg.Conversions.FromSnippet.Snapshot(filePath, output, TimeSpan.FromSeconds(5));
                IConversionResult result = await conversion.Start();
            }
            catch (Exception ex)
            {
                string error = ex.Message;
            }

            return output;

        }

        public virtual Size ImageSize(Stream input)
        {
            using (var image = Image.FromStream(input))
            {
                return new Size(image.Width, image.Height);
            }
        }

        public virtual Size ImageSize(string fullPath)
        {
            using (var image = Image.FromFile(fullPath))
            {
                return new Size(image.Width, image.Height);
            }
        }

        public virtual ImageWithMimeType Resize(Stream input, int width, int height, long? quality = null)
        {
            using (var image = Image.FromStream(input))
            {
                return ScaleOrCrop(image, new Rectangle(0, 0, image.Width, image.Height), new Rectangle(0, 0, width, height), quality);
            }
        }

        public virtual ImageWithMimeType Rotate(Stream input, int angle, Color? background = null, long? quality = null)
        {
            using (var image = Image.FromStream(input))
            {
                return Rotate(image, angle, background, quality);
            }
        }

        public virtual ImageWithMimeType Rotate(Stream input, int angle, string backgroundHex = null, long? quality = null)
        {
            Color? bgColor = null;

            if (!string.IsNullOrEmpty(backgroundHex))
            {
                var argb = Convert.ToInt32($"FF{backgroundHex.Substring(1)}", 16);
                bgColor = Color.FromArgb(argb);
            }

            using (var image = Image.FromStream(input))
            {
                return Rotate(image, angle, bgColor, quality);
            }
        }

        public virtual ImageWithMimeType ChangeQuality(Image image, ImageFormat imageFormat, long value)
        {
            var qualityEncoder = Encoder.Quality;
            var parameters = new EncoderParameters(1);
            var qualityParam = new EncoderParameter(qualityEncoder, value);
            parameters.Param[0] = qualityParam;
            var memoryStream = new MemoryStream();

            string mimeType;
            if (imageFormat.Guid == ImageFormat.Jpeg.Guid)
                mimeType = "image/jpeg";
            else if (imageFormat.Guid == ImageFormat.Gif.Guid)
                mimeType = "image/gif";
            else
                mimeType = "image/png";

            var imageCodecInfo = GetEncoderInfo(mimeType);
            image.Save(memoryStream, imageCodecInfo, parameters);
            memoryStream.Position = 0;
            return new ImageWithMimeType(mimeType, memoryStream);
        }

        #endregion IPictureEditor Members

        /// <summary>
        /// Creates a new Image containing the same image, only rotated
        /// </summary>
        /// <param name="image">The <see cref="System.Drawing.Image"/> to rotate</param>
        /// <param name="degrees">The amount to rotate the image, clockwise, in degrees</param>
        /// <returns>A new <see cref="System.Drawing.Bitmap"/> that is just large enough to contain the rotated image without cutting any corners off.</returns>
        /// <remarks>Original code can be found at http://www.codeproject.com/Articles/58815/C-Image-PictureBox-Rotations </remarks>
        /// <exception cref="System.ArgumentNullException">Thrown if <see cref="image"/> is null.</exception>
        protected virtual ImageWithMimeType Rotate(Image image, float degrees, Color? background, long? quality = null)
        {
            if (image == null)
            {
                throw new ArgumentNullException("image");
            }

            const double halfPi = Math.PI / 2.0;
            double oldWidth = image.Width;
            double oldHeight = image.Height;

            double theta = degrees * Math.PI / 180.0;
            double lockedTheta = theta;

            while (lockedTheta < 0.0)
            {
                lockedTheta += 2 * Math.PI;
            }

            double newWidth, newHeight;
            int nWidth, nHeight;

            double adjacentTop;
            double oppositeTop;
            double adjacentBottom;
            double oppositeBottom;

            if ((lockedTheta >= 0.0 && lockedTheta < halfPi) ||
                (lockedTheta >= Math.PI && lockedTheta < (Math.PI + halfPi)))
            {
                adjacentTop = Math.Abs(Math.Cos(lockedTheta)) * oldWidth;
                oppositeTop = Math.Abs(Math.Sin(lockedTheta)) * oldWidth;

                adjacentBottom = Math.Abs(Math.Cos(lockedTheta)) * oldHeight;
                oppositeBottom = Math.Abs(Math.Sin(lockedTheta)) * oldHeight;
            }
            else
            {
                adjacentTop = Math.Abs(Math.Sin(lockedTheta)) * oldHeight;
                oppositeTop = Math.Abs(Math.Cos(lockedTheta)) * oldHeight;

                adjacentBottom = Math.Abs(Math.Sin(lockedTheta)) * oldWidth;
                oppositeBottom = Math.Abs(Math.Cos(lockedTheta)) * oldWidth;
            }

            newWidth = adjacentTop + oppositeBottom;
            newHeight = adjacentBottom + oppositeTop;

            nWidth = (int)Math.Ceiling(newWidth);
            nHeight = (int)Math.Ceiling(newHeight);

            using (var rotatedBmp = new Bitmap(nWidth, nHeight))
            {
                using (var g = Graphics.FromImage(rotatedBmp))
                {
                    g.Clear(background ?? BackgroundColor);
                    Point[] points;
                    if (lockedTheta >= 0.0 && lockedTheta < halfPi)
                    {
                        points = new Point[]
                        {
                            new Point((int) oppositeBottom, 0),
                            new Point(nWidth, (int) oppositeTop),
                            new Point(0, (int) adjacentBottom)
                        };
                    }
                    else if (lockedTheta >= halfPi && lockedTheta < Math.PI)
                    {
                        points = new Point[]
                        {
                            new Point(nWidth, (int) oppositeTop),
                            new Point((int) adjacentTop, nHeight),
                            new Point((int) oppositeBottom, 0)
                        };
                    }
                    else if (lockedTheta >= Math.PI && lockedTheta < (Math.PI + halfPi))
                    {
                        points = new Point[]
                        {
                            new Point((int) adjacentTop, nHeight),
                            new Point(0, (int) adjacentBottom),
                            new Point(nWidth, (int) oppositeTop)
                        };
                    }
                    else
                    {
                        points = new Point[]
                        {
                            new Point(0, (int) adjacentBottom),
                            new Point((int) oppositeBottom, 0),
                            new Point((int) adjacentTop, nHeight)
                        };
                    }
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;

                    g.DrawImage(image, points);
                }

                if (quality == null) return SaveImage(rotatedBmp, image.RawFormat);
                return ChangeQuality(rotatedBmp, image.RawFormat, quality.Value);
            }
        }

        protected virtual ImageWithMimeType SaveImage(Image image, ImageFormat imageFormat)
        {
            var memoryStream = new MemoryStream(); // Will be disposed when "ImageWithMimeType" is disposed

            string mimeType;
            if (imageFormat.Guid == ImageFormat.Jpeg.Guid)
            {
                image.Save(memoryStream, ImageFormat.Jpeg);
                mimeType = "image/jpeg";
            }
            else if (imageFormat.Guid == ImageFormat.Gif.Guid)
            {
                image.Save(memoryStream, ImageFormat.Gif);
                mimeType = "image/gif";
            }
            else
            {
                image.Save(memoryStream, ImageFormat.Png);
                mimeType = "image/png";
            }
            memoryStream.Position = 0;
            return new ImageWithMimeType(mimeType, memoryStream);
        }

        protected virtual ImageWithMimeType ScaleOrCrop(Image image, Rectangle source, Rectangle destination, long? quality = null)
        {
            using (var newImage = new Bitmap(destination.Width, destination.Height))
            {
                using (var g = Graphics.FromImage(newImage))
                {
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(image, destination, source, GraphicsUnit.Pixel);
                }

                if (quality == null) return SaveImage(newImage, image.RawFormat);
                return ChangeQuality(newImage, image.RawFormat, quality.Value);
            }
        }

        protected virtual ImageCodecInfo GetEncoderInfo(string mimeType)
        {
            int j;
            ImageCodecInfo[] encoders;
            encoders = ImageCodecInfo.GetImageEncoders();
            for (j = 0; j < encoders.Length; ++j)
            {
                if (encoders[j].MimeType == mimeType)
                    return encoders[j];
            }
            return null;
        }
    }
}
