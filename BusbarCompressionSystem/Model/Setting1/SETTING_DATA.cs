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



        [XmlElement("工序名称2")]
        public string ProcedureName2 { set; get; } = "工序名称2";
        [XmlElement("工位号2")]
        public string StationCode2 { set; get; } = "工位号2";

        [XmlElement("标准工序码2")]
        public string StandardCode2 { set; get; } = "标准工序码2";




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

        /// <summary>
        /// 绝缘电阻仪编号（用于 IR 结果追溯：SQLite/F7/MES 归档区分）
        /// </summary>
        [XmlElement("IR仪器编号")]
        public string IRMeterID { set; get; } = "IRMETERID";

        /// <summary>
        /// IR(AT6835FL) 参数下发/通信调试开关：
        /// 打开后会生成独立调试日志文件，并在串口收发路径记录轮询快照（体积较大，仅排障时开启）。
        /// </summary>
        [XmlElement("IR参数下发调试日志")]
        public bool IrDownloadDebugLog { get; set; } = false;

        /// <summary>
        /// 报工失败报警使能：为 true 时写 M3045 禁止进站线圈并弹窗提示；为 false 时仅记日志，不写 PLC、不弹窗。
        /// </summary>
        [XmlElement("报工失败报警使能")]
        public bool ReportFailAlarmEnabled { get; set; } = true;

        [XmlElement("仪器类型1")]
        public TVMeterType TVMeterType1 { set; get; } = TVMeterType.AT9620;

        [XmlElement("仪器类型2")]
        public TVMeterType TVMeterType2 { set; get; } = TVMeterType.AT9620;

        [XmlElement("仪器类型3")]
        public TVMeterType TVMeterType3 { set; get; } = TVMeterType.AT9620;

        // 耐压工位点检 OK / NG SN 码
        [XmlElement("耐压点检OK_SN码")]
        public string InspectionTVOKSN { set; get; } = "INSPECTION_TV_OK";

        [XmlElement("耐压点检NG_SN码")]
        public string InspectionTVNGSN { set; get; } = "INSPECTION_TV_NG";

        // AOI 工位点检 OK / NG SN 码
        [XmlElement("AOI点检OK_SN码")]
        public string InspectionAOIOKSN { set; get; } = "INSPECTION_AOI_OK";

        [XmlElement("AOI点检NG_SN码")]
        public string InspectionAOINGSN { set; get; } = "INSPECTION_AOI_NG";

        /// <summary>
        /// 动态密码认证配置（用于敏感操作的权限开启）
        /// </summary>
        [XmlElement("动态密码认证")]
        public DynamicPasswordAuthenticationSetting DynamicPasswordAuth { get; set; } = new DynamicPasswordAuthenticationSetting();

    }
}
