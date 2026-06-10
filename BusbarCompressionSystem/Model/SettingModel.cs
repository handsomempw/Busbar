/*
 * =====================================================================
 * 系统配置模型（混合模式）
 * =====================================================================
 * 
 * 【设计说明】
 * 本类采用"配置参数 + 硬件设备"混合模式，将可序列化的配置参数和运行时设备对象放在同一个类中管理。
 * 
 * 【为什么这样设计】
 * 优点：
 * - 配置和设备一一对应，便于理解和维护
 * - 支持XML序列化，配置可持久化到文件
 * - 全局访问方便，通过 DataModel.Settingmodel.XXX 即可访问
 * - 修改配置界面时，直接绑定到属性即可
 * 
 * 【序列化说明】
 * - 带 [XmlElement("名称")] 的属性会被保存到XML配置文件
 * - 带 [XmlIgnore] 的属性不会被保存（运行时临时对象）
 * - 软件启动时从XML加载配置，关闭时保存配置
 * 
 * 【包含内容】
 * 1. Halcon显示窗口（HWindow1-5）：运行时对象，不序列化
 * 2. 相机设备（camedata1-5）：运行时对象，不序列化
 * 3. 扫码器配置：序列化保存
 * 4. PLC通信配置：序列化保存
 * 5. 耐压仪设备（AT9620_1-3）：配置+测试方法，序列化保存配置部分
 * 6. 机器人通信配置：序列化保存
 * 7. 系统参数（SETTING_DATA）：序列化保存
 * 
 * 【注意事项】
 * - 修改配置后需要重启软件或重新加载配置才能生效
 * - 设备对象（如AT9620）包含业务方法，不是纯数据类
 * - 运行时对象在软件启动时需要手动初始化
 * =====================================================================
 */

using BusbarCompressionSystem.Model.Setting;
using BusbarCompressionSystem.Model.Setting1;
using GalaSoft.MvvmLight;
using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Xml.Serialization;

namespace BusbarCompressionSystem.Model
{
    /// <summary>
    /// 系统配置模型（混合模式：配置参数 + 硬件设备）
    /// </summary>
    /// <remarks>
    /// 管理所有硬件设备配置和系统参数，支持XML序列化持久化。
    /// 通过 DataModel.Settingmodel 全局访问。
    /// </remarks>
    public class SettingModel : ObservableObject
    {
        #region ==================== Halcon显示窗口（运行时对象，不序列化） ====================

        /// <summary>
        /// Halcon图像显示窗口1（拍照留底工位-相机1）
        /// </summary>
        /// <remarks>运行时由界面控件初始化，用于显示相机采集的图像</remarks>
        [XmlIgnore]
        public HWindow HWindow1 { get; set; } = null;

        /// <summary>
        /// Halcon图像显示窗口2（拍照留底工位-相机2）
        /// </summary>
        [XmlIgnore]
        public HWindow HWindow2 { get; set; } = null;

        /// <summary>
        /// Halcon图像显示窗口3（拍照留底工位-相机3）
        /// </summary>
        [XmlIgnore]
        public HWindow HWindow3 { get; set; } = null;

        /// <summary>
        /// Halcon图像显示窗口4（AOI外观检测工位）
        /// </summary>
        [XmlIgnore]
        public HWindow HWindow4 { get; set; } = null;

        /// <summary>
        /// Halcon图像显示窗口5（预留）
        /// </summary>
        [XmlIgnore]
        public HWindow HWindow5 { get; set; } = null;

        #endregion

        #region ==================== 相机设备（运行时对象，不序列化） ====================

        /// <summary>
        /// 相机1数据对象（拍照留底工位）
        /// </summary>
        /// <remarks>
        /// 包含相机连接、采集、触发等功能。
        /// 运行时初始化，不保存到配置文件。
        /// </remarks>
        [XmlIgnore]
        [XmlElement("相机配置1")]
        public Camera.DATA camedata1 { set; get; } = new Camera.DATA();

        /// <summary>
        /// 相机2数据对象（拍照留底工位）
        /// </summary>
        [XmlIgnore]
        [XmlElement("相机配置2")]
        public Camera.DATA camedata2 { set; get; } = new Camera.DATA();

        /// <summary>
        /// 相机3数据对象（拍照留底工位）
        /// </summary>
        [XmlIgnore]
        [XmlElement("相机配置3")]
        public Camera.DATA camedata3 { set; get; } = new Camera.DATA();

