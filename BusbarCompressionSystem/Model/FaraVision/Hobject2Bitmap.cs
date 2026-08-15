using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusbarCompressionSystem.Model.FaraVision
{
    public class Hobject2Bitmap
    {

        /// <summary>
        /// 将 HALCON 图像转换为供编辑器预览使用的 24 位位图。
        /// 返回的位图拥有独立的像素内存，调用方负责在更换预览图或关闭窗口时释放它。
        /// 输入图像由调用方继续管理，转换过程创建的 HALCON 中间图像和指针元组在方法返回前释放。
        /// </summary>
        /// <param name="ho_image">待转换的 HALCON 图像。</param>
        /// <param name="res24">转换后的 24 位位图。</param>
        public static void HobjectToBitmap24(HObject ho_image, out Bitmap res24)
        {
            res24 = null;
            HObject composedImage = null;
            HObject interImage = null;
            HTuple channels = null;
            HTuple pointer = null;
            HTuple type = null;
            HTuple width = null;
            HTuple height = null;

            try
            {
                if (ho_image == null)
                {
                    throw new ArgumentNullException(nameof(ho_image));
                }

                HOperatorSet.CountChannels(ho_image, out channels);
                HObject image = ho_image;
                if (channels.I == 1)
                {
                    HOperatorSet.Compose3(ho_image, ho_image, ho_image, out composedImage);
                    image = composedImage;
                }

                HOperatorSet.InterleaveChannels(image, out interImage, "rgb", "match", 255);
                HOperatorSet.GetImagePointer1(interImage, out pointer, out type, out width, out height);

                // Bitmap(pointer) 依赖 HALCON 内存的生命周期；复制后即可安全释放 HALCON 中间图像。
                using (Bitmap view = new Bitmap(width.I / 3, height.I, width.I, PixelFormat.Format24bppRgb, pointer))
                {
                    res24 = new Bitmap(view);
                }
            }
            catch
            {
                res24?.Dispose();
                res24 = null;
                throw;
            }
            finally
            {
                height?.Dispose();
                width?.Dispose();
                type?.Dispose();
                pointer?.Dispose();
                channels?.Dispose();
                interImage?.Dispose();
                composedImage?.Dispose();
            }
        }

        /// <summary>
        /// 将 HALCON 图像转换为供编辑器预览使用的 32 位位图。
        /// 返回的位图拥有独立的像素内存，调用方负责释放返回值；输入图像保持由调用方管理。
        /// </summary>
        /// <param name="ho_image">待转换的 HALCON 图像。</param>
        /// <param name="res32">转换后的 32 位位图。</param>
        public static void HobjectToBitmap32(HObject ho_image, out Bitmap res32)
        {
            res32 = null;
            HObject interImage = null;
            HTuple pointer = null;
            HTuple type = null;
            HTuple width = null;
            HTuple height = null;

            try
            {
                if (ho_image == null)
                {
                    throw new ArgumentNullException(nameof(ho_image));
                }

                HOperatorSet.InterleaveChannels(ho_image, out interImage, "argb", "match", 255);
                HOperatorSet.GetImagePointer1(interImage, out pointer, out type, out width, out height);

                using (Bitmap view = new Bitmap(width.I / 4, height.I, width.I, PixelFormat.Format32bppRgb, pointer))
                {
                    res32 = new Bitmap(view);
                }
            }
            catch
            {
                res32?.Dispose();
                res32 = null;
                throw;
            }
            finally
            {
                height?.Dispose();
                width?.Dispose();
                type?.Dispose();
                pointer?.Dispose();
                interImage?.Dispose();
            }
        }

        /// <summary>
        /// 将 HALCON 灰度图转换为供编辑器预览使用的 8 位索引位图。
        /// 返回的位图拥有独立的像素内存和灰度调色板，调用方负责释放返回值。
        /// </summary>
        /// <param name="ho_image">待转换的 HALCON 灰度图。</param>
        /// <param name="res8">转换后的 8 位灰度位图。</param>
        public static void HobjectToBitmap8(HObject ho_image, out Bitmap res8)
        {
            res8 = null;
            HTuple pointer = null;
            HTuple type = null;
            HTuple width = null;
            HTuple height = null;

            try
            {
                if (ho_image == null)
                {
                    throw new ArgumentNullException(nameof(ho_image));
                }

                HOperatorSet.GetImagePointer1(ho_image, out pointer, out type, out width, out height);
                res8 = new Bitmap(width.I, height.I, PixelFormat.Format8bppIndexed);
                BitmapData bitmapData = res8.LockBits(
                    new Rectangle(0, 0, width.I, height.I),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format8bppIndexed);
                try
                {
                    byte[] row = new byte[width.I];
                    for (int y = 0; y < height.I; y++)
                    {
                        Marshal.Copy(IntPtr.Add(pointer, y * width.I), row, 0, row.Length);
                        Marshal.Copy(row, 0, IntPtr.Add(bitmapData.Scan0, y * bitmapData.Stride), row.Length);
                    }
                }
                finally
                {
                    res8.UnlockBits(bitmapData);
                }

                ColorPalette cp = res8.Palette;
                for (int i = 0; i < 256; i++)
                {
                    cp.Entries[i] = Color.FromArgb(i, i, i);
                }
                res8.Palette = cp;
            }
            catch
            {
                res8?.Dispose();
                res8 = null;
                throw;
            }
            finally
            {
                height?.Dispose();
                width?.Dispose();
                type?.Dispose();
                pointer?.Dispose();
            }
        }

    }
}
