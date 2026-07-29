using System;
using System.Xml.Serialization;

namespace BusbarCompressionSystem.Model.Setting1
{
    /// <summary>
    /// 双Y专用机台的部署、扫码器和 PLC 交互配置。
    /// 该模型独立保存到“配置\双Y电测配置.xml”，普通产线扫码器继续使用“配置\配置数据.xml”。
    /// </summary>
    [XmlRoot("双Y电测配置")]
    public class DualYElectricalTestConfiguration
    {
        /// <summary>
        /// 配置结构版本，仅用于后续兼容迁移，不参与 PLC、电测判定或 MES 报工。
        /// </summary>
        [XmlElement("配置版本")]
        public int ConfigurationVersion { get; set; } = 3;

        /// <summary>
        /// 面向现场维护人员的业务边界说明。程序不依据这些文字作判定，保存时保留说明便于直接检查 XML。
        /// </summary>
        [XmlElement("业务说明")]
        public DualYConfigurationDescription BusinessDescription { get; set; } = new DualYConfigurationDescription();

        /// <summary>
        /// 双Y专用机台部署开关。软件启动前读取该值：true 打开双Y小屏窗口并跳过标准视觉窗口，false 打开标准生产窗口。
        /// 运行中的产品流程仍由 <see cref="ElectricalTestModeCoilAddress"/> 对应的 PLC 线圈决定。
        /// </summary>
        [XmlElement("双Y专用部署")]
        public bool DeploymentEnabled { get; set; }

        /// <summary>
        /// 双Y仅电测模式线圈地址，单位为 M 寄存器编号。1 进入双Y扫码、并行电测和流程结束归档，0 进入标准产线流程。
        /// </summary>
        [XmlElement("双Y电测模式线圈M")]
        public int ElectricalTestModeCoilAddress { get; set; } = 3050;

        /// <summary>
        /// Y1 自动扫码器类型，支持“HF800”或“Scanner”。手动扫码继续走同一套 MES 校验、SQLite 建账和 PLC 反馈业务链。
        /// </summary>
        [XmlElement("Y1扫码器模式")]
        public string Station1ScannerMode { get; set; } = "HF800";

        /// <summary>
        /// Y1 通用串口扫码器参数，仅在 <see cref="Station1ScannerMode"/> 为“Scanner”时使用。
        /// </summary>
        [XmlElement("Y1串口扫码器")]
        public Scanner.ScannerModel Station1ScannerModel { get; set; } = new Scanner.ScannerModel();

        /// <summary>
        /// Y1 HF800 网络扫码器参数，仅在 <see cref="Station1ScannerMode"/> 为“HF800”时使用。
        /// </summary>
        [XmlElement("Y1霍尼韦尔扫码器")]
        public Honeywell.HF800 Station1HF800 { get; set; } = new Honeywell.HF800();

        /// <summary>
        /// Y1 扫码成功后写入产品码的起始地址，单位为 D 寄存器编号，内容格式为“SN;工单号”。
        /// 信号表约定为 D800；仅服务进站写码。流程结束归档回读耐压1镜像 AddressSN+25（默认 D825），不读本地址。
        /// </summary>
        [XmlElement("Y1扫码SN地址D")]
        public int Station1ScanSnAddress { get; set; } = 800;

        /// <summary>
        /// Y1 自动扫码触发地址，单位为 D 寄存器编号；PLC 写入 1 时触发一次 Y1 读码。
        /// </summary>
        [XmlElement("Y1扫码触发地址D")]
        public int Station1ScanTriggerAddress { get; set; } = 1000;

        /// <summary>
        /// Y1 扫码业务结果地址，单位为 D 寄存器编号；上位机写入 1 表示完整成功，2 表示读码或业务校验失败。
        /// </summary>
        [XmlElement("Y1扫码返回地址D")]
        public int Station1ScanResultAddress { get; set; } = 1001;

        /// <summary>
        /// Y2 自动扫码器类型，支持“HF800”或“Scanner”。该设备独立于普通产线的下料扫码器。
        /// </summary>
        [XmlElement("Y2扫码器模式")]
        public string Station2ScannerMode { get; set; } = "HF800";