        /// <summary>
        /// 相机4数据对象（AOI外观检测工位）
        /// </summary>
        [XmlIgnore]
        [XmlElement("相机配置4")]
        public Camera.DATA camedata4 { set; get; } = new Camera.DATA();

        /// <summary>
        /// 相机5数据对象（预留）
        /// </summary>
        [XmlIgnore]
        [XmlElement("相机配置5")]
        public Camera.DATA camedata5 { set; get; } = new Camera.DATA();

        #endregion

        #region ==================== 图像保存配置（序列化保存） ====================

        /// <summary>
        /// 照片保存设置（保存路径、是否保存OK/NG图片等）
        /// </summary>
        [XmlElement("照片保存设置")]
        public ImageSaveSetting ImageSaveSetting { get; set; } = new ImageSaveSetting();

        #endregion

        #region ==================== 扫码器配置（序列化保存） ====================

        /// <summary>
        /// 上料位扫码器（通用串口扫码器）
        /// </summary>
        [XmlElement("扫码器")]
        public Scanner.ScannerModel ScannerModel { set; get; } = new Scanner.ScannerModel();

        /// <summary>
        /// 上料位扫码器（霍尼韦尔HF800型号）
        /// </summary>
        [XmlElement("霍尼韦尔扫码器")]
        public Honeywell.HF800 HF800 { set; get; } = new Honeywell.HF800();

        /// <summary>
        /// 下料位扫码器（通用串口扫码器）
        /// </summary>
        [XmlElement("下料扫码器")]
        public Scanner.ScannerModel SecondScannerModel { set; get; } = new Scanner.ScannerModel();

        /// <summary>
        /// 下料位扫码器（霍尼韦尔HF800型号）
        /// </summary>
        [XmlElement("下料霍尼韦尔扫码器")]
        public Honeywell.HF800 SecondHF800 { set; get; } = new Honeywell.HF800();

        /// <summary>
        /// 上料扫码器模式（"HF800" 或 "Scanner"）
        /// 配置文件节点沿用属性名 ScannerMode，与下料扫码器模式并列。
        /// </summary>
        public string ScannerMode { set; get; } = "HF800";

        /// <summary>
        /// 下料扫码器模式（"HF800" 或 "Scanner"）
        /// </summary>
        [XmlElement("下料扫码器模式")]
        public string SecondScannerMode { set; get; } = "HF800";

        #endregion

        #region ==================== PLC通信配置（序列化保存） ====================

        /// <summary>
        /// PLC 通讯 IP 地址（Modbus TCP）。
        /// 现场更换控制器或切换网段时，直接调整这一项即可完成目标站点切换。
        /// </summary>
        [XmlElement("PLC通讯IP")]
        public string PLC_IP { set; get; } = "192.168.1.88";

        /// <summary>
        /// PLC 通讯端口（Modbus TCP）。
        /// 默认值 502 对应标准 Modbus 端口，与 <see cref="PLC_IP"/> 共同组成连接参数。
        /// </summary>
        [XmlElement("PLC通讯端口")]
        public int PLC_Port { set; get; } = 502;

        /// <summary>
        /// PLC 信号交互起始地址（D 寄存器）。
        /// 从这一组连续寄存器开始承载扫码、拍照、耐压触发和完成反馈。
        /// </summary>
        /// <remarks>
        /// 地址组用途：
        /// - AddressStart+0: 扫码触发
        /// - AddressStart+1: 拍照触发
        /// - AddressStart+2: 耐压触发
        /// - AddressStart+3: 拍照完成反馈
        /// 具体定义对应 PLC 程序中的固定信号组。
        /// </remarks>
        [XmlElement("信号交互地址")]
        public int AddressStart { set; get; } = 1000;

        /// <summary>
        /// 产品 SN 起始地址（D 寄存器，字符串类型）。
        /// 上料、耐压1、耐压2工位通过固定偏移共享这一基址。
        /// </summary>
        /// <remarks>
        /// 地址组用途：
        /// - AddressSN: 拍照工位 SN
        /// - AddressSN+25: 耐压 1 工位 SN
        /// - AddressSN+50: 耐压 2 工位 SN
        /// PLC 写入格式为 "SN;工单号"。
        /// </remarks>
        [XmlElement("SN起始地址")]
        public int AddressSN { set; get; } = 800;

