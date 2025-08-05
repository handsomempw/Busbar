using GalaSoft.MvvmLight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace BusbarCompressionSystem.Model.Setting1
{

    public class SETTING_DATA : ObservableObject
    {

        [XmlElement("设备编号")]
        public string MachineID { get; set; } = "设备编号";
        [XmlElement("工序名称")]
        public string ProcedureName { set; get; } = "工序名称";
        [XmlElement("工位号")]
        public string StationCode { set; get; } = "工位号";

        [XmlElement("标准工序码")]
        public string StandardCode { set; get; } = "标准工序码";
        [XmlElement("工号")]
        [XmlIgnore]
        public string WorkerID { set; get; } = "工号";
        [XmlElement("姓名")]
        [XmlIgnore]
        public string WorkerName { set; get; } = "姓名";



        [XmlElement("仪器编号1")]
        public string TVMeterID1 { set; get; } = "TVMETERID1";

        [XmlElement("仪器编号2")]
        public string TVMeterID2 { set; get; } = "TVMETERID2";
        [XmlElement("仪器编号3")]
        public string TVMeterID3 { set; get; } = "TVMETERID3";


        [XmlElement("仪器类型1")]
        public TVMeterType TVMeterType1 { set; get; } = TVMeterType.AT9620;

        [XmlElement("仪器类型2")]
        public TVMeterType TVMeterType2 { set; get; } = TVMeterType.AT9620;

        [XmlElement("仪器类型3")]
        public TVMeterType TVMeterType3 { set; get; } = TVMeterType.AT9620;

    }
}