        /// <summary>
        /// Y2 通用串口扫码器参数，仅在 <see cref="Station2ScannerMode"/> 为“Scanner”时使用。
        /// </summary>
        [XmlElement("Y2串口扫码器")]
        public Scanner.ScannerModel Station2ScannerModel { get; set; } = new Scanner.ScannerModel();

        /// <summary>
        /// Y2 HF800 网络扫码器参数，仅在 <see cref="Station2ScannerMode"/> 为“HF800”时使用。
        /// </summary>
        [XmlElement("Y2霍尼韦尔扫码器")]
        public Honeywell.HF800 Station2HF800 { get; set; } = new Honeywell.HF800();

        /// <summary>
        /// Y2 扫码成功后写入产品码的起始地址，单位为 D 寄存器编号，内容格式为“SN;工单号”。
        /// 信号表约定为 D950；仅服务进站写码。流程结束归档回读耐压2镜像 AddressSN+50（默认 D850），不读本地址。
        /// </summary>
        [XmlElement("Y2扫码SN地址D")]
        public int Station2ScanSnAddress { get; set; } = 950;

        /// <summary>
        /// Y2 自动扫码触发地址，单位为 D 寄存器编号；PLC 写入 1 时触发一次 Y2 读码。
        /// </summary>
        [XmlElement("Y2扫码触发地址D")]
        public int Station2ScanTriggerAddress { get; set; } = 1120;

        /// <summary>
        /// Y2 扫码业务结果地址，单位为 D 寄存器编号；上位机写入 1 表示完整成功，2 表示读码或业务校验失败。
        /// </summary>
        [XmlElement("Y2扫码返回地址D")]
        public int Station2ScanResultAddress { get; set; } = 1121;

        /// <summary>
        /// Y1 流程结束触发地址，单位为 D 寄存器编号；PLC 写入 1 后，上位机结算本工位实测结果并写 M3051，
        /// 随后释放工位并在后台一次性归档，归档失败不影响下一件产品扫码作业。
        /// </summary>
        [XmlElement("Y1流程结束地址D")]
        public int Station1FlowEndAddress { get; set; } = 1020;

        /// <summary>
        /// Y2 流程结束触发地址，单位为 D 寄存器编号；PLC 写入 1 后，上位机结算本工位实测结果并写 M3052，
        /// 随后释放工位并在后台一次性归档，归档失败不影响下一件产品扫码作业。
        /// </summary>
        [XmlElement("Y2流程结束地址D")]
        public int Station2FlowEndAddress { get; set; } = 1021;

        /// <summary>
        /// Y1 工位总结果线圈地址，单位为 M 寄存器编号。
        /// D1020 结算触发后写入：1 表示本工位耐压、阻值、压力综合合格，0 表示任一实测项目 NG；
        /// 点检期望命中和后台归档状态均不改变该线圈。
        /// </summary>
        [XmlElement("Y1工位总结果线圈M")]
        public int Station1TotalResultCoilAddress { get; set; } = 3051;

        /// <summary>
        /// Y2 工位总结果线圈地址，单位为 M 寄存器编号。
        /// D1021 结算触发后写入：1 表示本工位耐压、阻值、压力综合合格，0 表示任一实测项目 NG；
        /// 与 Y1 的 M3051 按工位隔离，点检期望命中和后台归档状态均不改变该线圈。
        /// </summary>
        [XmlElement("Y2工位总结果线圈M")]
        public int Station2TotalResultCoilAddress { get; set; } = 3052;

        /// <summary>
        /// Y2 压力平均值起始地址，单位为 D 寄存器编号。平均、最大、最小压力均为 uint32，
        /// 依次占用 D1630-D1631、D1632-D1633、D1634-D1635；Y1 继续使用标准压力起始地址配置。
        /// </summary>
        [XmlElement("Y2压力起始地址D")]
        public int Station2PressureAddress { get; set; } = 1630;