        /// <summary>
        /// 阻值结果起始地址（D 寄存器，Float 类型）。
        /// PLC 使用连续寄存器写入三个阻值点的数据。
        /// </summary>
        /// <remarks>
        /// 地址组用途：
        /// - AddressRes: 阻值 1（D1200-D1201，Float 占 2 个字）
        /// - AddressRes+2: 阻值2
        /// - AddressRes+4: 阻值3
        /// </remarks>
        [XmlElement("阻值起始地址")]
        public int AddressRes { set; get; } = 1200;

        /// <summary>
        /// 压力监控起始地址（D 寄存器）。
        /// 压接过程的平均、最大、最小压力使用这一组连续寄存器。
        /// </summary>
        /// <remarks>
        /// 地址组用途：
        /// - AddressPressure: 平均压力
        /// - AddressPressure+2: 最大压力
        /// - AddressPressure+4: 最小压力
        /// </remarks>
        [XmlElement("压力起始地址")]
        public int AddressPressure { set; get; } = 1600;

        /// <summary>
        /// IR 绝缘电阻测试触发地址（D 寄存器，PLC -> PC）。
        /// PLC 使用 1/2 作为启动和停止指令。
        /// </summary>
        /// <remarks>
        /// D1014：
        /// 1 = 启动绝缘电阻测试
        /// 2 = 停止绝缘电阻测试
        /// </remarks>
        [XmlElement("IR测试触发地址")]
        public int IRTrigAddress { get; set; } = 1014;

        /// <summary>
        /// IR 绝缘电阻测试结果地址（D 寄存器，PC -> PLC）。
        /// 上位机把 OK / NG 结果写回 PLC，供后续节拍判断。
        /// </summary>
        /// <remarks>
        /// D1015：
        /// 1 = OK
        /// 2 = NG
        /// </remarks>
        [XmlElement("IR测试结果地址")]
        public int IRResultAddress { get; set; } = 1015;

        /// <summary>
        /// IR 仪器可用标志地址（M 寄存器，PLC -> PC）。
        /// PLC 通过 1 / 0 提示第三电测位当前的可用状态。
        /// </summary>
        /// <remarks>
        /// M3033：
        /// 1 = 仪器开启 / 可用
        /// 0 = 仪器关闭 / 可用状态解除
        /// </remarks>
        [XmlElement("IR仪器启用地址")]
        public int IRMeterAvailableAddress { get; set; } = 3033;

        /// <summary>
        /// TV1 耐压结果反馈地址（用于决定 IR 流程放行）。
        /// PLC 读取这项结果后，决定第三电测位是否进入 IR 逻辑。
        /// </summary>
        /// <remarks>
        /// M3041：0=NG，1=OK
        /// </remarks>
        [XmlElement("IR启用判断-TV1耐压结果地址")]
        public int IrEnableByTv1ResultAddress { get; set; } = 3041;

        /// <summary>
        /// TV2 耐压结果反馈地址（用于决定 IR 流程放行）。
        /// PLC 读取这项结果后，决定第三电测位是否进入 IR 逻辑。
        /// </summary>
        /// <remarks>
        /// M3042：0=NG，1=OK
        /// </remarks>
        [XmlElement("IR启用判断-TV2耐压结果地址")]
        public int IrEnableByTv2ResultAddress { get; set; } = 3042;

        /// <summary>
        /// 压力上限参数地址。
        /// PLC 读取这一项作为压力超限判定基准。
        /// </summary>
        [XmlElement("压力上限地址")]
        public int MaxPressure_Address { set; get; } = 2038;

        /// <summary>
        /// 压力下限参数地址。
        /// PLC 读取这一项作为压力达标判定基准。
        /// </summary>
        [XmlElement("压力下限地址")]
        public int MinPressure_Address { set; get; } = 2048;

        /// <summary>
        /// 实时压力显示地址。
        /// PLC 使用这一项传递当前压力值，供上位机显示与记录。
        /// </summary>
        [XmlElement("压力地址")]
        public int Pressure_Address { set; get; } = 2054;

        /// <summary>
        /// 耐压仪 1 启用状态地址（M 寄存器，Bool 类型）。
        /// 现场口径与耐压 2 保持一致，PLC 用 1 / 0 表达可用状态。
        /// </summary>
        [XmlElement("仪器1启用地址")]
        public int Meter1AvailableAddress { set; get; } = 3012;

