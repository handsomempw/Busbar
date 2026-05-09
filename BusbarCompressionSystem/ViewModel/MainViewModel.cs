/*
 * 主要业务逻辑控制器
 *
 * MVVM架构中的ViewModel层核心，负责所有业务逻辑：
 * 1. 硬件设备控制：相机、PLC、测试仪器、机器人协调工作
 * 2. 生产流程管理：产品检测、测试、数据记录的完整流程
 * 3. 数据处理：XML序列化保存/加载配置，数据库交互
 * 4. 算法集成：Halcon视觉算法、位置检测算法等
 * 5. 多线程管理：并发处理多工位测试和检测任务
 *
 * 核心业务流程：
 * 产品进站 → 视觉检测 → 耐压测试 → 数据记录 → MES报工
 */

using BusbarCompressionSystem.Model;
using GalaSoft.MvvmLight;
using System.IO;
using System.Windows;
using System.Xml.Serialization;
using System;
using System.Globalization;
using AT9620;
using HslCommunication.ModBus;
using System.Data;
using System.Threading;
using System.Text;
using HalconDotNet;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Camera;
using TcpServerHelper;
using Panuon.WPF.UI;
using BusbarCompressionSystem.Model.Record;
using SQLITEDATABASE;
using BusbarCompressionSystem.Model.FaraVision.Tool.QRCode;
using BusbarCompressionSystem.Model.FaraVision.Tool;
using BusbarCompressionSystem.Model.FaraVision;
using PositionDetect;
using System.Drawing;
using System.Linq;
using Honeywell;
using System.Runtime.InteropServices; // 用于GDI句柄管理
using BusbarCompressionSystem.Utils;

namespace BusbarCompressionSystem.ViewModel
{
    /// <summary>
    /// This class contains properties that the main View can data bind to.
    /// <para>
    /// Use the <strong>mvvminpc</strong> snippet to add bindable properties to this ViewModel.
    /// </para>
    /// <para>
    /// You can also use Blend to data bind with the tool's support.
    /// </para>
    /// <para>
    /// See http://www.galasoft.ch/mvvm
    /// </para>
    /// </summary>
    public partial class MainViewModel : ViewModelBase
    {
        #region 静态锁对象
        /// <summary>
        /// 尺寸测量日志写入锁
        /// </summary>
        private static readonly object _measurementLogLock = new object();
        #endregion

        /// <summary>
        /// Initializes a new instance of the MainViewModel class.
        /// </summary>
        public MainViewModel()
        {
            // 注入SQLite模块的UI日志回调：当底层检测到关键文件缺失等情况时，也能通过 writeLog 在界面提示
            sqlite.UiLog = msg => writeLog(msg);

            ////if (IsInDesignMode)
            ////{
            ////    // Code runs in Blend --> create design time data.
            ////}
            ////else
            ////{
            ////    // Code runs "for real"
            ////}
            ///
            Initrelaycommand();
        }

        public DataModel DataModel { get; set; } = new DataModel();

        #region 操作
        /// <summary>
        /// 处理扫码数据，完成产品SN的解码、验证和数据库记录创建
        /// </summary>
        /// <param name="snstr">扫码原始数据字符串</param>
        /// <returns>
        /// 返回错误信息字符串，如果处理成功则返回空字符串
        /// 可能的错误信息包括：
        /// - 标签解码失败
        /// - 混批错误（不同规格产品）
        /// - 工单号查询失败
        /// - 物料编码查询失败
        /// </returns>
        /// <remarks>
        /// 处理流程：
        /// 1. 解码扫码数据获取产品SN
        /// 2. 根据SN查询MES系统获取工单号和物料编码
        /// 3. 验证物料编码是否与当前产线规格一致（防止混批）
        /// 4. 验证数据完整性（工单号和物料编码不能为空）
        /// 5. 将SN和工单号写入PLC
        /// 6. 在本地数据库创建生产记录
        /// 业务逻辑：
        /// 1. 点检SN码（INSPECTION_TV_OK/NG, INSPECTION_AOI_OK/NG）：跳过MES校验，直接创建本地记录
        /// 2. 普通SN码：调用MES解析获取产品信息，校验规格一致性后创建本地记录
        /// </remarks>
        public string _ScanSN(string snstr)
        {
            // 点检SN码特殊处理：跳过MES校验，直接返回成功
            // 扩展为四个点检 SN：耐压点检 OK/NG + AOI 点检 OK/NG
            if (snstr == DataModel.Settingmodel.SETTING_DATA.InspectionTVOKSN ||
                snstr == DataModel.Settingmodel.SETTING_DATA.InspectionTVNGSN ||
                snstr == DataModel.Settingmodel.SETTING_DATA.InspectionAOIOKSN ||
                snstr == DataModel.Settingmodel.SETTING_DATA.InspectionAOINGSN)
            {
                writeLog($"点检扫码原始数据: {snstr}");
                string wocode = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.get_WO_CODE(snstr);
                string partnoid = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.get_PartNO_ID(snstr);

                bool plcWriteOk = PLC_Writestring(DataModel.Settingmodel.AddressSN.ToString(), $"{snstr};{wocode}");
                writeLog($"写入PLC地址 {DataModel.Settingmodel.AddressSN}: {(plcWriteOk ? "成功" : "失败")} | {snstr};{wocode}");
                if (!plcWriteOk)
                {
                    // 【日志归置】PLC 写入异常属于设备/通讯类错误，按约定归到“日志\\错误”，避免污染“数据库异常”
                    writePlcError($"[PLC写入异常]ScanSN-写入AddressSN失败 | AddressSN={DataModel.Settingmodel.AddressSN}, 内容长度={(($"{snstr};{wocode}")?.Length ?? 0)}, SN={snstr}, WO={wocode}");
                }

                sqlite.CREATENEWLINE(wocode, partnoid, snstr, DataModel.Settingmodel.SETTING_DATA.StationCode, DataModel.Settingmodel.SETTING_DATA.MachineID, DateTime.Now);
                writeLog($"创建数据库记录: wocode={wocode}, partnoid={partnoid}, SN={snstr}, 工位={DataModel.Settingmodel.SETTING_DATA.StationCode}, 设备={DataModel.Settingmodel.SETTING_DATA.MachineID}");

                writeLog($"✓ 点检扫码处理成功！");
                return string.Empty;
            }

            //if (string.IsNullOrEmpty(DataModel.Processmodel.TakePhotoTestModel.Productinfo.SN))
            //{
            
            // 记录扫码原始数据
            writeLog($"扫码原始数据: {snstr}");
            
            // 解码SN
            string sn = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.DecodeSN(snstr);
            writeLog($"解码后的SN: {(string.IsNullOrEmpty(sn) ? "解码失败" : sn)}");
            
            if (!string.IsNullOrEmpty(sn))
            {
                string wocode = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.get_WO_CODE(sn);
                writeLog($"查询WOCODE: {(string.IsNullOrEmpty(wocode) ? "查询失败" : wocode)}");
                
                string partnoid = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.get_PartNO_ID(sn);
                writeLog($"查询PartNOID: {(string.IsNullOrEmpty(partnoid) ? "查询失败" : partnoid)}");
               
                if (partnoid != DataModel.Processmodel.PartNOID)
                {
                    // 【日志】混批报错详细信息
                    writeLog($"❌ 检测到混批! 不允许不同规格产品混合作业", true);
                    writeLog($"  - 产线当前规格: {DataModel.Processmodel.PartNOID}", true);
                    writeLog($"  - 扫码产品规格: {partnoid}", true);
                    writeLog($"  - 产品SN: {sn}", true);
                    writeLog($"  - 工单号: {wocode}", true);
                    writeLog($"========== 扫码处理失败（混批错误）==========", true);
                    return $"混批错误！禁止不同规格产品混合作业\n当前规格: {DataModel.Processmodel.PartNOID}\n扫码规格: {partnoid}";
                }

                // 数据完整性检查
                if (string.IsNullOrEmpty(wocode))
                {
                    return "关联批次号读取失败";
                }
                if (string.IsNullOrEmpty(partnoid))
                {
                    return "关联规格信息读取失败";
                }
                
                // 写入PLC和数据库
                bool plcWriteOk = PLC_Writestring(DataModel.Settingmodel.AddressSN.ToString(), $"{sn};{wocode}");
                writeLog($"写入PLC地址 {DataModel.Settingmodel.AddressSN}: {(plcWriteOk ? "成功" : "失败")} | {sn};{wocode}");
                if (!plcWriteOk)
                {
                    // 【日志归置】PLC 写入异常属于设备/通讯类错误，按约定归到“日志\\错误”，避免污染“数据库异常”
                    writePlcError($"[PLC写入异常]ScanSN-写入AddressSN失败 | AddressSN={DataModel.Settingmodel.AddressSN}, 内容长度={(($"{sn};{wocode}")?.Length ?? 0)}, SN={sn}, WO={wocode}");
                }
                
                writeLog($"创建数据库记录: wocode={wocode}, partnoid={partnoid}, SN={sn}, 工位={DataModel.Settingmodel.SETTING_DATA.StationCode}, 设备={DataModel.Settingmodel.SETTING_DATA.MachineID}");
                bool dbResult = sqlite.CREATENEWLINE(wocode, partnoid, sn, DataModel.Settingmodel.SETTING_DATA.StationCode, DataModel.Settingmodel.SETTING_DATA.MachineID, DateTime.Now);
                
                if (!dbResult)
                {
                    writeLog($"⚠ 数据库记录创建失败！wocode={wocode}, SN={sn}", true);
                }
                
                writeLog($"✓ 扫码处理成功！");
                return string.Empty;

            }
            else
            {
                return "标签读取失败,请确认该产品编号是否正常";
            }
            //}
            //else
            //{
            //    return "拍照位已经有产品编号，请勿重复扫码";
            //}
        }

        /// <summary>
        /// 扫描产品编号并进行MES校验
        /// </summary>
        /// <remarks>
        /// 触发方式：手动扫码（Enter键/按钮）或 自动扫码（PLC触发）
        /// 点检SN码会跳过MES校验，普通SN进行完整校验
        /// </remarks>
        /// <returns>true: 扫码成功; false: 扫码失败</returns>
        public bool ScanSN()
        {
            string s = _ScanSN(DataModel.Processmodel.sninputstr);
            if (!string.IsNullOrEmpty(s))
            {
                writeLog(s, true);
                return false;
            }
            else
            {
                DataModel.Processmodel.sninputstr = string.Empty;
                return true;
            }
            DataModel.Processmodel.TakePhotoTestModel.error = s;

        }
        #endregion
        #region 耐压测试

        public void InitAt9620()
        {
            DataModel.Settingmodel.AT9620_1.DataReceived += OnReceive1;
            DataModel.Settingmodel.AT9620_2.DataReceived += OnReceive2;
            DataModel.Settingmodel.AT9620_3.DataReceived += OnReceive3;
        }

        /// <summary>
        /// 初始化 AT6835FL 绝缘电阻测试仪：订阅串口日志事件
        /// </summary>
        public void InitAT6835FL()
        {
            DataModel.Settingmodel.AT6835FL_1.LogMessage += (s, e) =>
            {
                writeLog($"[IR测试] {e.Message}");
            };

            // 将 SETTING_DATA 中的 IR 串口调试开关同步到驱动运行时属性（避免“界面已勾选但驱动仍为非详细模式”）
            SyncIrAt6835RuntimeFlagsFromSettings("[启动]");
        }

        /// <summary>
        /// 同步 IR(AT6835FL) 驱动的运行时调试/通讯策略开关（来源：<see cref="SettingModel.SETTING_DATA"/>）。
        /// 说明：这些字段使用 XmlIgnore，不会随仪器串口 XML 一起持久化，因此每次通讯前同步最可靠。
        /// </summary>
        /// <param name="reason">简短原因标签，便于在主日志中定位调用点</param>
        public void SyncIrAt6835RuntimeFlagsFromSettings(string reason)
        {
            try
            {
                var sd = DataModel.Settingmodel?.SETTING_DATA;
                var ir = DataModel.Settingmodel?.AT6835FL_1;
                if (sd == null || ir == null)
                {
                    return;
                }

                // 仅保留一个 XML 开关：是否生成详细调试日志
                ir.VerboseSerialDebug = sd.IrDownloadDebugLog;

                // 固化生产策略：写前清空输入缓冲 + 下发时自检
                ir.DiscardInBufferBeforeWrite = true;
                ir.DownloadSelfCheck = true;

                string day = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                string dbgDir = Path.Combine(Environment.CurrentDirectory, "日志", "IR串口调试", day);
                writeLog($"{reason}[IR驱动] 运行时开关: Verbose={ir.VerboseSerialDebug}, DiscardIn={ir.DiscardInBufferBeforeWrite}, DownloadSelfCheck={ir.DownloadSelfCheck}");
                if (ir.VerboseSerialDebug)
                {
                    writeLog($"{reason}[IR驱动] 详细串口日志目录: {dbgDir}（文件名 IR_Serial_HHmmss_fff.txt，每次打开串口会新建会话）");
                }
            }
            catch
            {
                // 同步失败不影响主流程
            }
        }