        /// <summary>
        /// 补齐旧版本或人工删减 XML 中缺失的对象与说明。该方法只修复配置结构，不改写现场地址和设备参数。
        /// </summary>
        public void EnsureDefaults()
        {
            ConfigurationVersion = System.Math.Max(ConfigurationVersion, 3);
            BusinessDescription = BusinessDescription ?? new DualYConfigurationDescription();
            if (string.IsNullOrWhiteSpace(BusinessDescription.InspectionBoundary))
            {
                BusinessDescription.InspectionBoundary =
                    "双Y只认配置数据.xml中的耐压点检OK/NG码；IR/AOI点检码在双Y扫码入口拦截。工位总结果按实测写M3051/M3052；点检期望命中仅日志与小屏展示。M3041/M3042仍按耐压结束写入以兼容旧链路。";
            }
            if (string.IsNullOrWhiteSpace(BusinessDescription.ArchiveBoundary)
                || BusinessDescription.ArchiveBoundary.IndexOf("M3051", StringComparison.Ordinal) < 0
                || BusinessDescription.ArchiveBoundary.IndexOf("归档失败", StringComparison.Ordinal) >= 0)
            {
                BusinessDescription.ArchiveBoundary =
                    "Y1、Y2流程结束信号先结算耐压、阻值和压力，并按实测结果写M3051/M3052；写完即释放工位供下一件扫码。MES保存和正常产品报工在后台执行一次，失败只记日志；耐压点检只留档不报工。";
            }
            Station1ScannerModel = Station1ScannerModel ?? new Scanner.ScannerModel();
            Station1HF800 = Station1HF800 ?? new Honeywell.HF800();
            Station2ScannerModel = Station2ScannerModel ?? new Scanner.ScannerModel();
            Station2HF800 = Station2HF800 ?? new Honeywell.HF800();
            if (Station2PressureAddress <= 0)
            {
                Station2PressureAddress = 1630;
            }
            if (Station1TotalResultCoilAddress <= 0)
            {
                Station1TotalResultCoilAddress = 3051;
            }
            if (Station2TotalResultCoilAddress <= 0)
            {
                Station2TotalResultCoilAddress = 3052;
            }
        }

        /// <summary>
        /// 从旧“配置数据.xml”提取双Y部署和扫码参数，用于首次升级生成独立配置文件。
        /// 普通扫码配置保留在原模型中，后续双Y运行只读取返回的专用副本。
        /// </summary>
        /// <param name="legacy">已经从旧系统配置加载的设置模型。</param>
        /// <returns>可直接保存到“配置\双Y电测配置.xml”的独立配置。</returns>
        public static DualYElectricalTestConfiguration CreateFromLegacy(SettingModel legacy)
        {
            var result = new DualYElectricalTestConfiguration();
            if (legacy == null)
            {
                return result;
            }

            result.DeploymentEnabled = legacy.SETTING_DATA?.DualYElectricalTestDeployment ?? false;
            result.ElectricalTestModeCoilAddress = legacy.DualYElectricalTestModeCoilAddress;
            result.Station1ScannerMode = legacy.ScannerMode;
            result.Station1ScannerModel = CopyScanner(legacy.ScannerModel);
            result.Station1HF800 = CopyHf800(legacy.HF800);
            result.Station1ScanSnAddress = legacy.AddressSN;
            result.Station1ScanTriggerAddress = legacy.AddressStart;
            result.Station1ScanResultAddress = legacy.AddressStart + 1;
            result.Station2ScannerMode = legacy.DualYStation2ScannerMode;
            result.Station2ScannerModel = CopyScanner(legacy.DualYStation2ScannerModel);
            result.Station2HF800 = CopyHf800(legacy.DualYStation2HF800);
            result.Station2ScanSnAddress = legacy.DualYStation2ScanSnAddress;
            result.Station2ScanTriggerAddress = legacy.DualYStation2ScanTrigAddress;
            result.Station2ScanResultAddress = legacy.DualYStation2ScanResultAddress;
            result.Station1FlowEndAddress = legacy.DualYStation1FlowEndAddress;
            result.Station2FlowEndAddress = legacy.DualYStation2FlowEndAddress;
            result.Station2PressureAddress = 1630;
            return result;
        }