        /// <summary>
        /// 耐压仪 2 启用状态地址。
        /// 现场口径与耐压 1 保持一致，PLC 用 1 / 0 表达可用状态。
        /// </summary>
        [XmlElement("仪器2启用地址")]
        public int Meter2AvailableAddress { set; get; } = 3022;

        /// <summary>
        /// 耐压仪 3 启用状态地址（M 寄存器，Bool 类型）。
        /// TV3 与 IR 共用第三电测位，PLC 按产品节拍保持互斥。
        /// </summary>
        [XmlElement("仪器3启用地址")]
        public int Meter3AvailableAddress { set; get; } = 3032;

        /// <summary>
        /// 阻值 1 读取触发地址（M 寄存器，Bool 类型）。
        /// PLC 置位后，上位机读取阻值 1 数据。
        /// </summary>
        [XmlElement("阻值1触发地址")]
        public int Res1TrigAddress { set; get; } = 3035;

        /// <summary>
        /// 阻值 2 读取触发地址。
        /// PLC 置位后，上位机读取阻值 2 数据。
        /// </summary>
        [XmlElement("阻值2触发地址")]
        public int Res2TrigAddress { set; get; } = 3036;

        /// <summary>
        /// 阻值 3 读取触发地址。
        /// PLC 置位后，上位机读取阻值 3 数据。
        /// </summary>
        [XmlElement("阻值3触发地址")]
        public int Res3TrigAddress { set; get; } = 3037;

        /// <summary>
        /// 阻值上限参数地址（用于判定阻值是否超标，默认 14 μΩ）。
        /// PLC 读取这一项作为阻值上限判定基准。
        /// </summary>
        [XmlElement("阻值上限地址")]
        public int Res_Max_Address { set; get; } = 2020;

        /// <summary>
        /// 阻值下限参数地址。
        /// PLC 读取这一项作为阻值下限判定基准。
        /// </summary>
        [XmlElement("阻值下限地址")]
        public int Res_Min_Address { set; get; } = 2022;

        /// <summary>
        /// 设备允许启动状态地址（上位机写入 PLC）。
        /// 这一项用于告知 PLC 可以进入生产节拍。
        /// </summary>
        [XmlElement("设备是否允许启动")]
        public int DeviceAvailableAddress { set; get; } = 1620;

        /// <summary>
        /// 设备心跳地址（上位机定时翻转，PLC 监控通信状态）。
        /// 这一项用于维持联机心跳和掉线判断。
        /// </summary>
        [XmlElement("设备心跳地址")]
        public int ShankHandAddress { set; get; } = 1622;

        /// <summary>
        /// AOI NG 点检通过信号地址（M 寄存器，Bool 类型）。
        /// 当全部参与判定的 AOI 工具均为 NG 或 NG2 时，上位机向该地址写入通过信号。
        /// </summary>
        /// <remarks>
        /// 用于 AOI NG 点检场景。
        /// </remarks>
        [XmlElement("AOI_NG点检通过信号地址")]
        public int AOI_NG_InspectionAddress { set; get; } = 3040;

        /// <summary>
        /// 报工失败后报警信号地址（M 寄存器线圈，Bool）。
        /// </summary>
        /// <remarks>
        /// 报工失败且「报工失败报警使能」为 true 时上位机写 1；清零由 PLC/现场处理，上位机不复位。
        /// 使能为 false 时不写该线圈、不弹窗。
        /// </remarks>
        [XmlElement("报工失败报警线圈")]
        public int ReportFailBlockCoilAddress { get; set; } = 3045;

        /// <summary>
        /// 下料位扫码触发地址。
        /// PLC 置位后，上位机读取下料位条码。
        /// </summary>
        [XmlElement("下料扫码触发地址")]
        public int SecondScanTrigAddress { set; get; } = 1100;

        /// <summary>
        /// 下料位扫码结果反馈地址。
        /// 上位机把解析后的扫码结果写回 PLC。
        /// </summary>
        [XmlElement("下料扫码返回地址")]
        public int SecondScanResultAddress { set; get; } = 1101;

        /// <summary>
        /// 下料位产品 SN 地址。
        /// PLC 在这一组寄存器中保存下料位产品标识。
        /// </summary>
        [XmlElement("下料位SN地址")]
        public int SecondScanSNAddress { set; get; } = 1150;

