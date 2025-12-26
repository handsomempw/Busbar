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
        /// PLC通讯IP地址（Modbus TCP）
        /// </summary>
        [XmlElement("PLC通讯IP")]
        public string PLC_IP { set; get; } = "127.0.0.1";

        /// <summary>
        /// PLC通讯端口（默认502为Modbus标准端口）
        /// </summary>
        [XmlElement("PLC通讯端口")]
        public int PLC_Port { set; get; } = 502;

        /// <summary>
        /// 信号交互起始地址（D寄存器）
        /// </summary>
        /// <remarks>
        /// 从此地址开始的连续寄存器用于上位机与PLC的信号交互：
        /// - AddressStart+0: 扫码触发
        /// - AddressStart+1: 拍照触发
        /// - AddressStart+2: 耐压触发
        /// - AddressStart+3: 拍照完成反馈
        /// 具体定义参见PLC程序文档
        /// </remarks>
        [XmlElement("信号交互地址")]
        public int AddressStart { set; get; } = 1000;

        /// <summary>
        /// 产品SN码起始地址（D寄存器，字符串类型）
        /// </summary>
        /// <remarks>
        /// PLC将产品SN码写入此地址，格式为"SN;工单号"
        /// 不同工位使用不同偏移：
        /// - AddressSN: 拍照工位SN
        /// - AddressSN+25: 耐压1工位SN
        /// - AddressSN+50: 耐压2工位SN
        /// </remarks>
        [XmlElement("SN起始地址")]
        public int AddressSN { set; get; } = 800;

        /// <summary>
        /// 阻值数据起始地址（D寄存器，Float类型）
        /// </summary>
        /// <remarks>
        /// PLC将阻值测试结果写入此地址：
        /// - AddressRes: 阻值1（D1200-D1201，Float占2个字）
        /// - AddressRes+2: 阻值2
        /// - AddressRes+4: 阻值3
        /// </remarks>
        [XmlElement("阻值起始地址")]
        public int AddressRes { set; get; } = 1200;

        /// <summary>
        /// 压力数据起始地址（D寄存器）
        /// </summary>
        /// <remarks>
        /// 压接过程的压力监控数据：
        /// - AddressPressure: 平均压力
        /// - AddressPressure+2: 最大压力
        /// - AddressPressure+4: 最小压力
        /// </remarks>
        [XmlElement("压力起始地址")]
        public int AddressPressure { set; get; } = 1600;

        /// <summary>
        /// 压力上限参数地址（用于判定压力是否超标）
        /// </summary>
        [XmlElement("压力上限地址")]
        public int MaxPressure_Address { set; get; } = 2038;

        /// <summary>
        /// 压力下限参数地址（用于判定压力是否达标）
        /// </summary>
        [XmlElement("压力下限地址")]
        public int MinPressure_Address { set; get; } = 2048;

        /// <summary>
        /// 实时压力显示地址
        /// </summary>
        [XmlElement("压力地址")]
        public int Pressure_Address { set; get; } = 2054;

        /// <summary>
        /// 耐压仪1启用状态地址（M寄存器，Bool类型）
        /// </summary>
        [XmlElement("仪器1启用地址")]
        public int Meter1AvailableAddress { set; get; } = 3012;

        /// <summary>
        /// 耐压仪2启用状态地址
        /// </summary>
        [XmlElement("仪器2启用地址")]
        public int Meter2AvailableAddress { set; get; } = 3022;

        /// <summary>
        /// 耐压仪3启用状态地址
        /// </summary>
        [XmlElement("仪器3启用地址")]
        public int Meter3AvailableAddress { set; get; } = 3032;

        /// <summary>
        /// 阻值1读取触发地址（M寄存器，Bool类型）
        /// </summary>
        /// <remarks>PLC置1时，上位机读取阻值1数据</remarks>
        [XmlElement("阻值1触发地址")]
        public int Res1TrigAddress { set; get; } = 3035;

        /// <summary>
        /// 阻值2读取触发地址
        /// </summary>
        [XmlElement("阻值2触发地址")]
        public int Res2TrigAddress { set; get; } = 3036;

        /// <summary>
        /// 阻值3读取触发地址
        /// </summary>
        [XmlElement("阻值3触发地址")]
        public int Res3TrigAddress { set; get; } = 3037;

        /// <summary>
        /// 阻值上限参数地址（用于判定阻值是否超标，默认14μΩ）
        /// </summary>
        [XmlElement("阻值上限地址")]
        public int Res_Max_Address { set; get; } = 2020;

        /// <summary>
        /// 阻值下限参数地址
        /// </summary>
        [XmlElement("阻值下限地址")]
        public int Res_Min_Address { set; get; } = 2022;

        /// <summary>
        /// 设备允许启动状态地址（上位机写入，告知PLC可以开始生产）
        /// </summary>
        [XmlElement("设备是否允许启动")]
        public int DeviceAvailableAddress { set; get; } = 1620;

        /// <summary>
        /// 心跳信号地址（上位机定时翻转，PLC监控通信状态）
        /// </summary>
        [XmlElement("设备心跳地址")]
        public int ShankHandAddress { set; get; } = 1622;

        /// <summary>
        /// AOI NG点检通过信号地址（M寄存器，Bool类型）
        /// </summary>
        /// <remarks>
        /// 用于AOI NG点检场景：当所有AOI工具均为NG时，向此地址写入1表示点检通过
        /// </remarks>
        [XmlElement("AOI_NG点检通过信号地址")]
        public int AOI_NG_InspectionAddress { set; get; } = 3040;

        /// <summary>
        /// 下料位扫码触发地址
        /// </summary>
        [XmlElement("下料扫码触发地址")]
        public int SecondScanTrigAddress { set; get; } = 1100;

        /// <summary>
        /// 下料位扫码结果反馈地址
        /// </summary>
        [XmlElement("下料扫码返回地址")]
        public int SecondScanResultAddress { set; get; } = 1101;

        /// <summary>
        /// 下料位产品SN地址
        /// </summary>
        [XmlElement("下料位SN地址")]
        public int SecondScanSNAddress { set; get; } = 1150;

        /// <summary>
        /// 测试模式信号地址（D寄存器，uint16类型）
        /// </summary>
        /// <remarks>
        /// 上位机向PLC写入测试模式值：
        /// - 0: 只测交流(ACW Only)
        /// - 1: 只测直流(DCW Only)
        /// - 2: 先交后直(ACW then DCW)
        /// - 3: 先直后交(DCW then ACW)
        /// 默认地址D1012
        /// </remarks>
        [XmlElement("测试模式地址")]
        public int TestModeAddress { set; get; } = 1012;

        /// <summary>
        /// 当前测试模式配置
        /// </summary>
        /// <remarks>
        /// 系统启动或配置变更时，将此值写入PLC TestModeAddress地址
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
        /// 当前测试模式的中文显示名称（用于界面绑定）
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
        /// 机器人TCP通信配置（IP、端口）
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

        #region ==================== 耐压测试设备（配置+设备对象，部分序列化） ====================

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
        /// 【调用位置】TV1Process() 中调用
        /// </remarks>
        [XmlElement("耐压1")]
        public AT9620.AT9620 AT9620_1 = new AT9620.AT9620();

        /// <summary>
        /// 耐压仪2（工位2使用）
        /// </summary>
        /// <remarks>配置和功能同AT9620_1，在TV2Process()中调用</remarks>
        [XmlElement("耐压2")]
        public AT9620.AT9620 AT9620_2 = new AT9620.AT9620();

        /// <summary>
        /// 耐压仪3（工位3使用，可选）
        /// </summary>
        /// <remarks>配置和功能同AT9620_1，在TV3Process()中调用</remarks>
        [XmlElement("耐压3")]
        public AT9620.AT9620 AT9620_3 = new AT9620.AT9620();

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
