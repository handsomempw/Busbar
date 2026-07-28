using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using GalaSoft.MvvmLight;
using System.IO;

namespace Camera
{
    public class CameraModel : ObservableObject
    {

        [XmlIgnore]
        public bool finished { set; get; } = false;


        [XmlIgnore]
        //public HWindowControlWPF HWindow { get; set; }
        public HSmartWindowControlWPF HWindow { get; set; }

        private Hikvision _camera;

        /// <summary>
        /// 海康相机运行对象。相机 SDK 只在标准视觉流程首次访问该属性时加载，纯电测部署创建配置模型时不会触发 MVS 运行库。
        /// 该属性不参与 XML 序列化：保存相机配置时只写入 CameraID 与曝光时间，避免把运行时 SDK 对象写入配置并在反序列化时提前加载 MVS。
        /// </summary>
        [XmlIgnore]
        public Hikvision camera
        {
            get { return _camera ?? (_camera = new Hikvision()); }
            set { _camera = value; }
        }
        [XmlElement("相机序号")]
        //public string CameraID { set; get; } = "相机序号";
        public string CameraID { set; get; } = "00K09413100";
        

        [XmlElement("曝光时间")]
        public float exposuretime { set; get; } = 20000;
               


 


    }
}