        /// <summary>
        /// 复制旧配置中的串口扫码参数，保证独立配置后续修改不会回写普通扫码对象。
        /// </summary>
        /// <param name="source">旧配置中的串口扫码器参数；空值按扫码器默认参数迁移。</param>
        /// <returns>供双Y独立配置持有的串口扫码器参数副本。</returns>
        private static Scanner.ScannerModel CopyScanner(Scanner.ScannerModel source)
        {
            return source == null
                ? new Scanner.ScannerModel()
                : new Scanner.ScannerModel
                {
                    PortName = source.PortName,
                    keeptime = source.keeptime
                };
        }

        /// <summary>
        /// 复制旧配置中的 HF800 网络参数，保证 Y1/Y2 独立维护后与普通扫码器运行对象互不影响。
        /// </summary>
        /// <param name="source">旧配置中的 HF800 参数；空值按扫码器默认参数迁移。</param>
        /// <returns>供双Y独立配置持有的 HF800 参数副本。</returns>
        private static Honeywell.HF800 CopyHf800(Honeywell.HF800 source)
        {
            return source == null
                ? new Honeywell.HF800()
                : new Honeywell.HF800
                {
                    IP_address = source.IP_address,
                    port = source.port,
                    receivetimeout = source.receivetimeout
                };
        }
    }

    /// <summary>
    /// 双Y配置文件内的固定业务说明，供现场维护人员直接查看各类参数的作用边界。
    /// </summary>
    public class DualYConfigurationDescription
    {
        /// <summary>
        /// 说明启动窗口选型与 PLC 产品流向的职责边界，供现场修改部署开关时核对影响范围。
        /// </summary>
        [XmlElement("部署边界")]
        public string DeploymentBoundary { get; set; } = "双Y专用部署决定启动窗口和视觉资源是否加载；PLC模式线圈决定运行中的产品业务流向。";

        /// <summary>
        /// 说明普通扫码、双Y自动扫码和手动扫码的配置归属及共用业务链。
        /// </summary>
        [XmlElement("扫码边界")]
        public string ScannerBoundary { get; set; } =
            "Y1、Y2自动扫码器只服务双Y机台；普通产线扫码器继续由配置数据.xml管理。手动扫码与自动扫码共用MES校验、建账和PLC反馈。双Y点检仅放行耐压OK/NG码。";

        /// <summary>
        /// 说明双Y流程结束信号、实测结果写回和后台归档的业务边界。
        /// </summary>
        [XmlElement("归档边界")]
        public string ArchiveBoundary { get; set; } =
            "Y1、Y2流程结束信号先结算耐压、阻值和压力，并按实测结果写M3051/M3052；写完即释放工位供下一件扫码。MES保存和正常产品报工在后台执行一次，失败只记日志；耐压点检只留档不报工。";

        /// <summary>
        /// 说明两工位压力快照的地址和数据宽度，供 PLC 与上位机联调时核对寄存器分配。
        /// </summary>
        [XmlElement("压力边界")]
        public string PressureBoundary { get; set; } = "Y1使用配置数据.xml中的压力起始地址；Y2使用本文件的Y2压力起始地址。平均、最大、最小压力均为uint32并各占两个D寄存器。";

        /// <summary>
        /// 说明 XML 地址字段的 PLC 单位和填写格式，避免现场将完整地址字符串重复写入数值节点。
        /// </summary>
        [XmlElement("地址单位")]
        public string AddressUnits { get; set; } = "名称末尾M表示线圈编号，末尾D表示数据寄存器编号；XML中填写纯数字。";

        /// <summary>
        /// 说明双Y耐压点检与普通产线点检、IR/AOI 点检的边界，供现场配置点检码时核对。
        /// </summary>
        [XmlElement("点检边界")]
        public string InspectionBoundary { get; set; } =
            "双Y只认配置数据.xml中的耐压点检OK/NG码；IR/AOI点检码在双Y扫码入口拦截。工位总结果按实测写M3051/M3052；点检期望命中仅日志与小屏展示。M3041/M3042仍按耐压结束写入以兼容旧链路。";
    }
}
