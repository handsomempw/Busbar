using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusbarCompressionSystem.Model.FaraVision
{
    public class Hobject2Bitmap
    {

        /// <summary>
        /// hobject彩色图24位转bitmap
        /// </summary>
        /// <param name="ho_image"></param>
        /// <param name="res24"></param>
        public static void HobjectToBitmap24(HObject ho_image, out Bitmap res24)
        {
            HObject Image = ho_image;
            HTuple C = new HTuple();
            HOperatorSet.CountChannels(ho_image, out C);
            if (C == 1)
            {
                HOperatorSet.Compose3(ho_image, ho_image, ho_image, out Image);
            }

            HTuple type, width, height;
            //创建交错格式图像
            HOperatorSet.InterleaveChannels(Image, out HObject InterImage, "rgb", "match", 255);
            //获取交错格式图像指针
            HOperatorSet.GetImagePointer1(InterImage, out HTuple Pointer, out type, out width, out height);
            IntPtr ptr = Pointer;
            res24 = new Bitmap(width / 3, height, width, PixelFormat.Format24bppRgb, ptr);
        }
        /// <summary>
        /// hobject彩色图32位转bitmap
        /// </summary>
        /// <param name="ho_image"></param>
        /// <param name="res32"></param>
        public static void HobjectToBitmap32(HObject ho_image, out Bitmap res32)
        {
            HTuple type, width, height;
            //创建交错格式图像
            HOperatorSet.InterleaveChannels(ho_image, out HObject InterImage, "argb", "match", 255);
            //获取交错格式图像指针
            HOperatorSet.GetImagePointer1(InterImage, out HTuple Pointer, out type, out width, out height);
            IntPtr ptr = Pointer;
            res32 = new Bitmap(width / 4, height, width, PixelFormat.Format32bppRgb, ptr);
        }
        /// <summary>
        /// hobject灰度8位转bitmap
        /// </summary>
        /// <param name="ho_image"></param>
        /// <param name="res8"></param>
        public static void HobjectToBitmap8(HObject ho_image, out Bitmap res8)
        {
            HTuple type, width, height;
            HOperatorSet.GetImagePointer1(ho_image, out HTuple Pointer, out type, out width, out height);
            IntPtr ptr = Pointer;
            res8 = new Bitmap(width, height, width, PixelFormat.Format8bppIndexed, ptr);
            //设置灰度调色板
            ColorPalette cp = res8.Palette;
            for (int i = 0; i < 256; i++)
            {
                cp.Entries[i] = Color.FromArgb(i, i, i);
            }
            res8.Palette = cp;
        }

    }
}
