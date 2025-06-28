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
        [XmlIgnore]
        [XmlElement("相机参数")]
        public Hikvision camera { set; get; } = new Hikvision();

        [XmlElement("相机序号")]
        //public string CameraID { set; get; } = "相机序号";
        public string CameraID { set; get; } = "00K09413100";
        

        [XmlElement("曝光时间")]
        public float exposuretime { set; get; } = 20000;
               


 


    }
}