        /// <summary>
        /// 测试模式信号地址（D 寄存器，uint16 类型）。
        /// 上位机写入这一项后，PLC 进入对应的电测顺序。
        /// </summary>
        /// <remarks>
        /// 上位机向 PLC 写入测试模式值：
        /// - 0: 只测交流(ACW Only)
        /// - 1: 只测直流(DCW Only)
        /// - 2: 先交后直(ACW then DCW)
        /// - 3: 先直后交(DCW then ACW)
        /// 默认地址D1012
        /// </remarks>
        [XmlElement("测试模式地址")]
        public int TestModeAddress { set; get; } = 1012;

        /// <summary>
        /// 当前测试模式配置值。
        /// 系统启动或配置变更后，将这一项写入 PLC 的测试模式地址。
        /// </summary>
        /// <remarks>
        /// 这一项与 PLC 的 <see cref="TestModeAddress"/> 保持一致。
        /// </remarks>
        private AT9620.ElectricalTestMode _currentTestMode = AT9620.ElectricalTestMode.ACWOnly;

        [XmlElement("当前测试模式")]
        public AT9620.ElectricalTestMode CurrentTestMode 
        { 
            get { return _currentTestMode; }
            set 
            { 
                _currentTestMode = value;
                RaisePropertyChanged(() => CurrentTestMode);
                RaisePropertyChanged(() => CurrentTestModeDisplay);
            }
        }

        /// <summary>
        /// 当前测试模式的中文显示名称（用于界面绑定）。
        /// 这一项只负责界面展示。
        /// </summary>
        [XmlIgnore]
        public string CurrentTestModeDisplay
        {
            get
            {
                switch (CurrentTestMode)
                {
                    case AT9620.ElectricalTestMode.ACWOnly:
                        return "只测交流";
                    case AT9620.ElectricalTestMode.DCWOnly:
                        return "只测直流";
                    case AT9620.ElectricalTestMode.ACWThenDCW:
                        return "先交后直";
                    case AT9620.ElectricalTestMode.DCWThenACW:
                        return "先直后交";
                    default:
                        return "未知模式";
                }
            }
        }

        #endregion

        #region ==================== 机器人通信配置 ====================

        /// <summary>
        /// 机器人 TCP 通信配置（IP、端口）。
        /// 这一项保存机器人联机参数，供工位联动时调用。
        /// </summary>
        [XmlElement("机器人")]
        public TcpSetting RobotConnect { set; get; } = new TcpSetting();

        /// <summary>
        /// 机器人TCP服务器对象（运行时对象，不序列化）
        /// </summary>
        /// <remarks>
        /// 上位机作为TCP Server，机器人作为Client连接。
        /// 用于接收机器人的CHECK1/CHECK2请求，返回OK/NG结果。
        /// </remarks>
        [XmlIgnore]
        public TcpServerHelper.TCPServerH TcpServerRobot { set; get; } = new TcpServerHelper.TCPServerH();

        #endregion

        #region ==================== 耐压/绝缘测试设备（配置+设备对象，部分序列化） ====================

        /// <summary>
        /// 耐压仪1（工位1使用）
        /// </summary>
        /// <remarks>
        /// 【设备类型】安规测试仪 AT9620
        /// 【通信方式】TCP/IP（默认端口2000）
        /// 【序列化内容】IP、端口、测试参数（TVParameter）
        /// 【主要方法】
        /// - Start(): 启动耐压测试，阻塞等待测试完成
        /// - Download(): 下发测试参数到设备
        /// - stop: 设置为true可中止测试
        /// 【调用位置】当前 PLC 主路径由 TV1Process_ACW()/TV1Process_DCW() 调用；TV1Process() 仅保留历史兼容入口
        /// </remarks>
        [XmlElement("耐压1")]
        public AT9620.AT9620 AT9620_1 = new AT9620.AT9620();

        /// <summary>
        /// 耐压仪2（工位2使用）
        /// </summary>
        /// <remarks>配置和功能同AT9620_1；当前 PLC 主路径由 TV2Process_ACW()/TV2Process_DCW() 调用，TV2Process() 仅保留历史兼容入口。</remarks>
        [XmlElement("耐压2")]
        public AT9620.AT9620 AT9620_2 = new AT9620.AT9620();