        // 电测原始数据：按日期分子目录（yyyyMMdd），同目录下按 SN 分文件；诊断日志为 {SN}_diag.txt（不含工单料号），通信日志为 {SN}_comm.txt
        private readonly object _electricalRawLogLock = new object();
        private static readonly string ElectricalRawLogDir = Path.Combine(Environment.CurrentDirectory, "识别过程日志", "电测原始数据");
        private const int ElectricalLogRetainDays = 3;

        private static string GetElectricalLogDayDirectory()
        {
            return Path.Combine(ElectricalRawLogDir, DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// 确保当天目录存在，并删除早于保留天数的日期子目录（仅匹配八位 yyyyMMdd 文件夹名）。
        /// </summary>
        private static void EnsureElectricalLogInfrastructure()
        {
            Directory.CreateDirectory(ElectricalRawLogDir);
            string todayDir = GetElectricalLogDayDirectory();
            Directory.CreateDirectory(todayDir);

            try
            {
                foreach (var dir in Directory.GetDirectories(ElectricalRawLogDir))
                {
                    var name = Path.GetFileName(dir);
                    if (name != null && name.Length == 8 &&
                            DateTime.TryParseExact(name, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    {
                        // 删除「距今已满 retain 天」的日期目录（含边界：满 7 天即删）
                        if ((DateTime.Today - d).Days >= ElectricalLogRetainDays)
                        {
                            try { Directory.Delete(dir, true); } catch { }
                        }
                    }
                }
            }
            catch
            {
                // 清理失败不阻塞写日志
            }
        }

        private void WriteElectricalRawDataLog(int tvIndex, Productinfo productInfo, double voltage, double current, double time, string status)
        {
            try
            {
                if (productInfo == null || string.IsNullOrWhiteSpace(productInfo.SN))
                {
                    return;
                }

                lock (_electricalRawLogLock)
                {
                    EnsureElectricalLogInfrastructure();
                    string dayDir = GetElectricalLogDayDirectory();
                    string filePath = Path.Combine(dayDir, $"{productInfo.SN}.txt");
                    bool isNewFile = !File.Exists(filePath);

                    using (var sw = new StreamWriter(filePath, true, Encoding.UTF8))
                    {
                        if (isNewFile)
                        {
                            // 字段说明：时间戳、耐压机台、SN、工单、料号、实时电压/电流/时间、仪器状态（原始）
                            sw.WriteLine("时间\t机台\tSN\tWOCODE\tPartNOID\t电压\t电流\t时间\t状态");
                        }

                        sw.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\tTV{tvIndex}\t{productInfo.SN}\t{productInfo.WOCODE}\t{productInfo.PartNOID}\t{voltage}\t{current}\t{time}\t{status}");
                    }
                }
            }
            catch
            {
                // 原始数据日志不允许影响主流程，异常直接吞掉
            }
        }

        /// <summary>
        /// 电测诊断日志（一行一条，UTF-8 追加）。与原始数据同日期子目录，文件名为 {SN}_diag.txt。
        /// </summary>
        private void WriteElectricalDiagLog(string sn, string line)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sn) || string.IsNullOrEmpty(line))
                {
                    return;
                }

                lock (_electricalRawLogLock)
                {
                    EnsureElectricalLogInfrastructure();
                    string dayDir = GetElectricalLogDayDirectory();
                    string filePath = Path.Combine(dayDir, $"{sn.Trim()}_diag.txt");
                    using (var sw = new StreamWriter(filePath, true, Encoding.UTF8))
                    {
                        sw.WriteLine(line);
                    }
                }
            }
            catch
            {
                // 诊断日志不影响主流程
            }
        }

