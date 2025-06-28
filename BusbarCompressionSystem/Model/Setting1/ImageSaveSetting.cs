using GalaSoft.MvvmLight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace BusbarCompressionSystem.Model.Setting
{
    public class ImageSaveSetting : ObservableObject
    {
        [XmlElement("照片存储路径")]
        public string ImageSaveDir { set; get; } = $"{Environment.CurrentDirectory}\\照片";

        [XmlElement("保存合格照片")]
        public bool SaveOK { set; get; } = true;
        [XmlElement("保存NG照片")]
        public bool SaveNG { set; get; } = true;

    }
}