        /// <summary>
        /// 耐压仪3（工位3使用，可选）
        /// </summary>
        /// <remarks>配置和功能同AT9620_1；当前 PLC 主路径由 TV3Process_ACW()/TV3Process_DCW() 调用，TV3Process() 仅保留历史兼容入口。</remarks>
        [XmlElement("耐压3")]
        public AT9620.AT9620 AT9620_3 = new AT9620.AT9620();

        /// <summary>
        /// 绝缘电阻仪（工位3使用）
        /// </summary>
        /// <remarks>
        /// 【设备类型】绝缘电阻测试仪 AT6835FL
        /// 【通信方式】串口（默认9600,8,N,1）
        /// 【序列化内容】串口号、波特率、测试参数（IRParameter）
        /// 【主要方法】
        /// - Start(): 启动绝缘电阻测试，阻塞等待测试完成
        /// - Download(): 下发测试参数到设备
        /// - stop: 设置为true可中止测试并放电
        /// 【调用位置】IRProcess() 中调用
        /// </remarks>
        [XmlElement("绝缘电阻仪")]
        public AT6835FL.AT6835FL AT6835FL_1 = new AT6835FL.AT6835FL();

        #endregion

        #region ==================== 系统参数配置（序列化保存） ====================

        /// <summary>
        /// 系统全局配置参数
        /// </summary>
        /// <remarks>
        /// 包含：工站代码、设备ID、MES接口配置、点检SN码等
        /// </remarks>
        public SETTING_DATA SETTING_DATA { set; get; } = new SETTING_DATA();

        #endregion

        #region ==================== 数据库连接（运行时对象，不序列化） ====================

        /// <summary>
        /// SQL Server数据库连接对象
        /// </summary>
        /// <remarks>运行时初始化，用于MES数据交互</remarks>
        [XmlIgnore]
        public F7DataBase.Sqlserver Sqlserver { set; get; } = new F7DataBase.Sqlserver();

        #endregion
    }

    #region ==================== 辅助类型定义 ====================

    /// <summary>
    /// IO信号状态枚举
    /// </summary>
    public enum IOstatus
    {
        连接失败,
        高电平,
        低电平
    }

    /// <summary>
    /// 耐压仪型号枚举
    /// </summary>
    public enum TVMeterType
    {
        /// <summary>安规测试仪 AT9620</summary>
        AT9620,
        /// <summary>安规测试仪 SE7450</summary>
        SE7450
    }

    /// <summary>
    /// IO信号显示模型（用于界面绑定）
    /// </summary>
    /// <remarks>
    /// 根据IO状态自动计算背景色和前景色：
    /// - -1（未知）：白底黑字
    /// - 0（低电平）：黑底白字
    /// - 1（高电平）：绿底红字
    /// </remarks>
    public class IO : ObservableObject
    {

        private int _IOstatus = -1;

        /// <summary>
        /// IO状态值（-1:未知, 0:低电平, 1:高电平）
        /// </summary>
        [XmlIgnore]
        public int IOstatus
        {
            get
            {
                return _IOstatus;
            }
            set
            {
                _IOstatus = value;
                RaisePropertyChanged(() => IOstatusBackGround);
                RaisePropertyChanged(() => IOStatusForeGround);
            }
        }

        /// <summary>
        /// 根据IO状态返回背景色（用于界面绑定）
        /// </summary>
        [XmlIgnore]
        public SolidColorBrush IOstatusBackGround
        {
            get
            {
                if (_IOstatus == -1)
                {
                    return Brushes.WhiteSmoke;
                }
                else if (_IOstatus == 0)
                {
                    return Brushes.Black;
                }
                else
                {
                    return Brushes.GreenYellow;
                }
            }
        }

        /// <summary>
        /// 根据IO状态返回前景色（用于界面绑定）
        /// </summary>
        public SolidColorBrush IOStatusForeGround
        {
            get
            {
                if (_IOstatus == -1)
                {
                    return Brushes.Black;
                }
                else if (_IOstatus == 0)
                {
                    return Brushes.White;
                }
                else
                {
                    return Brushes.OrangeRed;
                }

            }
        }
    }

    /// <summary>
    /// 耐压状态码映射（设备返回码 → 显示文本）
    /// </summary>
    public class TvStatusMapping
    {
        /// <summary>设备返回的状态码</summary>
        public string Code { get; set; }
        /// <summary>界面显示的文本</summary>
        public string Display { get; set; }
    }

    #endregion
}
