using BusbarCompressionSystem.Model.FaraVision.Tool;
using BusbarCompressionSystem.Model.Setting;
using GalaSoft.MvvmLight;
using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Xml.Serialization;

namespace BusbarCompressionSystem.FaraVision
{
    public class FaraVisionDataModel : ObservableObject
    {
        #region 数据模型
        [XmlElement("过程参数模型")]
        public Processmodel Processmodel { get; set; } = new Processmodel();

        [XmlElement("日志模型")]
        public RecordModel Recordmodel { get; set; } = new RecordModel();

        [XmlElement("配置模型")]
        public VisionSettingModel Settingmodel { get; set; } = new VisionSettingModel();

        #endregion
    }
    public class RecordModel : ObservableObject
    {
        [XmlElement("通讯过程数据")]
        public ObservableCollection<ConnectRecord> ComuncationLog { get; set; } = new ObservableCollection<ConnectRecord>();

        [XmlElement("日志")]
        public ObservableCollection<string> workLog { set; get; } = new ObservableCollection<string>();

        [XmlElement("错误")]
        public ObservableCollection<string> ErrorLog { set; get; } = new ObservableCollection<string>();
    }

    public class Kposition : ObservableObject
    {
        /// <summary>
        /// X坐标
        /// </summary>
        public int X { set; get; } = 0;
        /// <summary>
        /// Y坐标
        /// </summary>
        public int Y { set; get; } = 0;
    }

    public class KColor : ObservableObject
    {
        public byte R { set; get; } = 0;
        public byte G { set; get; } = 0;
        public byte B { set; get; } = 0;

    }

    public class Processmodel : ObservableObject
    {


        /// <summary>
        /// 光标位置
        /// </summary>
        [XmlIgnore]
        public Kposition position { set; get; } = new Kposition() { X = 0, Y = 0 };
        [XmlIgnore]
        public KColor KColor { set; get; } = new KColor();




        public ObservableCollection<string> CameraIDList { get; set; } = new ObservableCollection<string>();

        public ObservableCollection<Camera.DATA> CameraList { set; get; }=new ObservableCollection<Camera.DATA>();


        [XmlIgnore]
        [XmlElement("二维码累积内容")]
        public string Barcodes { set; get; } = string.Empty;

        //[XmlElement("容器编号")]
        //public string RQSN { set; get; }=string.Empty;


        [XmlElement]
        [XmlIgnore]
        public string BarcodeStr { set; get; }

        [XmlElement]
        [XmlIgnore]
        public string RCMD { get; set; } = string.Empty;

        /// <summary>
        /// 当前 AOI 帧内、同一触发指令下的模板定位矫正运行态。
        /// </summary>
        [XmlIgnore]
        public LocatorCorrectionRuntimeState LocatorCorrection { get; set; } = new LocatorCorrectionRuntimeState();


        [XmlIgnore]
        public int ToolIndex { set; get; } = -1;

        #region 动态密码参数审计（P1快照模式：运行时，不序列化）

        /// <summary>
        /// 进入编辑界面时的工具索引（用于保存工程时做参数差异对比）
        /// </summary>
        [XmlIgnore]
        public int EditingToolIndex { get; set; } = -1;

        /// <summary>
        /// 进入编辑界面时的工具快照（旧值来源）
        /// </summary>
        [XmlIgnore]
        public ToolModel EditingToolSnapshot { get; set; } = null;

        /// <summary>
        /// 快照时间
        /// </summary>
        [XmlIgnore]
        public DateTime EditingToolSnapshotTime { get; set; } = DateTime.MinValue;

        #endregion

        [XmlElement("工具模型")]
        public ObservableCollection<ToolModel> Tools { get; set; } = new ObservableCollection<ToolModel>();

        [XmlElement("当前工具")]
        public ToolModel tool { set; get; } = new ToolModel();

        [XmlElement("选择工具序号")]
        public int selectedindex { set; get; } = -1;

        //public HWindowControlWPF HWindow = null;
        [XmlIgnore]
        public HWindow HWindow = null;

        [XmlIgnore]
        public int ImageNum { set; get; } = 0;


