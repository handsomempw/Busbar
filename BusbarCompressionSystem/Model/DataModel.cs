using GalaSoft.MvvmLight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace BusbarCompressionSystem.Model
{
    public class DataModel : ObservableObject
    {
        #region 数据模型
        [XmlElement("过程参数模型")]
        public Processmodel Processmodel { get; set; } = new Processmodel();

        [XmlElement("日志模型")]
        public RecordModel Recordmodel { get; set; } = new RecordModel();

        [XmlElement("配置模型")]
        public SettingModel Settingmodel { get; set; } = new SettingModel();



        #endregion



    }
}
