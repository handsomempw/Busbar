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
        /// 本地开发调试授权开关，随通用配置保存到配置 XML。
        /// 该节点仅由 DEBUG 构建读取；开启后开发包使用本地 AOI 编辑会话，发布构建继续固定走生产动态密码认证。
        /// </summary>
        [XmlElement("开发调试授权")]
        public bool DeveloperLocalAuthEnabled { get; set; } = false;

        /// <summary>
        /// 报工失败报警使能：为 true 时写 M3045 禁止进站线圈并弹窗提示；为 false 时仅记日志，不写 PLC、不弹窗。
        /// </summary>
        [XmlElement("报工失败报警使能")]
        public bool ReportFailAlarmEnabled { get; set; } = true;

        /// <summary>
        /// 双Y电测部署的旧配置兼容值。首次升级时用于生成“配置\双Y电测配置.xml”；正式界面选型由独立配置接管，
        /// 运行中的产品流向仍以独立配置指定的 PLC 模式线圈为准。
        /// </summary>
        [XmlElement("双Y电测部署")]
        public bool DualYElectricalTestDeployment { get; set; } = false;

        /// <summary>
        /// 旧部署字段只参与首次迁移；正式部署选择由“配置\双Y电测配置.xml”维护。
        /// </summary>
        public bool ShouldSerializeDualYElectricalTestDeployment() => false;

        [XmlElement("仪器类型1")]
        public TVMeterType TVMeterType1 { set; get; } = TVMeterType.AT9620;

        [XmlElement("仪器类型2")]
        public TVMeterType TVMeterType2 { set; get; } = TVMeterType.AT9620;

        [XmlElement("仪器类型3")]
        public TVMeterType TVMeterType3 { set; get; } = TVMeterType.AT9620;

        /// <summary>
        /// 耐压 OK 标准件点检 SN。
        /// 工程 XML 保存该值；扫码后仍由 MES 解析工单和料号，CHECK 阶段按耐压实测结果返回机器人分流指令。
        /// </summary>
        [XmlElement("耐压点检OK_SN码")]
        public string InspectionTVOKSN { set; get; } = "INSPECTION_TV_OK";

        /// <summary>
        /// 耐压 NG 标准件点检 SN。
        /// 实测 NG 时 CHECK 继续返回 NG2，使标准件进入 NG 分流；SN 用于识别期望结果和点检留档。
        /// </summary>
        [XmlElement("耐压点检NG_SN码")]
        public string InspectionTVNGSN { set; get; } = "INSPECTION_TV_NG";

        /// <summary>
        /// IR 绝缘电阻 OK 标准件点检 SN。
        /// 工程 XML 缺少该节点时使用默认码；正常产品扫码、耐压和 AOI 点检口径保持原有行为。
        /// </summary>
        [XmlElement("IR点检OK_SN码")]
        public string InspectionIROKSN { set; get; } = "INSPECTION_IR_OK";

        /// <summary>
        /// IR 绝缘电阻 NG 标准件点检 SN。
        /// 实测 NG 时 D1015 保持 2、CHECK 返回 NG2，使标准件按实际结果进入 NG 分流。
        /// </summary>
        [XmlElement("IR点检NG_SN码")]
        public string InspectionIRNGSN { set; get; } = "INSPECTION_IR_NG";

        /// <summary>
        /// AOI OK 标准件点检 SN。
        /// 该码仅改变点检判定边界，正常产品的扫码校验和报工流程保持原有口径。
        /// </summary>
        [XmlElement("AOI点检OK_SN码")]
        public string InspectionAOIOKSN { set; get; } = "INSPECTION_AOI_OK";

        /// <summary>
        /// AOI NG 标准件点检 SN。
        /// 全部判定工具命中 NG/NG2 时返回 NG4 并通过 AOI NG 点检信号记录期望命中结果。
        /// </summary>
        [XmlElement("AOI点检NG_SN码")]
        public string InspectionAOINGSN { set; get; } = "INSPECTION_AOI_NG";

        /// <summary>
        /// 动态密码认证配置（用于敏感操作的权限开启）
        /// </summary>
        [XmlElement("动态密码认证")]
        public DynamicPasswordAuthenticationSetting DynamicPasswordAuth { get; set; } = new DynamicPasswordAuthenticationSetting();

    }
}