        [XmlIgnore]
        [XmlElement("胶路高度检测触发")]
        public IO Trig_IO { get; set; } = new IO();




        [XmlElement("状态颜色")]
        ///透明,等待
        ///黄色，运行中
        ///绿色，OK
        ///红色, NG
        [XmlIgnore]
        public SolidColorBrush StatusColor
        {

            get
            {
                switch (_Status)
                {
                    case ToolStatus.识别中:
                        {
                            return Brushes.Orange;
                        }
                    case ToolStatus.OK:
                        {
                            return Brushes.GreenYellow;
                        }
                    case ToolStatus.定位未生效:
                        {
                            return Brushes.Gold;
                        }
                    case ToolStatus.NG:
                    case ToolStatus.NG2:
                        {
                            return Brushes.OrangeRed;
                        }
                    default:
                        {
                            // return new SolidColorBrush(Color.FromArgb(100, 255, 255, 255));
                            return Brushes.Gray;
                        }
                }
            }

        }


        private ToolStatus _Status = ToolStatus.等待中;
        [XmlElement("测试状态")]


        public ToolStatus Status
        {
            set
            {
                _Status = value;
                RaisePropertyChanged(() => Status);
                RaisePropertyChanged(() => StatusColor);

            }
            get { return _Status; }
        }

        [XmlElement("扫码枪接收信息")]
        public string Scannerstr { set; get; } = string.Empty;

        [XmlElement("SN列表")]
        public ObservableCollection<string> SNList { set; get; } = new ObservableCollection<string>();

        [XmlIgnore]
        [XmlElement("显示照片")]
        public BitmapSource ShowBitmapSource { set; get; }

    }


    public class VisionSettingModel : ObservableObject
    {
        [XmlIgnore]
        public HWindow HWindow { get; set; } = null;

        [XmlIgnore]
        [XmlElement("相机配置")]
        public Camera.DATA camedata { set; get; } = new Camera.DATA();




        [XmlElement("照片保存设置")]
        public ImageSaveSetting ImageSaveSetting { get; set; } = new ImageSaveSetting();

        [XmlElement("扫码器")]
        public Scanner.ScannerModel ScannerModel { set; get; } = new Scanner.ScannerModel();

        [XmlElement("霍尼韦尔扫码器")]

        public Honeywell.HF800 HF800 { set; get; } = new Honeywell.HF800();


        public string ScannerMode { set; get; } = "HF800";

        #region 工程配置
        [XmlElement("工程路径")]
        public string Prjdir { set; get; } = $"{Environment.CurrentDirectory}\\工程文件";
        [XmlIgnore]
        [XmlElement("工程名称清单")]
        public ObservableCollection<string> Prjs { set; get; } = new ObservableCollection<string>();

        [XmlElement("选中工程序号")]
        public int prjselected { set; get; } = -1;

        [XmlElement("工程名称")]
        public string Name { set; get; } = "999";
        private bool _permission;

        /// <summary>
        /// 当前动态密码授权是否有效。
        /// 该运行态控制 AOI 工程编辑和调机模式选择；程序启动、手动锁定及授权到期均回到关闭状态。
        /// </summary>
        [XmlIgnore]
        public bool permission
        {
            get { return _permission; }
            set
            {
                if (_permission == value)
                {
                    return;
                }

                _permission = value;
                RaisePropertyChanged(() => permission);
            }
        }

        private bool _isRetestAdjustmentMode;

        /// <summary>
        /// AOI 与电测复测次数的运行模式。
        /// true 表示动态密码授权期间的调机模式，入口保留日志并享有无限次数；false 表示量产模式，
        /// AOI 与电测按工单、SN 分别累计入口次数。该状态只服务当前运行会话，不写入工程 XML。
        /// </summary>
        [XmlIgnore]
        public bool IsRetestAdjustmentMode
        {
            get { return _isRetestAdjustmentMode; }
            set
            {
                if (_isRetestAdjustmentMode == value)
                {
                    return;
                }

                _isRetestAdjustmentMode = value;
                RaisePropertyChanged(() => IsRetestAdjustmentMode);
            }
        }

        #region 动态密码授权上下文（运行时，不序列化）