        /// <summary>
        /// 电测通信日志（一行一条，UTF-8 追加）。与原始数据同日期子目录，文件名为 {SN}_comm.txt。
        /// </summary>
        private void WriteElectricalCommLog(string sn, string line)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sn) || string.IsNullOrEmpty(line))
                {
                    return;
                }

                lock (_electricalRawLogLock)
                {
                    EnsureElectricalLogInfrastructure();
                    string dayDir = GetElectricalLogDayDirectory();
                    string filePath = Path.Combine(dayDir, $"{sn.Trim()}_comm.txt");
                    using (var sw = new StreamWriter(filePath, true, Encoding.UTF8))
                    {
                        sw.WriteLine(line);
                    }
                }
            }
            catch
            {
                // 通信日志不影响主流程
            }
        }

        /// <summary>
        /// 包一层耐压仪 Start：写入会话起止（不含工单/料号），并注入 AT9620 诊断回调。
        /// </summary>
        private global::AT9620.Result RunTvMeterStartWithDiagnostics(AT9620.AT9620 meter, string sn, string testModeLabel)
        {
            string normSn = (sn ?? string.Empty).Trim();
            if (meter == null)
            {
                return new global::AT9620.Result { Error = "仪器实例为空" };
            }

            if (string.IsNullOrWhiteSpace(normSn))
            {
                return meter.Start();
            }

            string sessionId = Guid.NewGuid().ToString("N");
            var tv = meter.TVParameter ?? new TVParameter();
            float total = tv.TestTime + tv.RiseTime + tv.FallTime;
            WriteElectricalDiagLog(normSn,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\t【会话开始】会话ID={sessionId}\tSN={normSn}\t模式={testModeLabel}\t上升(s)={tv.RiseTime.ToString(CultureInfo.InvariantCulture)}\t保持(s)={tv.TestTime.ToString(CultureInfo.InvariantCulture)}\t下降(s)={tv.FallTime.ToString(CultureInfo.InvariantCulture)}\t理论总时长(s)={total.ToString(CultureInfo.InvariantCulture)}\t超时阈值(s)={(total + 2f).ToString(CultureInfo.InvariantCulture)}\t仪器IP={meter.IP}\t端口={meter.Port}");

            meter.DiagnosticLog = line => WriteElectricalDiagLog(normSn, line);
            meter.CommunicationLog = line => WriteElectricalCommLog(normSn, line);
            try
            {
                var r = meter.Start();
                string errDisplay = r.Error;
                if (string.IsNullOrEmpty(errDisplay) && r.Success)
                {
                    errDisplay = "合格";
                }
                WriteElectricalDiagLog(normSn,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\t【会话结束】会话ID={sessionId}\t成功={(r.Success ? "是" : "否")}\t说明={errDisplay}\t过程串长度={r.Recordstr?.Length ?? 0}");
                return r;
            }
            finally
            {
                meter.DiagnosticLog = null;
                meter.CommunicationLog = null;
            }
        }

        private void OnReceive1(object sender, EventArgs e)
        {

            try
            {
                //writeLog($"相机->视觉:接收照片", false);
                AT9620EventArgs myEventArgs = e as AT9620EventArgs;
                var tv = myEventArgs.ResultTVProcess.Value;
                DataModel.Processmodel.TVTestTestModel1.Voltage = myEventArgs.ResultTVProcess.Value.Voltage;
                DataModel.Processmodel.TVTestTestModel1.Current = myEventArgs.ResultTVProcess.Value.Current;
                DataModel.Processmodel.TVTestTestModel1.Time = myEventArgs.ResultTVProcess.Value.Time;
                DataModel.Processmodel.TVTestTestModel1.Status = myEventArgs.ResultTVProcess.Value.status;

                DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage = Math.Max(DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage, DataModel.Processmodel.TVTestTestModel1.Voltage);
                DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent = Math.Max(DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent, DataModel.Processmodel.TVTestTestModel1.Current);
                DataModel.Processmodel.TVTestTestModel1.TVInfo = myEventArgs.ResultTVProcess.Value.status;

                // 电测阶段原始数据落盘（按 SN、按日期子目录）
                WriteElectricalRawDataLog(1, DataModel.Processmodel.TVTestTestModel1.Productinfo, tv.Voltage, tv.Current, tv.Time, tv.status);
            }
            catch (Exception ex)
            {
            }
        }
        private void OnReceive2(object sender, EventArgs e)
        {

            try
            {
                //writeLog($"相机->视觉:接收照片", false);
                AT9620EventArgs myEventArgs = e as AT9620EventArgs;
                var tv = myEventArgs.ResultTVProcess.Value;
                DataModel.Processmodel.TVTestTestModel2.Voltage = myEventArgs.ResultTVProcess.Value.Voltage;
                DataModel.Processmodel.TVTestTestModel2.Current = myEventArgs.ResultTVProcess.Value.Current;
                DataModel.Processmodel.TVTestTestModel2.Time = myEventArgs.ResultTVProcess.Value.Time;
                DataModel.Processmodel.TVTestTestModel2.Status = myEventArgs.ResultTVProcess.Value.status;

                DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage = Math.Max(DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage, DataModel.Processmodel.TVTestTestModel2.Voltage);
                DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent = Math.Max(DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent, DataModel.Processmodel.TVTestTestModel2.Current);
                DataModel.Processmodel.TVTestTestModel2.TVInfo = myEventArgs.ResultTVProcess.Value.status;

                // 电测阶段原始数据落盘（按 SN、按日期子目录）
                WriteElectricalRawDataLog(2, DataModel.Processmodel.TVTestTestModel2.Productinfo, tv.Voltage, tv.Current, tv.Time, tv.status);
            }
            catch (Exception ex)
            {
            }
        }
        private void OnReceive3(object sender, EventArgs e)
        {

            try
            {
                //writeLog($"相机->视觉:接收照片", false);
                AT9620EventArgs myEventArgs = e as AT9620EventArgs;
                var tv = myEventArgs.ResultTVProcess.Value;
                DataModel.Processmodel.TVTestTestModel3.Voltage = myEventArgs.ResultTVProcess.Value.Voltage;
                DataModel.Processmodel.TVTestTestModel3.Current = myEventArgs.ResultTVProcess.Value.Current;
                DataModel.Processmodel.TVTestTestModel3.Time = myEventArgs.ResultTVProcess.Value.Time;
                DataModel.Processmodel.TVTestTestModel3.Status = myEventArgs.ResultTVProcess.Value.status;

                DataModel.Processmodel.TVTestTestModel3.TVMaxVoltage = Math.Max(DataModel.Processmodel.TVTestTestModel3.TVMaxVoltage, DataModel.Processmodel.TVTestTestModel3.Voltage);
                DataModel.Processmodel.TVTestTestModel3.TVMaxCurrent = Math.Max(DataModel.Processmodel.TVTestTestModel3.TVMaxCurrent, DataModel.Processmodel.TVTestTestModel3.Current);
                DataModel.Processmodel.TVTestTestModel3.TVInfo = myEventArgs.ResultTVProcess.Value.status;

                // 电测阶段原始数据落盘（按 SN、按日期子目录）
                WriteElectricalRawDataLog(3, DataModel.Processmodel.TVTestTestModel3.Productinfo, tv.Voltage, tv.Current, tv.Time, tv.status);

            }
            catch (Exception ex)
            {
            }
        }

        #endregion
        #region PLC通讯

        /// <summary>
        /// 用于跟踪PLC连接状态，避免重复记录日志
        /// true表示上次循环时PLC已连接，false表示上次循环时PLC未连接
        /// </summary>
        public void ScannerProcess()
        {
            try
            {
                if (DataModel.Settingmodel.ScannerMode == "HF800")
                {
                    var r = DataModel.Settingmodel.HF800.Scanner();
                    // 添加空值检查，防止r.value为null时崩溃
                    if (r.Status == Honeywell.Status.OK && !string.IsNullOrEmpty(r.value))
                    {
                        string s = r.value.Replace("\r", "").Replace("\n", "").Trim();
                        DataModel.Processmodel.sninputstr = s;
                        //SQLITEDATABASE.sqlite.CREATENEWLINE("1", "1", s);
                        //PLC_write((DataModel.Settingmodel.AddressStart + 1).ToString(), (UInt16)1);
                        var R = ScanSN();
                        PLC_write((DataModel.Settingmodel.AddressStart + 1).ToString(), (UInt16)(R ? 1 : 2));
                    }
                    else
                    {
                        // 扫码失败，写入PLC失败状态
                        PLC_write((DataModel.Settingmodel.AddressStart + 1).ToString(), (UInt16)2);
                    }
                }
                else
                {
                    var r = DataModel.Settingmodel.ScannerModel.Scanner();
                    string s = r.receivestring?.Replace("\r", "").Replace("\n", "").Trim() ?? "";
                    if (!String.IsNullOrEmpty(s))
                    {
                        //s = "7Y00000000" + ((byte)(new Random().NextDouble() * 10)).ToString();
                        //s = $"7Y0000{DateTime.Now.ToString("MMss")}";
                        DataModel.Processmodel.sninputstr = s;
                        var R = ScanSN();
                        PLC_write((DataModel.Settingmodel.AddressStart + 1).ToString(), (UInt16)(r.IsSuccess && R ? 1 : 2));
                    }
                    else
                    {
                        PLC_write((DataModel.Settingmodel.AddressStart + 1).ToString(), (UInt16)2);
                    }
                }
            }
            catch (Exception ex)
            {
                // 异常处理，防止崩溃，写入PLC失败状态
                try
                {
                    PLC_write((DataModel.Settingmodel.AddressStart + 1).ToString(), (UInt16)2);
                }
                catch { }
            }
        }
        public void SecondScannerProcess()
        {
            if (DataModel.Settingmodel.SecondScannerMode == "HF800")
            {
                var r = DataModel.Settingmodel.SecondHF800.Scanner();
                string s = r.value.Replace("\r", "").Replace("\n", "");
                DataModel.Processmodel.sninputstr = s;
                //SQLITEDATABASE.sqlite.CREATENEWLINE("1", "1", s);
                PLC_write(DataModel.Settingmodel.SecondScanResultAddress.ToString(), (UInt16)(r.Status == Honeywell.Status.OK ? 1 : 2));

                if (r.Status == Honeywell.Status.OK && !string.IsNullOrEmpty(s))
                {
                    // 写入SN到D1150
                    PLC_Writestring(DataModel.Settingmodel.SecondScanSNAddress.ToString(), s);
                }
            }
            else
            {
                var r = DataModel.Settingmodel.SecondScannerModel.Scanner();
                string s = r.receivestring.Replace("\r", "").Replace("\n", "");
                if (!String.IsNullOrEmpty(s))
                {
                    DataModel.Processmodel.sninputstr = s;

                    PLC_write(DataModel.Settingmodel.SecondScanResultAddress.ToString(), (UInt16)(r.IsSuccess ? 1 : 2));

                    if (r.IsSuccess)
                    {
                        // 写入SN到D1150
                        PLC_Writestring(DataModel.Settingmodel.SecondScanSNAddress.ToString(), s);
                    }
                }
                else
                {
                    PLC_write(DataModel.Settingmodel.SecondScanResultAddress.ToString(), (UInt16)(2));
                }
            }
        }
        /// <summary>
        /// 拍照留底工位处理流程：触发三个相机同时拍照并等待完成。
        /// </summary>
        /// <remarks>
        /// 触发条件：PLC拍照触发信号（M地址）由0变1时调用
        /// 
        /// 处理流程：
        /// 1. 从PLC读取产品编码（格式：SN;WOCODE），解析出SN和工单号
        /// 2. 重置三个相机的完成标志位
        /// 3. 同时触发三个相机执行拍照
        /// 4. 轮询等待三个相机全部完成（超时时间约3秒，每200ms检查一次，最多15次）
        /// 5. 根据拍照结果更新数据库记录，并创建内存中的产品过程记录
        /// 6. 向PLC写入完成信号：1=成功，2=超时失败
        /// 
        /// PLC交互地址：
        /// - 读取：D{AddressSN} - 产品编码字符串
        /// - 写入：D{AddressStart+3} - 拍照完成状态（1=OK, 2=NG）
        /// </remarks>
        public void TakePhoto1Process()
        {
            // 从PLC读取产品编码（协议格式：SN;WOCODE）。
            // PLC_Readstring() 本身已包含“通讯重试”，这里增加少量“重读”，用于缓解PLC尚未写入完成导致的瞬时异常（如读到空/0）。
            // 一旦最终仍读码失败：直接回写PLC=2并return，避免沿用上一次残留SN导致UI/照片/数据库被污染。
            const int maxReadRetry = 2;
            string s = string.Empty;
            string lastNonEmpty = string.Empty;
            string sn = string.Empty;
            string wocode = string.Empty;
            bool isValid = false;

            for (int attempt = 1; attempt <= maxReadRetry; attempt++)
            {
                s = (PLC_Readstring(DataModel.Settingmodel.AddressSN) ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    lastNonEmpty = s;
                }

                string[] parts = s.Split(';');
                if (parts.Length == 2)
                {
                    sn = (parts[0] ?? string.Empty).Trim();
                    wocode = (parts[1] ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(sn) && !string.IsNullOrWhiteSpace(wocode))
                    {
                        isValid = true;
                        break;
                    }
                }

                Thread.Sleep(80);
            }

            if (!isValid)
            {
                // 通讯问题通常表现为“读到空”，值问题通常表现为“读到0/不含;”。
                // 实际判断可结合 PLC_Readstring() 输出的 [PLC通讯] 日志进一步确认。
                string failureType = string.IsNullOrWhiteSpace(lastNonEmpty) ? "通讯/未写入" : "值/格式";
                string raw = string.IsNullOrWhiteSpace(lastNonEmpty) ? s : lastNonEmpty;

                writeLog($"[拍照留底] ❌ 产品编码读取错误({failureType})! 原始值=[{raw}], 期望格式=[SN;WOCODE], 重试次数={maxReadRetry}, PLC地址=D{DataModel.Settingmodel.AddressSN}", true);

                // 【日志归置】PLC 数据异常属于设备侧错误，按约定归到“日志\\错误”，避免污染“数据库异常”
                writePlcError($"[PLC数据异常]TakePhoto1-产品编码读取失败 | 失败类型={failureType}, 原始值=[{raw}], 重试次数={maxReadRetry}, PLC地址=D{DataModel.Settingmodel.AddressSN}");

                // 清空当前产品信息，避免后续误用残留SN
                DataModel.Processmodel.TakePhotoTestModel.Productinfo = new Model.Record.Productinfo()
                {
                    SN = string.Empty,
                    WOCODE = string.Empty,
                    PartNOID = DataModel.Processmodel.PartNOID
                };

                // 读码失败：直接回写NG(2)并中止本工位流程（不拍照/不写库/不插UI行）
                PLC_write((DataModel.Settingmodel.AddressStart + 3).ToString(), 2);
                return;
            }

            DataModel.Processmodel.TakePhotoTestModel.Productinfo = new Model.Record.Productinfo()
            {
                SN = sn,
                WOCODE = wocode,
                PartNOID = DataModel.Processmodel.PartNOID
            };
            writeLog($"[拍照留底] 产品编码读取成功: SN={sn}, WOCODE={wocode}");

            // 开始拍照前，先清零三个相机的完成标志位；各相机在收到图片回调时会把 finished=true
            DataModel.Settingmodel.camedata1.CameraModel.finished = false;
            DataModel.Settingmodel.camedata2.CameraModel.finished = false;
            DataModel.Settingmodel.camedata3.CameraModel.finished = false;

            // 同时触发三个相机拍照；后续 while 循环轮询等待三个 finished 全部变为 true
            DataModel.Settingmodel.camedata1.CameraModel.camera.bnTriggerExec_Click();
            DataModel.Settingmodel.camedata2.CameraModel.camera.bnTriggerExec_Click();
            DataModel.Settingmodel.camedata3.CameraModel.camera.bnTriggerExec_Click();

            int i = 0;
            while (true)
            {
                Thread.Sleep(200);
                if ((DataModel.Settingmodel.camedata1.CameraModel.finished
                & DataModel.Settingmodel.camedata2.CameraModel.finished
                & DataModel.Settingmodel.camedata3.CameraModel.finished
                ))
                {
                    bool updateResult = SQLITEDATABASE.sqlite.UpdateTakePhoto1(
                        DataModel.Processmodel.TakePhotoTestModel.Productinfo.WOCODE,
                        DataModel.Processmodel.TakePhotoTestModel.Productinfo.PartNOID,
                         DataModel.Processmodel.TakePhotoTestModel.Productinfo.SN, true);
                    if (!updateResult)
                    {
                        writeLog($"[拍照留底] ⚠ UpdateTakePhoto1更新true失败! SN={DataModel.Processmodel.TakePhotoTestModel.Productinfo.SN}", true);
                    }
                    newline(true);
                    PLC_write((DataModel.Settingmodel.AddressStart + 3).ToString(), 1);
                    break;
                }
                if (i++ > 15)
                {
                    #region 超时未完成拍照

                    if (!DataModel.Settingmodel.camedata1.CameraModel.finished)
                    {
                        writeLog("相机1拍照超时");
                    }

                    if (!DataModel.Settingmodel.camedata2.CameraModel.finished)
                    {
                        writeLog("相机2拍照超时");
                    }

                    if (!DataModel.Settingmodel.camedata3.CameraModel.finished)
                    {
                        writeLog("相机3拍照超时");
                    }
                    bool updateResult = SQLITEDATABASE.sqlite.UpdateTakePhoto1(
                        DataModel.Processmodel.TakePhotoTestModel.Productinfo.WOCODE,
                        DataModel.Processmodel.TakePhotoTestModel.Productinfo.PartNOID,
                         DataModel.Processmodel.TakePhotoTestModel.Productinfo.SN, false);
                    if (!updateResult)
                    {
                        writeLog($"[拍照留底] ⚠ UpdateTakePhoto1更新false(超时或者异常)失败! SN={DataModel.Processmodel.TakePhotoTestModel.Productinfo.SN}", true);
                    }
                    newline(false);

                    PLC_write((DataModel.Settingmodel.AddressStart + 3).ToString(), 2);

                    break;
                    #endregion
                }

            }
        }

        /// <summary>
        /// 创建一条新的产品过程数据记录并加入到 DataModel.Recordmodel.ProductInfoRecords 集合中。
        /// 业务含义：
        /// - 在拍照留底完成后调用，将当前产品的基础信息、拍照结果缓存在内存集合里；
        /// - 后续耐压、压力、AOI 等过程都会通过 SN 在该集合中找到对应记录并补充数据，
        /// </summary>
        /// <param name="takephoto1">拍照留底是否成功</param>
        private void newline(bool takephoto1)
        {
            try
            {
                ProductInfoRecord p = new ProductInfoRecord()
                {
                    StationCode = DataModel.Settingmodel.SETTING_DATA.StationCode,
                    EQUIPMENTID = DataModel.Settingmodel.SETTING_DATA.MachineID,
                    Productinfo = DataModel.Processmodel.TakePhotoTestModel.Productinfo,
                    TakePhoto1 = takephoto1
                };
                App.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    DataModel.Recordmodel.ProductInfoRecords.Insert(0, p);
                }));
            }
            catch (Exception ex) { }
        }




        /// <summary>
        /// 执行ACW交流耐压测试（TV1工位）
        /// </summary>
        /// <remarks>
        /// 当TV1Trig=1时调用此方法。
        /// 触发前统一下发ACW参数到AT9620设备。
        /// </remarks>
        public void TV1Process_ACW()
        {
            writeLog($"[耐压1-ACW] 开始ACW交流耐压测试");
            
            // 触发前统一下发参数，避免设备参数未同步
            writeLog($"[耐压1-ACW] 开始下发ACW参数");
            DataModel.Settingmodel.AT9620_1.TVParameter = DataModel.Processmodel.ACWParameter;
            var downloadResult = DataModel.Settingmodel.AT9620_1.Download();
            if (!downloadResult.Success)
            {
                writeLog($"[耐压1-ACW] ❌ ACW参数下发失败: {downloadResult.Error}", true);
                // 参数下发失败视为该工位耐压 NG：通知 PLC 不要启用 IR
                WriteTvOkSignalForIrEnable(1, false);
                PLC_write((DataModel.Settingmodel.AddressStart + 7).ToString(), (UInt16)2);
                return;
            }
            DataModel.Processmodel.LastTV1TestMode = AT9620.TestMode.ACW;
            writeLog($"[耐压1-ACW] ACW参数下发成功");
            Thread.Sleep(500);
            
            // 执行测试（复用现有逻辑）
            TV1Process_Core("ACW");
        }

        /// <summary>
        /// 执行DCW直流耐压测试（TV1工位）
        /// </summary>
        /// <remarks>
        /// 当TV1Trig=2时调用此方法。
        /// 触发前统一下发DCW参数到AT9620设备。
        /// </remarks>
        public void TV1Process_DCW()
        {
            writeLog($"[耐压1-DCW] 开始DCW直流耐压测试");
            
            // 触发前统一下发参数，避免设备参数未同步
            writeLog($"[耐压1-DCW] 开始下发DCW参数");
            DataModel.Settingmodel.AT9620_1.TVParameter = DataModel.Processmodel.DCWParameter;
            var downloadResult = DataModel.Settingmodel.AT9620_1.Download();
            if (!downloadResult.Success)
            {
                writeLog($"[耐压1-DCW] ❌ DCW参数下发失败: {downloadResult.Error}", true);
                // 参数下发失败视为该工位耐压 NG：通知 PLC 不要启用 IR
                WriteTvOkSignalForIrEnable(1, false);
                PLC_write((DataModel.Settingmodel.AddressStart + 7).ToString(), (UInt16)2);
                return;
            }
            DataModel.Processmodel.LastTV1TestMode = AT9620.TestMode.DCW;
            writeLog($"[耐压1-DCW] DCW参数下发成功");
            Thread.Sleep(500);

            // 执行测试（复用现有逻辑）
            TV1Process_Core("DCW");
        }

        /// <summary>
        /// TV1耐压测试核心逻辑（ACW/DCW共用）。
        /// 业务说明：只有读到本次完整的 SN;WOCODE 后才允许继续测试和落记录；
        /// 若读码失败，则视为该工位本次流程无法绑定产品，回写耐压NG并中止，避免沿用旧SN。
        /// </summary>
        /// <param name="testType">测试类型标识，用于日志区分（"ACW"或"DCW"）</param>
        private void TV1Process_Core(string testType)
        {
            string snCode;
            string woCode;
            string rawCode;
            if (TryReadProductCodeFromPlc(DataModel.Settingmodel.AddressSN + 25, $"耐压1-{testType}", out snCode, out woCode, out rawCode))
            {
                DataModel.Processmodel.TVTestTestModel1.Productinfo = new Productinfo() { SN = snCode, WOCODE = woCode, PartNOID = DataModel.Processmodel.PartNOID };
                writeLog($"[耐压1-{testType}] 产品编码读取成功: SN={snCode}, WOCODE={woCode}");
            }
            else
            {
                // 读不到本次产品编码时不能继续电测，否则有串SN风险；回写本站NG让PLC走异常/重试分支。
                WriteTvOkSignalForIrEnable(1, false);
                PLC_write((DataModel.Settingmodel.AddressStart + 7).ToString(), (UInt16)2);
                return;
            }

            float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes);
            DataModel.Processmodel.TVTestTestModel1.Res = res;

            DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage = 0;
            DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent = 0;

            // 清空上一次测试残留，避免本次启动失败时沿用旧信息导致“结果/说明”不一致
            DataModel.Processmodel.TVTestTestModel1.Status = string.Empty;
            DataModel.Processmodel.TVTestTestModel1.TVInfo = string.Empty;
            DataModel.Processmodel.TVTestTestModel1.Voltage = 0;
            DataModel.Processmodel.TVTestTestModel1.Current = 0;
            DataModel.Processmodel.TVTestTestModel1.Time = 0;

            var r = RunTvMeterStartWithDiagnostics(DataModel.Settingmodel.AT9620_1, DataModel.Processmodel.TVTestTestModel1.Productinfo?.SN, testType);
            // 记录耐压失败原因到界面日志，便于首件异常定位
            if (!r.Success && !string.IsNullOrWhiteSpace(r.Error))
            {
                // 将失败原因写入状态/信息，避免首件启动失败时界面仍显示上一件PASS
                //DataModel.Processmodel.TVTestTestModel1.Status = r.Error;
                //DataModel.Processmodel.TVTestTestModel1.TVInfo = r.Error;
                writeLog($"[耐压1-{testType}] 失败原因: {r.Error}", true);
            }

            // 获取翻译后的状态并添加测试模式前缀
            var rawTvInfo = DataModel.Processmodel.TVTestTestModel1.TVInfo;
            var translatedTvInfo = GetLocalizedTvStatus(rawTvInfo);
            bool isACW = testType == "ACW";
            var localizedTvInfo1 = Utils.TvStatusTranslator.AddTestModePrefix(translatedTvInfo, isACW);
            DataModel.Processmodel.TVTestTestModel1.TVInfo = localizedTvInfo1;

            string wocode = DataModel.Processmodel.TVTestTestModel1.Productinfo.WOCODE;
            string partnoid = DataModel.Processmodel.TVTestTestModel1.Productinfo.PartNOID;
            string sn = DataModel.Processmodel.TVTestTestModel1.Productinfo.SN;

            // 判断是否为双测模式的第二次测试
            bool isSecondTest = CheckIsSecondTest(wocode, partnoid, sn, isACW, testType);

            bool updateTvResult;
            if (isSecondTest)
            {
                // 双测模式第二次测试：插入新记录
                updateTvResult = sqlite.InsertTV_SecondTest(wocode, partnoid, sn,
                    DataModel.Settingmodel.SETTING_DATA.StationCode,
                    DataModel.Settingmodel.SETTING_DATA.MachineID,
                    res,
                    DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage,
                    r.Success,
                    DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent,
                    localizedTvInfo1,
                    DataModel.Settingmodel.SETTING_DATA.TVMeterID1);
                writeLog($"[耐压1-{testType}] 双测模式第二次测试，插入新记录");
            }
            else
            {
                // 单测模式或双测模式第一次测试：更新现有记录
                updateTvResult = sqlite.UpdateTV(wocode, partnoid, sn,
                    res,
                    DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage,
                    r.Success,
                    DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent,
                    localizedTvInfo1,
                    DataModel.Settingmodel.SETTING_DATA.TVMeterID1);
            }
            
            if (!updateTvResult)
            {
                writeLog($"[耐压1-{testType}] ⚠ 数据库更新失败! SN={sn}, RES={res}", true);
            }

            updatetv(sn, res,
                DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage,
                r.Success,
                DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent,
                localizedTvInfo1,
                DataModel.Settingmodel.SETTING_DATA.TVMeterID1,
                testType);

            if (!r.Success)
            {
                var failureStatus1 = GetLocalizedTvStatus(DataModel.Processmodel.TVTestTestModel1.Status);
                DataModel.Settingmodel.Sqlserver.Save_TVProcessData(
                    DataModel.Processmodel.TVTestTestModel1.Productinfo.WOCODE,
                    DataModel.Processmodel.TVTestTestModel1.Productinfo.SN,
                    DataModel.Settingmodel.SETTING_DATA.ProcedureName,
                    failureStatus1,
                    DataModel.Settingmodel.SETTING_DATA.WorkerID,
                    DateTime.Now,
                    DataModel.Settingmodel.SETTING_DATA.TVMeterID1,
                    r.Recordstr
                    );
            }

            writeLog($"[耐压1-{testType}] 测试完成，结果: {(r.Success ? "PASS" : "FAIL")}");
            // 将工位1耐压结果写入 PLC（用于决定是否启用 IR）
            WriteTvOkSignalForIrEnable(1, r.Success);
            PLC_write((DataModel.Settingmodel.AddressStart + 7).ToString(), 1);
        }

        /// <summary>
        /// 检查当前测试是否为双测模式的第二次测试
        ///
        /// 业务逻辑说明：
        /// 在双测模式下，同一个产品需要进行交流(ACW)和直流(DCW)两种耐压测试。
        /// 系统会在TVInfo字段中添加测试模式前缀([ACW]或[DCW])来区分测试类型。
        /// 当检测到已有记录使用另一种测试模式时，说明当前是第二次测试。
        ///
        /// 处理逻辑：
        /// 1. 检查数据库中该SN最新的 ACW/DCW 记录，忽略独立 IR 行
        /// 2. 如果当前是ACW测试但数据库中有DCW记录，则为第二次测试
        /// 3. 如果当前是DCW测试但数据库中有ACW记录，则为第二次测试
        /// 4. 第二次测试时会插入新记录，而不是更新现有记录
        ///
        /// 设计考虑：
        /// - 通过TVInfo前缀标识测试模式，避免修改数据库结构
        /// - 确保双测模式下每种测试都有独立记录，便于数据追溯
        /// - 异常处理确保方法失败时不影响主测试流程
        /// </summary>
        /// <param name="wocode">工单号</param>
        /// <param name="partnoid">规格ID</param>
        /// <param name="sn">产品序列号</param>
        /// <param name="isACW">当前是否为交流测试</param>
        /// <param name="testType">测试类型标识("ACW"或"DCW")</param>
        /// <returns>true=第二次测试，false=第一次测试</returns>
        private bool CheckIsSecondTest(string wocode, string partnoid, string sn, bool isACW, string testType)
        {
            try
            {
                // 获取数据库连接字符串（按工单、规格、SN生成独立的数据库文件）
                string connstr = sqlite.CheckDataBase(wocode, partnoid, sn);
                if (!string.IsNullOrEmpty(connstr))
                {
                    // IR 也会新增独立电测行，但它不能参与 ACW/DCW 双测顺序判断；
                    // 这里只看最新一条耐压模式记录，避免 ACW -> IR -> DCW 时把 DCW 误判为第一次测试。
                    string checkSql = $"SELECT TVInfo FROM BusbarCompressionData WHERE sn='{sn}' AND (TVInfo LIKE '[ACW]%' OR TVInfo LIKE '[DCW]%') ORDER BY id DESC LIMIT 1";
                    var dt = sqlite.Read(checkSql, connstr);
                    if (dt != null && dt.Rows.Count > 0)
                    {
                        // 获取最新记录的TVInfo信息，包含测试模式前缀
                        string existingTvInfo = dt.Rows[0]["TVInfo"]?.ToString() ?? "";

                        // 双测模式判断逻辑：
                        // 如果当前是ACW测试但数据库中已有DCW记录 → 第二次测试
                        // 如果当前是DCW测试但数据库中已有ACW记录 → 第二次测试
                        // 这种设计确保每种测试模式都有独立的记录
                        if ((isACW && existingTvInfo.StartsWith("[DCW]")) ||
                            (!isACW && existingTvInfo.StartsWith("[ACW]")))
                        {
                            writeLog($"[耐压-{testType}] 检测到双测模式，当前为第二次测试，已有记录TVInfo={existingTvInfo}");
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 记录异常但不影响主流程，双测模式检查失败时按第一次测试处理
                writeLog($"[耐压-{testType}] 检查双测模式时异常: {ex.Message}", true);
            }
            return false;
        }

        /// <summary>
        /// TV1 历史兼容入口。
        /// 当前 PLC 主生产路径按 TV1Trig 分流到 <see cref="TV1Process_ACW"/> / <see cref="TV1Process_DCW"/>；
        /// 本入口保留旧单模式流程，不负责 ACW/DCW 双测模式分流，调试或维护时不应作为当前生产主路径使用。
        /// </summary>
        public void TV1Process()
        {
            string s = PLC_Readstring(DataModel.Settingmodel.AddressSN + 25);

            string[] ss = s.Split(';');
            if (ss.Length == 2)
            {
                //DataModel.Processmodel.TakePhotoTestModel.Productinfo = new Model.Record.Productinfo() { SN = ss[0], WOCODE = ss[1], PartNOID = "" };
                DataModel.Processmodel.TVTestTestModel1.Productinfo = new Productinfo() { SN = ss[0], WOCODE = ss[1], PartNOID = DataModel.Processmodel.PartNOID };
                writeLog($"[耐压1] 产品编码读取成功: SN={ss[0]}, WOCODE={ss[1]}");
            }
            else
            {
                writeLog($"[耐压1] ❌ 产品编号读取错误! 原始值=[{s}], 期望格式=[SN;WOCODE], 分段数={ss.Length}", true);
                // 【日志归置】PLC 数据异常属于设备侧错误，按约定归到“日志\\错误”，避免污染“数据库异常”
                writePlcError($"[PLC数据异常]TV1-产品编码格式错误 | 原始值=[{s}], 分段数={ss.Length}, PLC地址=D{DataModel.Settingmodel.AddressSN + 25}");
            }

            float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes);
            DataModel.Processmodel.TVTestTestModel1.Res = res;

            DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage = 0;
            DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent = 0;


            var r = RunTvMeterStartWithDiagnostics(DataModel.Settingmodel.AT9620_1, DataModel.Processmodel.TVTestTestModel1.Productinfo?.SN, DataModel.Processmodel.CurrentTV1TestModeDisplay);
            var localizedTvInfo1 = GetLocalizedTvStatus(DataModel.Processmodel.TVTestTestModel1.TVInfo);
            DataModel.Processmodel.TVTestTestModel1.TVInfo = localizedTvInfo1;

            bool updateTvResult = sqlite.UpdateTV(DataModel.Processmodel.TVTestTestModel1.Productinfo.WOCODE,
                DataModel.Processmodel.TVTestTestModel1.Productinfo.PartNOID,
                DataModel.Processmodel.TVTestTestModel1.Productinfo.SN,
                res,
                DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage,
                r.Success,
                DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent,
                localizedTvInfo1,
                DataModel.Settingmodel.SETTING_DATA.TVMeterID1
                );
            if (!updateTvResult)
            {
                writeLog($"[耐压1] ⚠ UpdateTV更新失败! SN={DataModel.Processmodel.TVTestTestModel1.Productinfo.SN}, RES={res}", true);
            }

            updatetv(DataModel.Processmodel.TVTestTestModel1.Productinfo.SN,
                res,
                DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage,
                r.Success,
                DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent,
                localizedTvInfo1,
                DataModel.Settingmodel.SETTING_DATA.TVMeterID1,
                DataModel.Processmodel.CurrentTV1TestModeDisplay
                );

            //if (r.Success)
            if (!r.Success)
            {
                var failureStatus1 = GetLocalizedTvStatus(DataModel.Processmodel.TVTestTestModel1.Status);
                DataModel.Settingmodel.Sqlserver.Save_TVProcessData(
                    DataModel.Processmodel.TVTestTestModel1.Productinfo.WOCODE,
                    DataModel.Processmodel.TVTestTestModel1.Productinfo.SN,
                    DataModel.Settingmodel.SETTING_DATA.ProcedureName,
                    failureStatus1,
                    DataModel.Settingmodel.SETTING_DATA.WorkerID,
                    DateTime.Now,
                    DataModel.Settingmodel.SETTING_DATA.TVMeterID1,
                    r.Recordstr
                    );
            }

            // 兼容旧流程：将工位1耐压结果写入 PLC（用于决定是否启用 IR）
            WriteTvOkSignalForIrEnable(1, r.Success);
            PLC_write((DataModel.Settingmodel.AddressStart + 7).ToString(), 1);
        }

        /// <summary>
        /// 执行ACW交流耐压测试（TV2工位）
        /// </summary>
        /// <remarks>
        /// 当TV2Trig=1时调用此方法。
        /// 触发前统一下发ACW参数到AT9620设备。
        /// </remarks>
        public void TV2Process_ACW()
        {
            writeLog($"[耐压2-ACW] 开始ACW交流耐压测试");
            
            // 触发前统一下发参数，避免设备参数未同步
            writeLog($"[耐压2-ACW] 开始下发ACW参数");
            DataModel.Settingmodel.AT9620_2.TVParameter = DataModel.Processmodel.ACWParameter;
            var downloadResult = DataModel.Settingmodel.AT9620_2.Download();
            if (!downloadResult.Success)
            {
                writeLog($"[耐压2-ACW] ❌ ACW参数下发失败: {downloadResult.Error}", true);
                // 参数下发失败视为该工位耐压 NG：通知 PLC 不要启用 IR
                WriteTvOkSignalForIrEnable(2, false);
                PLC_write((DataModel.Settingmodel.AddressStart + 9).ToString(), (UInt16)2);
                return;
            }
            DataModel.Processmodel.LastTV2TestMode = AT9620.TestMode.ACW;
            writeLog($"[耐压2-ACW] ACW参数下发成功");
            Thread.Sleep(500);

            // 执行测试（复用现有逻辑）
            TV2Process_Core("ACW");
        }

        /// <summary>
        /// 执行DCW直流耐压测试（TV2工位）
        /// </summary>
        /// <remarks>
        /// 当TV2Trig=2时调用此方法。
        /// 触发前统一下发DCW参数到AT9620设备。
        /// </remarks>
        public void TV2Process_DCW()
        {
            writeLog($"[耐压2-DCW] 开始DCW直流耐压测试");
            
            // 触发前统一下发参数，避免设备参数未同步
            writeLog($"[耐压2-DCW] 开始下发DCW参数");
            DataModel.Settingmodel.AT9620_2.TVParameter = DataModel.Processmodel.DCWParameter;
            var downloadResult = DataModel.Settingmodel.AT9620_2.Download();
            if (!downloadResult.Success)
            {
                writeLog($"[耐压2-DCW] ❌ DCW参数下发失败: {downloadResult.Error}", true);
                // 参数下发失败视为该工位耐压 NG：通知 PLC 不要启用 IR
                WriteTvOkSignalForIrEnable(2, false);
                PLC_write((DataModel.Settingmodel.AddressStart + 9).ToString(), (UInt16)2);
                return;
            }
            DataModel.Processmodel.LastTV2TestMode = AT9620.TestMode.DCW;
            writeLog($"[耐压2-DCW] DCW参数下发成功");
            Thread.Sleep(500);

            // 执行测试（复用现有逻辑）
            TV2Process_Core("DCW");
        }

        /// <summary>
        /// TV2耐压测试核心逻辑（ACW/DCW共用）。
        /// 业务说明：只有读到本次完整的 SN;WOCODE 后才允许继续测试和落记录；
        /// 若读码失败，则视为该工位本次流程无法绑定产品，回写耐压NG并中止，避免沿用旧SN。
        /// </summary>
        /// <param name="testType">测试类型标识，用于日志区分（"ACW"或"DCW"）</param>
        private void TV2Process_Core(string testType)
        {
            string snCode;
            string woCode;
            string rawCode;
            if (TryReadProductCodeFromPlc(DataModel.Settingmodel.AddressSN + 25 * 2, $"耐压2-{testType}", out snCode, out woCode, out rawCode))
            {
                DataModel.Processmodel.TVTestTestModel2.Productinfo = new Productinfo() { SN = snCode, WOCODE = woCode, PartNOID = DataModel.Processmodel.PartNOID };
                writeLog($"[耐压2-{testType}] 产品编码读取成功: SN={snCode}, WOCODE={woCode}");
            }
            else
            {
                // 读不到本次产品编码时不能继续电测，否则有串SN风险；回写本站NG让PLC走异常/重试分支。
                WriteTvOkSignalForIrEnable(2, false);
                PLC_write((DataModel.Settingmodel.AddressStart + 9).ToString(), (UInt16)2);
                return;
            }

            float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes + 1 * 2);
            DataModel.Processmodel.TVTestTestModel2.Res = res;

            DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage = 0;
            DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent = 0;

            // 清空上一次测试残留，避免本次启动失败时沿用旧信息导致“结果/说明”不一致
            DataModel.Processmodel.TVTestTestModel2.Status = string.Empty;
            DataModel.Processmodel.TVTestTestModel2.TVInfo = string.Empty;
            DataModel.Processmodel.TVTestTestModel2.Voltage = 0;
            DataModel.Processmodel.TVTestTestModel2.Current = 0;
            DataModel.Processmodel.TVTestTestModel2.Time = 0;

            var r = RunTvMeterStartWithDiagnostics(DataModel.Settingmodel.AT9620_2, DataModel.Processmodel.TVTestTestModel2.Productinfo?.SN, testType);
            // 记录耐压失败原因到界面日志，便于首件异常定位
            if (!r.Success && !string.IsNullOrWhiteSpace(r.Error))
            {
                // 将失败原因写入状态/信息，避免首件启动失败时界面仍显示上一件PASS
                //DataModel.Processmodel.TVTestTestModel2.Status = r.Error;
                //DataModel.Processmodel.TVTestTestModel2.TVInfo = r.Error;
                writeLog($"[耐压2-{testType}] 失败原因: {r.Error}", true);
            }

            // 获取翻译后的状态并添加测试模式前缀
            var rawTvInfo = DataModel.Processmodel.TVTestTestModel2.TVInfo;
            var translatedTvInfo = GetLocalizedTvStatus(rawTvInfo);
            bool isACW = testType == "ACW";
            var localizedTvInfo2 = Utils.TvStatusTranslator.AddTestModePrefix(translatedTvInfo, isACW);
            DataModel.Processmodel.TVTestTestModel2.TVInfo = localizedTvInfo2;

            string wocode = DataModel.Processmodel.TVTestTestModel2.Productinfo.WOCODE;
            string partnoid = DataModel.Processmodel.TVTestTestModel2.Productinfo.PartNOID;
            string sn = DataModel.Processmodel.TVTestTestModel2.Productinfo.SN;

            // 判断是否为双测模式的第二次测试
            bool isSecondTest = CheckIsSecondTest(wocode, partnoid, sn, isACW, testType);

            bool updateTvResult;
            if (isSecondTest)
            {
                // 双测模式第二次测试：插入新记录
                updateTvResult = sqlite.InsertTV_SecondTest(wocode, partnoid, sn,
                    DataModel.Settingmodel.SETTING_DATA.StationCode,
                    DataModel.Settingmodel.SETTING_DATA.MachineID,
                    res,
                    DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage,
                    r.Success,
                    DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent,
                    localizedTvInfo2,
                    DataModel.Settingmodel.SETTING_DATA.TVMeterID2);
                writeLog($"[耐压2-{testType}] 双测模式第二次测试，插入新记录");
            }
            else
            {
                // 单测模式或双测模式第一次测试：更新现有记录
                updateTvResult = sqlite.UpdateTV(wocode, partnoid, sn,
                    res,
                    DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage,
                    r.Success,
                    DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent,
                    localizedTvInfo2,
                    DataModel.Settingmodel.SETTING_DATA.TVMeterID2);
            }
            
            if (!updateTvResult)
            {
                writeLog($"[耐压2-{testType}] ⚠ 数据库更新失败! SN={sn}, RES={res}", true);
            }

            updatetv(sn, res,
                DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage,
                r.Success,
                DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent,
                localizedTvInfo2,
                DataModel.Settingmodel.SETTING_DATA.TVMeterID2,
                testType);

            if (!r.Success)
            {
                var failureStatus2 = GetLocalizedTvStatus(DataModel.Processmodel.TVTestTestModel2.Status);
                DataModel.Settingmodel.Sqlserver.Save_TVProcessData(
                    DataModel.Processmodel.TVTestTestModel2.Productinfo.WOCODE,
                    DataModel.Processmodel.TVTestTestModel2.Productinfo.SN,
                    DataModel.Settingmodel.SETTING_DATA.ProcedureName,
                    failureStatus2,
                    DataModel.Settingmodel.SETTING_DATA.WorkerID,
                    DateTime.Now,
                    DataModel.Settingmodel.SETTING_DATA.TVMeterID2,
                    r.Recordstr
                    );
            }

            writeLog($"[耐压2-{testType}] 测试完成，结果: {(r.Success ? "PASS" : "FAIL")}");
            // 将工位2耐压结果写入 PLC（用于决定是否启用 IR）
            WriteTvOkSignalForIrEnable(2, r.Success);
            PLC_write((DataModel.Settingmodel.AddressStart + 9).ToString(), 1);
        }

        /// <summary>
        /// TV2 历史兼容入口。
        /// 当前 PLC 主生产路径按 TV2Trig 分流到 <see cref="TV2Process_ACW"/> / <see cref="TV2Process_DCW"/>；
        /// 本入口保留旧单模式流程，不负责 ACW/DCW 双测模式分流，调试或维护时不应作为当前生产主路径使用。
        /// </summary>
        public void TV2Process()
        {
            string s = PLC_Readstring(DataModel.Settingmodel.AddressSN + 25 * 2);

            string[] ss = s.Split(';');
            if (ss.Length == 2)
            {
                //DataModel.Processmodel.TakePhotoTestModel.Productinfo = new Model.Record.Productinfo() { SN = ss[0], WOCODE = ss[1], PartNOID = "" };
                DataModel.Processmodel.TVTestTestModel2.Productinfo = new Productinfo() { SN = ss[0], WOCODE = ss[1], PartNOID = DataModel.Processmodel.PartNOID };
                writeLog($"[耐压2] 产品编码读取成功: SN={ss[0]}, WOCODE={ss[1]}");
            }
            else
            {
                //writeLog($"耐压2产品编号读取错误:{s}");
                writeLog($"[耐压2] ❌ 产品编号读取错误! 原始值=[{s}], 期望格式=[SN;WOCODE], 分段数={ss.Length}", true);
                writeLog($"[耐压2] PLC地址: D{DataModel.Settingmodel.AddressSN + 25 * 2}, 请检查PLC寄存器值", true);
                // 【日志归置】PLC 数据异常属于设备侧错误，按约定归到“日志\\错误”，避免污染“数据库异常”
                writePlcError($"[PLC数据异常]TV2-产品编码格式错误 | 原始值=[{s}], 分段数={ss.Length}, PLC地址=D{DataModel.Settingmodel.AddressSN + 25 * 2}");
            }

            float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes + 1 * 2);
            DataModel.Processmodel.TVTestTestModel2.Res = res;

            DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage = 0;
            DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent = 0;
            var r = RunTvMeterStartWithDiagnostics(DataModel.Settingmodel.AT9620_2, DataModel.Processmodel.TVTestTestModel2.Productinfo?.SN, DataModel.Processmodel.CurrentTV2TestModeDisplay);
            var localizedTvInfo2 = GetLocalizedTvStatus(DataModel.Processmodel.TVTestTestModel2.TVInfo);
            DataModel.Processmodel.TVTestTestModel2.TVInfo = localizedTvInfo2;

            bool updateTvResult = sqlite.UpdateTV(DataModel.Processmodel.TVTestTestModel2.Productinfo.WOCODE,
               DataModel.Processmodel.TVTestTestModel2.Productinfo.PartNOID,
               DataModel.Processmodel.TVTestTestModel2.Productinfo.SN,
               res,
               DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage,
               r.Success,
               DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent,
               localizedTvInfo2,
               DataModel.Settingmodel.SETTING_DATA.TVMeterID2
               );
            if (!updateTvResult)
            {
                writeLog($"[耐压2] ⚠ UpdateTV更新失败! SN={DataModel.Processmodel.TVTestTestModel2.Productinfo.SN}, RES={res}", true);
            }

            updatetv(DataModel.Processmodel.TVTestTestModel2.Productinfo.SN,
                res, DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage,
                r.Success,
                DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent,
                localizedTvInfo2,
                DataModel.Settingmodel.SETTING_DATA.TVMeterID2,
                DataModel.Processmodel.CurrentTV2TestModeDisplay

                );

            if (!r.Success)
            {
                var failureStatus2 = GetLocalizedTvStatus(DataModel.Processmodel.TVTestTestModel2.Status);
                DataModel.Settingmodel.Sqlserver.Save_TVProcessData(
                    DataModel.Processmodel.TVTestTestModel2.Productinfo.WOCODE,
                    DataModel.Processmodel.TVTestTestModel2.Productinfo.SN,
                    DataModel.Settingmodel.SETTING_DATA.ProcedureName,
                    failureStatus2,
                    DataModel.Settingmodel.SETTING_DATA.WorkerID,
                    DateTime.Now,
                    DataModel.Settingmodel.SETTING_DATA.TVMeterID2,
                    r.Recordstr
                    );
            }

            // 兼容旧流程：将工位2耐压结果写入 PLC（用于决定是否启用 IR）
            WriteTvOkSignalForIrEnable(2, r.Success);
            PLC_write((DataModel.Settingmodel.AddressStart + 9).ToString(), 1);

        }
        /// <summary>
        /// TV3 历史兼容入口。
        /// 当前 PLC 轮询中 TV3 触发已禁用，工位3的绝缘电阻测试由 IR 流程承担；
        /// 本方法体仅保留历史耐压3路径，恢复使用前必须重新核对 TV3 参数、记录字段和 PLC 完成信号。
        /// </summary>
        public void TV3Process()
        {
            string s = PLC_Readstring(DataModel.Settingmodel.AddressSN + 25 * 3);

            string[] ss = s.Split(';');
            if (ss.Length == 2)
            {
                //DataModel.Processmodel.TakePhotoTestModel.Productinfo = new Model.Record.Productinfo() { SN = ss[0], WOCODE = ss[1], PartNOID = "" };
                DataModel.Processmodel.TVTestTestModel3.Productinfo = new Productinfo() { SN = ss[0], WOCODE = ss[1], PartNOID = DataModel.Processmodel.PartNOID };
                writeLog($"[耐压3] 产品编码读取成功: SN={ss[0]}, WOCODE={ss[1]}");
            }
            else
            {
                //writeLog($"耐压3产品编号读取错误:{s}");
                writeLog($"[耐压3] ❌ 产品编号读取错误! 原始值=[{s}], 期望格式=[SN;WOCODE], 分段数={ss.Length}", true);
                // 【日志归置】PLC 数据异常属于设备侧错误，按约定归到“日志\\错误”，避免污染“数据库异常”
                writePlcError($"[PLC数据异常]TV3-产品编码格式错误 | 原始值=[{s}], 分段数={ss.Length}, PLC地址=D{DataModel.Settingmodel.AddressSN + 25 * 3}");
            }

            float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes + 2 * 2);
            DataModel.Processmodel.TVTestTestModel3.Res = res;

            DataModel.Processmodel.TVTestTestModel3.TVMaxVoltage = 0;
            DataModel.Processmodel.TVTestTestModel3.TVMaxCurrent = 0;

            var r = RunTvMeterStartWithDiagnostics(DataModel.Settingmodel.AT9620_3, DataModel.Processmodel.TVTestTestModel3.Productinfo?.SN, "ACW");
            var localizedTvInfo3 = GetLocalizedTvStatus(DataModel.Processmodel.TVTestTestModel3.TVInfo);
            DataModel.Processmodel.TVTestTestModel3.TVInfo = localizedTvInfo3;

            bool updateTvResult = sqlite.UpdateTV(DataModel.Processmodel.TVTestTestModel3.Productinfo.WOCODE,
               DataModel.Processmodel.TVTestTestModel3.Productinfo.PartNOID,
               DataModel.Processmodel.TVTestTestModel3.Productinfo.SN,
               res,
               DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage,
               r.Success,
               DataModel.Processmodel.TVTestTestModel3.TVMaxCurrent,
               localizedTvInfo3,
               DataModel.Settingmodel.SETTING_DATA.TVMeterID3
               );
            if (!updateTvResult)
            {
                writeLog($"[耐压3] ⚠ UpdateTV更新失败! SN={DataModel.Processmodel.TVTestTestModel3.Productinfo.SN}, RES={res}", true);
            }

            updatetv(DataModel.Processmodel.TVTestTestModel3.Productinfo.SN,
                res,
                DataModel.Processmodel.TVTestTestModel3.TVMaxVoltage
                , r.Success,
                DataModel.Processmodel.TVTestTestModel3.TVMaxCurrent,
                localizedTvInfo3,
                DataModel.Settingmodel.SETTING_DATA.TVMeterID3,
                "ACW"
                );

            if (!r.Success)
            {
                var failureStatus3 = GetLocalizedTvStatus(DataModel.Processmodel.TVTestTestModel3.Status);
                DataModel.Settingmodel.Sqlserver.Save_TVProcessData(
                    DataModel.Processmodel.TVTestTestModel3.Productinfo.WOCODE,
                    DataModel.Processmodel.TVTestTestModel3.Productinfo.SN,
                    DataModel.Settingmodel.SETTING_DATA.ProcedureName,
                    failureStatus3,
                    DataModel.Settingmodel.SETTING_DATA.WorkerID,
                    DateTime.Now,
                    DataModel.Settingmodel.SETTING_DATA.TVMeterID3,
                    r.Recordstr
                    );
            }
            PLC_write((DataModel.Settingmodel.AddressStart + 11).ToString(), 1);

        }

        /// <summary>
        /// IR绝缘电阻测试流程（独立电测工位）。
        /// 业务链路：
        /// 1. PLC触发后读取当前产品编码与接触电阻；
        /// 2. AT6835FL执行绝缘电阻测试；
        /// 3. SQLite写入一条 TestMode=IR 的独立电测行；
        /// 4. 失败时复用耐压失败追溯入口，便于F7按仪表号/TVInfo追查；
        /// 5. UI复用 updatetv(...) 的 ACW/DCW/IR 通用显示入口；
        /// 6. finally 必须回写PLC OK/NG，避免现场流程卡在IR工位。
        /// </summary>
        public void IRProcess()
        {
            string sn = string.Empty;
            string wocode = string.Empty;
            string partnoid = DataModel.Processmodel.PartNOID;
            bool irSuccess = false;

            try
            {
                writeLog("[IR测试] 收到PLC触发信号，开始绝缘电阻测试");

                // 1) 从PLC读取产品SN（复用工位3 SN地址）
                string rawCode;
                if (!TryReadProductCodeFromPlc(DataModel.Settingmodel.AddressSN + 25 * 3, "IR测试", out sn, out wocode, out rawCode))
                {
                    return;
                }

                // 2) 读取接触电阻/阻值（写入 RES 列供 CHECK 使用）
                // 注意：这不是 IR 仪器返回的绝缘电阻（绝缘电阻在 r.Resistance 中）。
                float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes + 2 * 2);
                if (float.IsNaN(res) || res <= 0)
                {
                    // 兜底：若 PLC 未写入或通讯异常，尝试从同一 SN 的最近一条数据库记录复用 RES（例如 ACW/DCW 行）。
                    try
                    {
                        float dbRes;
                        if (SQLITEDATABASE.sqlite.TryGetLatestRes(wocode, partnoid, sn, out dbRes))
                        {
                            res = dbRes;
                            writeLog($"[IR测试] ⚠ PLC阻值无效，已从数据库兜底复用RES={res}（SN={sn}）", true);
                        }
                        else
                        {
                            res = -1;
                            writeLog($"[IR测试] ⚠ PLC阻值无效且数据库兜底失败，RES置为-1（SN={sn}）", true);
                        }
                    }
                    catch
                    {
                        res = -1;
                    }
                }

                // 3) 执行IR测试（AT6835FL Start：自检→下发→STAT:CHAR 自动流程→按 TIME 窗口取稳定结果）
                SyncIrAt6835RuntimeFlagsFromSettings("[IR自动测试]");
                var r = DataModel.Settingmodel.AT6835FL_1.Start();
                irSuccess = r.Success;

                // 4) 构造区分信息：写入 TVInfo 用于多测判定
                string irInfo = $"[IR] {(r.Success ? r.Judgment : r.Error)}";

                // 5) SQLite：插入IR独立电测行，区别靠 TVInfo 前缀
                bool dbOk = sqlite.InsertIR_Test(
                    wocode,
                    partnoid,
                    sn,
                    DataModel.Settingmodel.SETTING_DATA.StationCode,
                    DataModel.Settingmodel.SETTING_DATA.MachineID,
                    res,
                    (float)r.Resistance,
                    r.Success,
                    (float)r.LeakCurrent,
                    irInfo,
                    DataModel.Settingmodel.SETTING_DATA.IRMeterID);

                if (!dbOk)
                {
                    writeLog($"[IR测试] ⚠ SQLite InsertIR_Test失败! SN={sn}", true);
                }

                // 6) F7失败追溯（仅失败时）
                if (!r.Success)
                {
                    // 与耐压失败追溯保持同一落库入口：dr_TVProcess
                    // （通过 TVMETERID + FTVResult（RESULT字段）区分IR）
                    DataModel.Settingmodel.Sqlserver.Save_TVProcessData(
                        wocode,
                        sn,
                        DataModel.Settingmodel.SETTING_DATA.ProcedureName,
                        irInfo,
                        DataModel.Settingmodel.SETTING_DATA.WorkerID,
                        DateTime.Now,
                        DataModel.Settingmodel.SETTING_DATA.IRMeterID,
                        r.Recordstr);
                }

                // 7) 更新界面：复用ACW/DCW同一套电测UI记录逻辑，完成后新增/更新 TestMode=IR 独立行
                updatetv(sn, res, (float)r.Resistance, r.Success, (float)r.LeakCurrent, irInfo,
                    DataModel.Settingmodel.SETTING_DATA.IRMeterID, "IR", wocode, partnoid);

                writeLog($"[IR测试] 测试完成，结果: {(r.Success ? "PASS" : "FAIL")}, SN={sn}");
            }
            catch (Exception ex)
            {
                irSuccess = false;
                writeLog($"[IR测试] ❌ 流程异常: {ex.Message}, SN={sn}", true);
                sqlite.WriteErrorLog("[IR测试异常]IRProcess失败", $"异常: {ex.Message}, 堆栈: {ex.StackTrace}", sn, wocode);
            }
            finally
            {
                // 8) PLC回写 D1015: 1=OK, 2=NG。无论中途异常与否都回写，避免PLC卡流程。
                bool plcWriteOk = PLC_write(DataModel.Settingmodel.IRResultAddress.ToString(), (UInt16)(irSuccess ? 1 : 2));
                if (!plcWriteOk)
                {
                    writeLog($"[IR测试] ⚠ PLC结果回写失败 D{DataModel.Settingmodel.IRResultAddress}={(irSuccess ? 1 : 2)}, SN={sn}", true);
                }
            }
        }

        /// <summary>
        /// 阻值1读取处理：从PLC读取阻值并保存到内存
        /// 触发地址：M3035，读取地址：D1200
        /// </summary>
        public void Res1Process()
        {
            try
            {
                // 1. 读取阻值并保存到临时模型
                float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes);
                DataModel.Processmodel.TVTestTestModel1.Res = res;
                writeLog($"阻值1读取完成 D{DataModel.Settingmodel.AddressRes}={res}");
                
                // 2. 从PLC读取SN，立即更新到ProductInfoRecord
                // 这样即使阻值NG导致PLC跳过耐压测试，CHECK阶段也能获取到正确的阻值数据
                try
                {
                    string s = PLC_Readstring(DataModel.Settingmodel.AddressSN + 25); // TV1工位SN地址
                    string[] ss = s.Split(';');
                    if (ss.Length == 2)
                    {
                        string sn = ss[0];
                        string wocode = ss[1];
                        UpdateResValue(sn, res);

                        // 同步将阻值写入SQLite，避免“阻值NG跳过耐压”导致数据库缺失RES
                        bool updateResDbOk = sqlite.UpdateResOnly(wocode, DataModel.Processmodel.PartNOID, sn, res);
                        if (!updateResDbOk)
                        {
                            writeLog($"[阻值1] ⚠ UpdateResOnly写库失败：WOCODE={wocode}, SN={sn}, Res={res}", true);
                        }

                        writeLog($"阻值1已更新到记录 SN={sn}, Res={res}");
                    }
                    else
                    {
                        writeLog($"[阻值1] SN读取格式错误，原始值=[{s}]，跳过ProductInfoRecord更新", false);
                    }
                }
                catch (Exception exSN)
                {
                    writeLog($"[阻值1] 读取SN异常: {exSN.Message}，ProductInfoRecord未更新", false);
                }
            }
            catch (Exception ex)
            {
                writeLog($"[ERROR] 阻值1读取异常 M{DataModel.Settingmodel.Res1TrigAddress}/D{DataModel.Settingmodel.AddressRes}: {ex.Message}");
            }
        }

        /// <summary>
        /// 阻值2读取处理：从PLC读取阻值并保存到内存
        /// 触发地址：M3036，读取地址：D1202
        /// </summary>
        public void Res2Process()
        {
            try
            {
                // 1. 读取阻值并保存到临时模型
                float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes + 1 * 2);
                DataModel.Processmodel.TVTestTestModel2.Res = res;
                writeLog($"阻值2读取完成 D{DataModel.Settingmodel.AddressRes + 2}={res}");
                
                // 2. 从PLC读取SN，立即更新到ProductInfoRecord
                // 这样即使阻值NG导致PLC跳过耐压测试，CHECK阶段也能获取到正确的阻值数据
                try
                {
                    string s = PLC_Readstring(DataModel.Settingmodel.AddressSN + 25 * 2); // TV2工位SN地址
                    string[] ss = s.Split(';');
                    if (ss.Length == 2)
                    {
                        string sn = ss[0];
                        string wocode = ss[1];
                        UpdateResValue(sn, res);

                        // 同步将阻值写入SQLite，避免“阻值NG跳过耐压”导致数据库缺失RES
                        bool updateResDbOk = sqlite.UpdateResOnly(wocode, DataModel.Processmodel.PartNOID, sn, res);
                        if (!updateResDbOk)
                        {
                            writeLog($"[阻值2] ⚠ UpdateResOnly写库失败：WOCODE={wocode}, SN={sn}, Res={res}", true);
                        }

                        writeLog($"阻值2已更新到记录 SN={sn}, Res={res}");
                    }
                    else
                    {
                        writeLog($"[阻值2] SN读取格式错误，原始值=[{s}]，跳过ProductInfoRecord更新", false);
                    }
                }
                catch (Exception exSN)
                {
                    writeLog($"[阻值2] 读取SN异常: {exSN.Message}，ProductInfoRecord未更新", false);
                }
            }
            catch (Exception ex)
            {
                writeLog($"[ERROR] 阻值2读取异常 M{DataModel.Settingmodel.Res2TrigAddress}/D{DataModel.Settingmodel.AddressRes + 2}: {ex.Message}");
            }
        }

        /// <summary>
        /// 阻值3读取处理：从PLC读取阻值并保存到内存
        /// 触发地址：M3037，读取地址：D1204
        /// </summary>
        public void Res3Process()
        {
            try
            {
                // 1. 读取阻值并保存到临时模型
                float res = PLC_ReadFloat(DataModel.Settingmodel.AddressRes + 2 * 2);
                DataModel.Processmodel.TVTestTestModel3.Res = res;
                writeLog($"阻值3读取完成 D{DataModel.Settingmodel.AddressRes + 4}={res}");
                
                // 2. 从PLC读取SN，立即更新到ProductInfoRecord
                // 这样即使阻值NG导致PLC跳过耐压测试，CHECK阶段也能获取到正确的阻值数据
                try
                {
                    string s = PLC_Readstring(DataModel.Settingmodel.AddressSN + 25 * 3); // TV3工位SN地址
                    string[] ss = s.Split(';');
                    if (ss.Length == 2)
                    {
                        string sn = ss[0];
                        string wocode = ss[1];
                        UpdateResValue(sn, res);

                        // 同步将阻值写入SQLite，避免“阻值NG跳过耐压”导致数据库缺失RES
                        bool updateResDbOk = sqlite.UpdateResOnly(wocode, DataModel.Processmodel.PartNOID, sn, res);
                        if (!updateResDbOk)
                        {
                            writeLog($"[阻值3] ⚠ UpdateResOnly写库失败：WOCODE={wocode}, SN={sn}, Res={res}", true);
                        }

                        writeLog($"阻值3已更新到记录 SN={sn}, Res={res}");
                    }
                    else
                    {
                        writeLog($"[阻值3] SN读取格式错误，原始值=[{s}]，跳过ProductInfoRecord更新", false);
                    }
                }
                catch (Exception exSN)
                {
                    writeLog($"[阻值3] 读取SN异常: {exSN.Message}，ProductInfoRecord未更新", false);
                }
            }
            catch (Exception ex)
            {
                writeLog($"[ERROR] 阻值3读取异常 M{DataModel.Settingmodel.Res3TrigAddress}/D{DataModel.Settingmodel.AddressRes + 4}: {ex.Message}");
            }
        }

        /// <summary>
        /// 电测结果的通用 UI 更新入口。
        ///
        /// 业务含义：
        /// - ACW/DCW/IR 都以 ProductInfoRecord 显示，依靠 TestMode 区分同一 SN 的多条电测记录；
        /// - 已有同 SN + TestMode 行时直接更新，保持界面行稳定；
        /// - 只有同 SN 但无当前模式行时复制基础记录，保留拍照/压力等前序信息；
        /// - IR 这类独立电测工位可能没有前序拍照占位行，因此允许调用方传入工单/料号创建最小显示行。
        /// </summary>
        /// <param name="SN">产品序列号，用于在集合中定位记录。</param>
        private void updatetv(string SN, float res, float maxvoltage, bool result, float maxcurrent, string tvinfo, string tvmeterid, string testMode,
                              string fallbackWocode = null, string fallbackPartnoid = null)
        {
            App.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    // 双测模式下，同一SN会产生两条电测记录（[ACW]/[DCW]）。
                    // 界面行按「SN + TestMode」定位，避免第二次电测结果覆盖第一次记录。
                    // 若当前模式记录不存在：
                    // 1) 优先复用“尚未写入TV结果”的占位记录（拍照后创建的记录）作为第一次电测；
                    // 2) 若已存在另一模式记录，则复制一条新记录用于第二次电测，确保界面可追溯两次电测。

                    ProductInfoRecord firstSnRecord = null;
                    ProductInfoRecord target = null;
                    int targetIndex = -1;
                    bool refreshExistingTarget = false;

                    // 1) 优先找同SN且模式匹配的记录（存在则直接更新）
                    for (int idx = 0; idx < DataModel.Recordmodel.ProductInfoRecords.Count; idx++)
                    {
                        var p = DataModel.Recordmodel.ProductInfoRecords[idx];
                        if (p?.Productinfo?.SN != SN) continue;

                        if (firstSnRecord == null) firstSnRecord = p;

                        if (!string.IsNullOrWhiteSpace(testMode) &&
                            string.Equals(p.TestMode, testMode, StringComparison.OrdinalIgnoreCase))
                        {
                            target = p;
                            targetIndex = idx;
                            refreshExistingTarget = true;
                            break;
                        }
                    }

                    // 2) 若没有同模式记录，尝试复用“占位记录”（第一次电测：避免创建空白的另一模式行）
                    if (target == null)
                    {
                        for (int idx = 0; idx < DataModel.Recordmodel.ProductInfoRecords.Count; idx++)
                        {
                            var p = DataModel.Recordmodel.ProductInfoRecords[idx];
                            if (p?.Productinfo?.SN != SN) continue;

                            bool hasTvData = !string.IsNullOrWhiteSpace(p.TVInfo) ||
                                             !string.IsNullOrWhiteSpace(p.TVMeterID) ||
                                             p.TVMaxVoltage != 0 ||
                                             p.TVMaxCurrent != 0;
                            if (!hasTvData)
                            {
                                target = p;
                                targetIndex = idx;
                                refreshExistingTarget = true;
                                break;
                            }
                        }
                    }

                    // 3) 仍未找到目标：认为是双测第二次测试，复制一条新记录显示第二次电测结果
                    if (target == null && firstSnRecord != null)
                    {
                        var newRecord = new ProductInfoRecord
                        {
                            StationCode = firstSnRecord.StationCode,
                            EQUIPMENTID = firstSnRecord.EQUIPMENTID,
                            Productinfo = firstSnRecord.Productinfo,
                            TakePhoto1 = firstSnRecord.TakePhoto1,
                            AppearanceInspection = firstSnRecord.AppearanceInspection,
                            Pressure_Max = firstSnRecord.Pressure_Max,
                            Pressure_Average = firstSnRecord.Pressure_Average,
                            Pressure_Min = firstSnRecord.Pressure_Min,
                            Pressure_Result = firstSnRecord.Pressure_Result,
                            Report = firstSnRecord.Report,
                            DateTime = DateTime.Now,
                            TestMode = testMode
                        };

                        DataModel.Recordmodel.ProductInfoRecords.Insert(0, newRecord);
                        target = newRecord;
                    }

                    // 4) 仍未找到目标但调用方提供了产品信息：创建最小电测行（IR独立工位可走到这里）
                    if (target == null && !string.IsNullOrWhiteSpace(SN) && !string.IsNullOrWhiteSpace(fallbackWocode))
                    {
                        var newRecord = new ProductInfoRecord
                        {
                            StationCode = DataModel.Settingmodel.SETTING_DATA.StationCode,
                            EQUIPMENTID = DataModel.Settingmodel.SETTING_DATA.MachineID,
                            Productinfo = new Productinfo
                            {
                                SN = SN,
                                WOCODE = fallbackWocode ?? string.Empty,
                                PartNOID = string.IsNullOrWhiteSpace(fallbackPartnoid) ? DataModel.Processmodel.PartNOID : fallbackPartnoid
                            },
                            DateTime = DateTime.Now,
                            TestMode = testMode
                        };

                        DataModel.Recordmodel.ProductInfoRecords.Insert(0, newRecord);
                        target = newRecord;
                        writeLog($"[电测] 内存无同SN记录，已新建界面行 SN={SN}, 模式={testMode}");
                    }

                    if (target != null)
                    {
                        target.TestMode = testMode;
                        target.Res = res;
                        target.TVMaxVoltage = maxvoltage;
                        target.TVMaxCurrent = maxcurrent;
                        target.TVResult = result;
                        target.TVInfo = tvinfo;
                        target.TVMeterID = tvmeterid;
                        target.DateTime = DateTime.Now;

                        if (refreshExistingTarget &&
                            targetIndex >= 0 &&
                            targetIndex < DataModel.Recordmodel.ProductInfoRecords.Count)
                        {
                            // ProductInfoRecord uses plain auto-properties, so replace the item
                            // to force DataGrid refresh/re-sort without changing source order.
                            DataModel.Recordmodel.ProductInfoRecords[targetIndex] = target;
                        }
                    }
                    else
                    {
                        // 极端情况：未找到任何记录（例如拍照留底记录未创建/PLC SN异常），记录日志便于现场定位
                        writeLog($"[耐压] ⚠ 未找到内存记录，无法更新界面电测结果：SN={SN}, 模式={testMode}", true);
                        sqlite.WriteErrorLog("UPDATETV_RECORD_NOT_FOUND", $"未找到ProductInfoRecord，无法更新电测结果，模式={testMode}", SN);
                    }
                }
                catch (Exception ex)
                {
                    sqlite.WriteErrorLog("UPDATETV_EXCEPTION", $"更新耐压/阻值数据失败: {ex.Message}", SN);
                }
            }));
        }

        /// <summary>
        /// 根据产品 SN 仅更新阻值字段到 ProductInfoRecord
        /// 业务含义：
        /// - 在阻值测试完成后立即调用，确保阻值数据及时更新到内存记录；
        /// - 即使后续阻值NG导致PLC跳过耐压测试，CHECK阶段也能获取到正确的阻值进行判断。
        /// </summary>
        /// <param name="SN">产品序列号，用于在集合中定位记录</param>
        /// <param name="res">阻值测量值</param>
        private void UpdateResValue(string SN, float res)
        {
            App.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    foreach (var p in DataModel.Recordmodel.ProductInfoRecords)
                    {
                        if (p.Productinfo.SN == SN)
                        {
                            p.Res = res;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    sqlite.WriteErrorLog("UPDATERES_EXCEPTION", $"更新阻值数据失败: {ex.Message}", SN);
                }
            }));
        }

        /// <summary>
        /// 根据产品 SN 更新 DataModel.Recordmodel.ProductInfoRecords 中对应记录的压力测试数据。
        /// 业务含义：
        /// - 在电测压力监控完成后调用，将平均值、最大值、最小值及判定结果写入内存记录；
        /// - 这些压力数据会一并在 SaveBusBarData 报存到 MES，方便后续追溯。
        /// </summary>
        /// <param name="SN">产品序列号，用于在集合中定位记录</param>
        private void updatepressure(string SN, UInt16 Pressure_Average, UInt16 Pressure_Max, UInt16 Pressure_Min, bool Pressure_Result)
        {
            App.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    foreach (var p in DataModel.Recordmodel.ProductInfoRecords)
                    {
                        if (p.Productinfo.SN == SN)
                        {
                            p.Pressure_Average = Pressure_Average;
                            p.Pressure_Max = Pressure_Max;
                            p.Pressure_Min = Pressure_Min;
                            p.Pressure_Result = Pressure_Result;

                            // 不对同SN历史记录做“全量同步刷新”，仅更新第一条匹配记录（通常是列表中最新的一条）。
                            // 双测的另一条记录允许不包含压力信息，以避免点检SN/重复SN场景下污染历史显示。
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    sqlite.WriteErrorLog("UPDATEPRESSURE_EXCEPTION", $"更新压力数据失败: {ex.Message}", SN);
                }
            }));
        }


        public bool Download_PressureParameter()
        {
            try
            {
                var r1 = PLC_write(DataModel.Settingmodel.MaxPressure_Address.ToString(), DataModel.Processmodel.PressureParamter.Max_Pressure);
                var r2 = PLC_write(DataModel.Settingmodel.MinPressure_Address.ToString(), DataModel.Processmodel.PressureParamter.Min_Pressure);
                var r3 = PLC_write(DataModel.Settingmodel.Pressure_Address.ToString(), DataModel.Processmodel.PressureParamter.Pressure);
                return r1 & r2 & r3;
            }
            catch {; }
            return false;
        }

        /// <summary>
        /// 将测试模式写入PLC地址D1012
        /// </summary>
        /// <remarks>
        /// 测试模式值：
        /// - 0: 只测交流(ACW Only)
        /// - 1: 只测直流(DCW Only)
        /// - 2: 先交后直(ACW then DCW)
        /// - 3: 先直后交(DCW then ACW)
        /// </remarks>
        /// <returns>true: 写入成功; false: 写入失败</returns>
        public bool WriteTestModeToPLC()
        {
            try
            {
                ushort modeValue = (ushort)DataModel.Settingmodel.CurrentTestMode;
                string address = DataModel.Settingmodel.TestModeAddress.ToString();
                
                writeLog($"[测试模式] 写入PLC地址D{address}, 值={modeValue} ({DataModel.Settingmodel.CurrentTestMode})");
                
                bool result = PLC_write(address, modeValue);
                
                if (!result)
                {
                    writeLog($"[测试模式] ❌ 写入PLC失败! 地址=D{address}, 值={modeValue}", true);
                }
                
                return result;
            }
            catch (Exception ex)
            {
                writeLog($"[测试模式] ❌ 写入PLC异常: {ex.Message}", true);
                return false;
            }
        }

        /// <summary>
        /// 将指定测试模式写入PLC
        /// </summary>
        /// <param name="testMode">要写入的测试模式</param>
        /// <returns>true: 写入成功; false: 写入失败</returns>
        public bool WriteTestModeToPLC(AT9620.ElectricalTestMode testMode)
        {
            DataModel.Settingmodel.CurrentTestMode = testMode;
            return WriteTestModeToPLC();
        }



        /// <summary>
        /// 根据产品 SN 更新 DataModel.Recordmodel.ProductInfoRecords 中对应记录的 AOI 外观检测结果。
        /// 业务含义：
        /// - AOI 工具完成判定后调用，将外观 OK/NG 结果及时间写入内存记录；
        /// - 最终综合判定，会把 AppearanceInspection 作为外观工序的依据。
        /// </summary>
        /// <param name="SN">产品序列号，用于在集合中定位记录</param>
        private void updatetakephoto2(string SN, bool result, DateTime dt)
        {
            App.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    foreach (var p in DataModel.Recordmodel.ProductInfoRecords)
                    {
                        if (p.Productinfo.SN == SN)
                        {
                            p.AppearanceInspection = result;
                            p.DateTime = dt;

                            // 不对同SN历史记录做“全量同步刷新”，仅更新第一条匹配记录（通常是列表中最新的一条）。
                            // 双测的另一条记录允许不包含AOI信息，以避免点检SN/重复SN场景下污染历史显示。
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 同时写入UI日志与数据库错误日志，便于现场快速定位“内存更新失败/对象不存在/线程异常”等问题
                    writeLog($"[AOI] 更新内存外观结果失败：SN={SN}, 结果={(result ? "OK" : "NG")}, 异常={ex.Message}", true);
                    sqlite.WriteErrorLog("UPDATETAKEPHOTO2_EXCEPTION", $"更新AOI外观数据失败: {ex.Message}", SN);
                }
            }));
        }

        /// <summary>
        /// 检查所有AOI工具是否都为NG状态
        /// 用于AOI NG点检：只有所有工具都为NG才算点检通过
        /// </summary>
        /// <returns>true: 所有工具都为NG; false: 存在非NG工具</returns>
        private bool CheckAllAOIToolsNG()
        {
            try
            {
                if (DataModel.FaraVisionDataModel.Processmodel.Tools.Count == 0)
                {
                    writeLog("AOI_NG点检->无工具配置，返回false");
                    return false;
                }

                foreach (var tool in DataModel.FaraVisionDataModel.Processmodel.Tools)
                {
                    if (tool.ToolStatus != ToolStatus.NG)
                    {
                        writeLog($"AOI_NG点检->工具[{tool.Name}]状态为{tool.ToolStatus}，不是NG");
                        return false;
                    }
                }

                writeLog($"AOI_NG点检->所有{DataModel.FaraVisionDataModel.Processmodel.Tools.Count}个工具均为NG");
                return true;
            }
            catch (Exception ex)
            {
                writeLog($"AOI_NG点检->检查工具状态异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 向PLC写入AOI NG点检信号
        /// </summary>
        /// <param name="value">1: 点检通过; 0: 点检失败</param>
        private void WriteAOI_NG_InspectionSignal(UInt16 value)
        {
                try
                {
                    ModbusTcpNet modbusTcp = new ModbusTcpNet();
                    modbusTcp.ConnectTimeOut = 1;
                    modbusTcp.ReceiveTimeOut = 1;
                    modbusTcp.IpAddress = DataModel.Settingmodel.PLC_IP;
                    modbusTcp.Port = DataModel.Settingmodel.PLC_Port;
                    modbusTcp.DataFormat = HslCommunication.Core.DataFormat.CDAB;
                    
                    var connectresult = modbusTcp.ConnectServer();
                    if (connectresult.IsSuccess)
                    {
                        // M寄存器是线圈（Coil），使用WriteCoil方法写入Bool值
                        bool boolValue = (value == 1);
                        var writeResult = modbusTcp.WriteCoil(DataModel.Settingmodel.AOI_NG_InspectionAddress.ToString(), boolValue);
                        modbusTcp.ConnectClose();
                        
                        if (writeResult.IsSuccess)
                        {
                            writeLog($"AOI_NG点检->向PLC地址M{DataModel.Settingmodel.AOI_NG_InspectionAddress}写入{value}成功");
                        }
                        else
                        {
                                writeLog($"AOI_NG点检->向PLC地址M{DataModel.Settingmodel.AOI_NG_InspectionAddress}写入{value}失败: {writeResult.Message}");
                            }
                        }
                    else
                    {
                            writeLog($"AOI_NG点检->PLC连接失败: {connectresult.Message}");
                        }
                    }
                catch (Exception ex)
                {
                        writeLog($"AOI_NG点检->写入PLC信号异常: {ex.Message}");
                    }
                }


        #endregion
        #region 照片存储
        public void SaveImage(HObject Image, string sn, string type, int index, string Result)
        {
            string savefilename = $"{DataModel.Settingmodel.ImageSaveSetting.ImageSaveDir}\\{DateTime.Now.ToString("yyyyMMdd")}\\{type}\\{Result}\\{sn}-{index.ToString("00")}-{DateTime.Now.ToString("yyyyMMddHHmmssFFF")}.jpg";
            string dir = Path.GetDirectoryName(savefilename);
            if (!Directory.Exists(dir))
            { Directory.CreateDirectory(dir); }
            HOperatorSet.WriteImage(Image, "jpg", 0, savefilename);
        }
        #endregion

        #region 性能日志
        /// <summary>
        /// 统一的性能诊断日志方法
        /// 写入性能诊断日志到独立文件,不影响现有业务日志
        /// 超过1秒的耗时会用特殊格式突出显示
        /// </summary>
        /// <param name="component">组件标识，如BUSINESS、UI等</param>
        /// <param name="tag">日志标签,如 SCAN_SUCCESS、PROCESS_COMPLETE等</param>
        /// <param name="message">日志消息</param>
        /// <param name="elapsedMs">耗时(毫秒),可选</param>
        /// <param name="extraInfo">额外信息,可选</param>
        private void writePerfLog(string component, string tag, string message, long? elapsedMs = null, string extraInfo = null)
        {
            try
            {
                // 获取线程ID
                int threadId = Thread.CurrentThread.ManagedThreadId;
                string threadName = Thread.CurrentThread.Name ?? (threadId == 1 ? "UI-Thread" : $"Thread-{threadId}");

                // 获取设备ID(从配置中读取)
                string deviceId = DataModel.Settingmodel.SETTING_DATA?.MachineID ?? "Unknown";

                // 检测是否为异常耗时（超过1秒）
                bool isAbnormalTime = elapsedMs.HasValue && elapsedMs.Value > 1000;
                string perfLevel = isAbnormalTime ? "PERF-异常" : "PERF";

                // 构建日志内容
                StringBuilder logBuilder = new StringBuilder();
                logBuilder.Append($"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}]");
                logBuilder.Append($"[{threadName}]");
                logBuilder.Append($"[{component}_{tag}]");
                logBuilder.Append($"[{perfLevel}]");
                logBuilder.Append($"[Device-{deviceId}] {message}");

                if (elapsedMs.HasValue)
                {
                    string timeDisplay = isAbnormalTime ?
                        $"耗时={elapsedMs.Value}ms[异常!!!]" :
                        $"耗时={elapsedMs.Value}ms";
                    logBuilder.Append($" | {timeDisplay}");
                }

                if (!string.IsNullOrEmpty(extraInfo))
                {
                    logBuilder.Append($" | {extraInfo}");
                }

                string logContent = logBuilder.ToString();

                // 写入独立的性能日志文件
                string filename = $"{Environment.CurrentDirectory}\\日志\\性能诊断\\{DateTime.Now.ToString("yyyyMMdd")}_performance.log";
                string dir = Path.GetDirectoryName(filename);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // 使用文件锁确保多线程安全
                lock (writeLog_Locker)
                {
                    using (StreamWriter sw = new StreamWriter(filename, true, Encoding.UTF8))
                    {
                        sw.WriteLine(logContent);
                        sw.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                // 性能日志失败不应影响业务,仅记录到错误日志
                try
                {
                    writeError($"性能日志写入失败: {ex.Message}");
                }
                catch { }
            }
        }
        #endregion
    }
}
