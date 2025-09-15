using GalaSoft.MvvmLight;
using HalconDotNet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;
using System.Xml.Serialization;

namespace Camera
{
    public class DATA : ObservableObject
    {
        public CameraModel CameraModel { get; set; } = new CameraModel();

        #region 保存设置
        public void save_setting(string filename)
        {
            try
            {
                string dir = Path.GetDirectoryName(filename);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                using (var stream = new FileStream(filename, FileMode.Create))
                {
                    XmlSerializer sz = new XmlSerializer(typeof(CameraModel));
                    sz.Serialize(stream, CameraModel);
                }
            }
            catch {; }
        }
        public void load_setting(string filename)
        {
            try
            {
                if (File.Exists(filename))
                {
                    using (var stream = new StreamReader(filename))
                    {
                        XmlSerializer sz = new XmlSerializer(typeof(CameraModel));
                        CameraModel = sz.Deserialize(stream) as CameraModel;
                    }
                    return;
                }
            }
            catch
            {; }
            CameraModel = new CameraModel();
        }
        #endregion

        public void init(string filename)
        {
            init1(filename);
            init2();
        }

        public void init1(string filename)
        {
            load_setting(filename);
            
        }
        public void init2()
        {
            CameraModel.camera.init(CameraModel.CameraID);
            CameraModel.camera.bnOpen_Click();
            CameraModel.camera.bnStartGrab_Click();
            CameraModel.camera.Exposure = CameraModel.exposuretime;
            CameraModel.camera.bnGetParam_Click();
        }


        public void closing(string filename)
        {
            save_setting(filename);
            CameraModel.camera.bnClose_Click();

        }

        public bool disp(HObject image)
        {
            try
            {
                HTuple w = 0, h = 0;
                HOperatorSet.GetImageSize(image, out w, out h);

                //CameraModel.HWindow.HalconWindow.SetPart((int)0, (int)0, (int)(h - 1), (int)(w - 1));
                CameraModel.HWindow.HalconWindow.DispObj(image);
                return true;
            }
            catch { return false; }
        }


        //public void saveimage(HObject hoimage, string filename)
        //{
        //    //string filename = $"D:\\图片\\{DATA.name}\\{DateTime.Now.ToString("yyyy-MM-dd")}\\{(result ? "OK" : "NG")}\\{DateTime.Now.ToString("yyyyMMddHHmmssFFF")}.jpg";
        //    string path = Path.GetDirectoryName(filename);
        //    if (!Directory.Exists(path))
        //    {
        //        Directory.CreateDirectory(path);
        //    }
        //    HOperatorSet.WriteImage(hoimage, "jpg", 0, filename);
        //}


    }
}