        /// <summary>
        /// 动态密码授权时间（用于“保存工程”时上报参数差异审计）
        /// </summary>
        [XmlIgnore]
        public DateTime PermissionGrantedAt { get; set; } = DateTime.MinValue;

        /// <summary>
        /// 授权人姓名（动态密码验证返回）
        /// </summary>
        [XmlIgnore]
        public string PermissionAuthorizerName { get; set; } = string.Empty;

        /// <summary>
        /// 授权人工号（动态密码验证返回）
        /// </summary>
        [XmlIgnore]
        public string PermissionAuthorizerNo { get; set; } = string.Empty;

        /// <summary>
        /// 申请时通知的接收人列表（用于审计追溯，格式：姓名(工号)；姓名(工号)）
        /// </summary>
        [XmlIgnore]
        public string PermissionRequestedReceivers { get; set; } = string.Empty;

        /// <summary>
        /// 权限等级（动态密码验证返回）
        /// </summary>
        [XmlIgnore]
        public int PermissionPrivilegeLevel { get; set; } = 0;

        /// <summary>
        /// 有效期分钟（来自配置）
        /// </summary>
        [XmlIgnore]
        public int PermissionPeriodMinutes { get; set; } = 0;

        /// <summary>
        /// 申请原因（用于审计追溯）
        /// </summary>
        [XmlIgnore]
        public string PermissionReason { get; set; } = string.Empty;

        /// <summary>
        /// 当前动态密码授权会话的审计关联编号。运行时字段不写入工程 XML，权限关闭后清空。
        /// </summary>
        [XmlIgnore]
        public string PermissionAuditId { get; set; } = string.Empty;

        #endregion
        #endregion

        #region 通讯配置

        [XmlElement("机器人")]
        public TcpSetting RobotConnect { set; get; } = new TcpSetting();
        [XmlElement("上位机")]
        public TcpSetting SoftConnect { set; get; } = new TcpSetting();

        [XmlElement("扫码报工服务器")]
        public TcpSetting BarcodeReporter { set; get; } = new TcpSetting();



        #region PLC配置
        [XmlElement("PLC通讯IP")]
        public string PLC_IP { set; get; } = "10.23.8.20";
        [XmlElement("PLC通讯端口")]
        public int PLC_Port { set; get; } = 502;
        [XmlElement("信号交互地址")]
        public int AddressStart { set; get; } = 7080;


        #endregion





        [XmlIgnore]
        public TcpClientHelper.TcpClientH TcpClientH { set; get; } = new TcpClientHelper.TcpClientH();

        [XmlIgnore]
        public TcpServerHelper.TCPServerH TcpServerRobot { set; get; } = new TcpServerHelper.TCPServerH();
        [XmlIgnore]
        public TcpServerHelper.TCPServerH TcpServerSoft { set; get; } = new TcpServerHelper.TCPServerH();

        #endregion


        [XmlElement("测试延时")]
        public int ACommandTriggerWaitMs { set; get; } = 1000;


        [XmlElement("缩放照片尺寸")]
        public int ImageSize { set; get; } = 140;


        [XmlElement("自动记录识别过程日志")]
        public bool SaveProcessData { set; get; } = true;

        [XmlElement("扫码检索服务器地址")]
        public string SNlistServerIP { set; get; } = "10.23.8.28";



    }

    public enum IOstatus
    {
        连接失败,
        高电平,
        低电平
    }

    public class IO : ObservableObject
    {

        private int _IOstatus = -1;
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
        [XmlIgnore]
        public SolidColorBrush IOstatusBackGround
        {
            get
            {
                if (_IOstatus == -1)
                {
                    return Brushes.WhiteSmoke;
                }

                else if (IOstatus == 1)
                {
                    return Brushes.GreenYellow;
                }
                else
                {
                    return Brushes.Black;
                }
            }
        }
        public SolidColorBrush IOStatusForeGround
        {
            get
            {
                if (_IOstatus == -1)
                {
                    return Brushes.Black;
                }
                else if (_IOstatus == 1)
                {
                    return Brushes.OrangeRed;
                }
                else
                {
                    return Brushes.White;
                }
            }
        }
    }

}
