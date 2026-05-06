/*
 * 工单信息输入窗口
 *
 * 生产任务管理的入口界面：
 * 1. 工单号输入：关联MES系统的生产任务
 * 2. 物料信息录入：产品型号、规格等基本信息
 * 3. 生产参数设置：根据工单要求调整测试标准
 * 4. 任务初始化：为新的生产批次准备系统状态
 *
 * 作用：建立生产任务与系统测试流程的关联，确保生产数据的准确追溯
 */

using AT9620;
using AT6835FL;
using BusbarCompressionSystem.Model;
using BusbarCompressionSystem.ViewModel;
using HalconDotNet;
using Panuon.WPF.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace BusbarCompressionSystem
{
    /// <summary>
    /// WOCODEINPUTFORM.xaml 的交互逻辑
    /// </summary>
    public partial class WOCODEINPUTFORM : WindowX
    {
        ViewModelLocator vml { get; set; }
        public WOCODEINPUTFORM()
        {
            InitializeComponent();
            vml = (ViewModelLocator)this.FindResource("Locator");
        }

        /// <summary>
        /// 手动下发 IR 参数：用于现场快速验证 IR 串口链路与指令集（不依赖 PLC M3033）。
        /// </summary>
        private void Button_Click_IrDownload(object sender, RoutedEventArgs e)
        {
            try
            {
                vml.Main.writeLog("[IR] 手动下发参数");
                vml.Main.writeLog($"[IR] 参数: 电压={vml.Main.DataModel.Processmodel.IRParameter.Voltage}V, 时间={vml.Main.DataModel.Processmodel.IRParameter.TestTime}s, " +
                    $"下限={vml.Main.DataModel.Processmodel.IRParameter.ResLow}, 上限={vml.Main.DataModel.Processmodel.IRParameter.ResHigh}, " +
                    $"电压下限={vml.Main.DataModel.Processmodel.IRParameter.VoltageLow}V, 电压上限={vml.Main.DataModel.Processmodel.IRParameter.VoltageHigh}V, " +
                    $"(下发阈值={vml.Main.DataModel.Processmodel.IRParameter.CompRes})");

                // 同步唯一开关：是否生成详细调试日志文件（其余策略固定为：下发自检 + 写前清缓冲）
                vml.Main.SyncIrAt6835RuntimeFlagsFromSettings("[IR手动下发]");
                vml.Main.DataModel.Settingmodel.AT6835FL_1.IRParameter = vml.Main.DataModel.Processmodel.IRParameter;

                int oldTimeout = vml.Main.DataModel.Settingmodel.AT6835FL_1.ReceiveTimeoutMs;
                vml.Main.DataModel.Settingmodel.AT6835FL_1.ReceiveTimeoutMs = Math.Max(oldTimeout, 8000);
                var r = vml.Main.DataModel.Settingmodel.AT6835FL_1.Download();
                vml.Main.DataModel.Settingmodel.AT6835FL_1.ReceiveTimeoutMs = oldTimeout;

                if (r.Success)
                {
                    NoticeBox.Show("IR参数手动下发成功", "成功", MessageBoxIcon.Success, true, 5000);
                }
                else
                {
                    NoticeBox.Show($"IR参数手动下发失败: {r.Error}", "错误", MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                NoticeBox.Show($"IR参数手动下发异常: {ex.Message}", "错误", MessageBoxIcon.Error);
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {


            try
            {
                string partnoid = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.Get_PARTNOID_BY_WOCODE(vml.Main.DataModel.Processmodel.wocodeinputstr);
                if (string.IsNullOrEmpty(partnoid))
                {
                    MessageBoxX.Show("未获取到规格信息，请检查批号是否正确");
                    return;
                }

                var ps = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.Get_Parameters(
                    vml.Main.DataModel.Settingmodel.SETTING_DATA.MachineID,
                    vml.Main.DataModel.Processmodel.wocodeinputstr,
                    vml.Main.DataModel.Settingmodel.SETTING_DATA.StandardCode
                    );

                // 【日志】打印MES返回的完整原始参数，便于调试和问题追溯
                try
                {
                    vml.Main.writeLog($"========== MES参数获取 ==========");
                    vml.Main.writeLog($"批号: {vml.Main.DataModel.Processmodel.wocodeinputstr}");
                    vml.Main.writeLog($"规格: {partnoid}");
                    //vml.Main.writeLog($"设备ID: {vml.Main.DataModel.Settingmodel.SETTING_DATA.MachineID}");
                    //vml.Main.writeLog($"标准代码: {vml.Main.DataModel.Settingmodel.SETTING_DATA.StandardCode}");
                    vml.Main.writeLog($"参数数量: {ps?.Count ?? 0}");
                    if (ps != null && ps.Count > 0)
                    {
                        string jsonParams = Newtonsoft.Json.JsonConvert.SerializeObject(ps, Newtonsoft.Json.Formatting.Indented);
                        vml.Main.writeLog($"原始参数JSON:\r\n{jsonParams}",false);
                    }
                    vml.Main.writeLog($"==================================");
                }
                catch (Exception logEx)
                {
                    vml.Main.writeLog($"[MES参数日志] 序列化异常: {logEx.Message}");
                }

                    // ACW交流参数
                var r1 = ps.Where(p => p.ParameterName == "测试电压");
                var r2 = ps.Where(p => p.ParameterName == "测试模式");
                var r3 = ps.Where(p => p.ParameterName == "上升时间");
                var r4 = ps.Where(p => p.ParameterName == "测试时间");
                var r5 = ps.Where(p => p.ParameterName == "下降时间");
                var r6 = ps.Where(p => p.ParameterName == "测试电流");
                var r7 = ps.Where(p => p.ParameterName == "充电电流下限");
                var r8 = ps.Where(p => p.ParameterName == "测试频率");
                var r9 = ps.Where(p => p.ParameterName == "极壳压力上限");
                var r10 = ps.Where(p => p.ParameterName == "极壳压力下限");
                var r11 = ps.Where(p => p.ParameterName == "极壳压力");
                
                // DCW直流参数
                var dcw1 = ps.Where(p => p.ParameterName == "直流测试电压");
                var dcw2 = ps.Where(p => p.ParameterName == "直流上升时间");
                var dcw3 = ps.Where(p => p.ParameterName == "直流测试时间");
                var dcw4 = ps.Where(p => p.ParameterName == "直流下降时间");
                var dcw5 = ps.Where(p => p.ParameterName == "直流测试电流");
                var dcw6 = ps.Where(p => p.ParameterName == "直流充电电流下限");
                var dcw7 = ps.Where(p => p.ParameterName == "直流极壳压力");
                var dcw8 = ps.Where(p => p.ParameterName == "直流极壳压力上限");
                var dcw9 = ps.Where(p => p.ParameterName == "直流极壳压力下限");

                // IR绝缘电阻参数（按 20260414 实际 MES 字段）
                var irVolt = ps.Where(p => p.ParameterName == "IR测试电压");
                var irTime = ps.Where(p => p.ParameterName == "IR测试时间");
                var irResLow = ps.Where(p => p.ParameterName == "IR下限");
                var irResHigh = ps.Where(p => p.ParameterName == "IR上限");
                var irVoltLow = ps.Where(p => p.ParameterName == "IR测试电压下限");
                var irVoltHigh = ps.Where(p => p.ParameterName == "IR测试电压上限");

                // 首先检查测试模式参数是否存在
                if (r2.Count() == 0)
                {
                    MessageBoxX.Show("缺少测试模式参数，无法继续", MessageBoxIcon.Error);
                    return;
                }

                // 解析测试模式，决定需要检查哪些参数
                int testModeValue = Convert.ToInt16(r2.First().TargetValue);
                bool needACW = (testModeValue == 0 || testModeValue == 2 || testModeValue == 3); // 只测交流、先交后直、先直后交
                bool needDCW = (testModeValue == 1 || testModeValue == 2 || testModeValue == 3); // 只测直流、先交后直、先直后交

                string error = "缺少以下工艺参数:\r\n";
                bool r = true;
                
                // 根据测试模式检查ACW交流参数
                if (needACW)
                {
                    if (r1.Count() == 0)
                    {
                        error += "测试电压\r\n";
                        r = false;
                    }
                    if (r3.Count() == 0)
                    {
                        error += "上升时间\r\n";
                        r = false;
                    }
                    if (r4.Count() == 0)
                    {
                        error += "测试时间\r\n";
                        r = false;
                    }
                    if (r5.Count() == 0)
                    {
                        error += "下降时间\r\n";
                        r = false;
                    }
                    if (r6.Count() == 0)
                    {
                        error += "测试电流\r\n";
                        r = false;
                    }
                    if (r7.Count() == 0)
                    {
                        error += "充电电流下限\r\n";
                        r = false;
                    }
                    if (r8.Count() == 0)
                    {
                        error += "测试频率\r\n";
                        r = false;
                    }
                    if (r9.Count() == 0)
                    {
                        error += "极壳压力上限\r\n";
                        r = false;
                    }
                    if (r10.Count() == 0)
                    {
                        error += "极壳压力下限\r\n";
                        r = false;
                    }
                    if (r11.Count() == 0)
                    {
                        error += "极壳压力\r\n";
                        r = false;
                    }
                }
                
                // 根据测试模式检查DCW直流参数
                if (needDCW)
                {
                    if (dcw1.Count() == 0)
                    {
                        error += "直流测试电压\r\n";
                        r = false;
                    }
                    if (dcw2.Count() == 0)
                    {
                        error += "直流上升时间\r\n";
                        r = false;
                    }
                    if (dcw3.Count() == 0)
                    {
                        error += "直流测试时间\r\n";
                        r = false;
                    }
                    if (dcw4.Count() == 0)
                    {
                        error += "直流下降时间\r\n";
                        r = false;
                    }
                    if (dcw5.Count() == 0)
                    {
                        error += "直流测试电流\r\n";
                        r = false;
                    }
                    if (dcw6.Count() == 0)
                    {
                        error += "直流充电电流下限\r\n";
                        r = false;
                    }
                    if (dcw7.Count() == 0)
                    {
                        error += "直流极壳压力\r\n";
                        r = false;
                    }
                    if (dcw8.Count() == 0)
                    {
                        error += "直流极壳压力上限\r\n";
                        r = false;
                    }
                    if (dcw9.Count() == 0)
                    {
                        error += "直流极壳压力下限\r\n";
                        r = false;
                    }
                }

                if (r)
                {
                    // 使用前面已声明的 testModeValue
                    AT9620.ElectricalTestMode electricalTestMode = (AT9620.ElectricalTestMode)testModeValue;
                    
                    // 写入测试模式到PLC地址D1012
                    bool writeModeResult = vml.Main.WriteTestModeToPLC(electricalTestMode);
                    if (writeModeResult)
                    {
                        vml.Main.writeLog($"[测试模式参数] 成功写入PLC: {GetTestModeDisplayName(electricalTestMode)} (值={testModeValue})");
                    }
                    else
                    {
                        vml.Main.writeLog($"[测试模式参数] ⚠️ 写入PLC失败: {GetTestModeDisplayName(electricalTestMode)} (值={testModeValue})", true);
                    }

                    // 解析ACW交流参数（仅当测试模式需要交流时）
                    if (needACW)
                    {
                        vml.Main.DataModel.Processmodel.ACWParameter.Voltage = Convert.ToSingle(r1.First().TargetValue);
                        vml.Main.DataModel.Processmodel.ACWParameter.TestMode = TestMode.ACW;
                        vml.Main.DataModel.Processmodel.ACWParameter.RiseTime = Convert.ToSingle(r3.First().TargetValue);
                        vml.Main.DataModel.Processmodel.ACWParameter.TestTime = Convert.ToSingle(r4.First().TargetValue);
                        vml.Main.DataModel.Processmodel.ACWParameter.FallTime = Convert.ToSingle(r5.First().TargetValue);
                        vml.Main.DataModel.Processmodel.ACWParameter.High = Convert.ToSingle(r6.First().TargetValue);
                        vml.Main.DataModel.Processmodel.ACWParameter.Low = Convert.ToSingle(r7.First().TargetValue);
                        vml.Main.DataModel.Processmodel.ACWParameter.Freq = Convert.ToSingle(r8.First().TargetValue);
                        vml.Main.DataModel.Processmodel.ACWParameter.Arc = 0; // 使用默认值
                    }
                
                    // 解析DCW直流参数（仅当测试模式需要直流时）
                    if (needDCW)
                    {
                        vml.Main.DataModel.Processmodel.DCWParameter.Voltage = Convert.ToSingle(dcw1.First().TargetValue);
                        vml.Main.DataModel.Processmodel.DCWParameter.TestMode = TestMode.DCW;
                        vml.Main.DataModel.Processmodel.DCWParameter.RiseTime = Convert.ToSingle(dcw2.First().TargetValue);
                        vml.Main.DataModel.Processmodel.DCWParameter.TestTime = Convert.ToSingle(dcw3.First().TargetValue);
                        vml.Main.DataModel.Processmodel.DCWParameter.FallTime = Convert.ToSingle(dcw4.First().TargetValue);
                        vml.Main.DataModel.Processmodel.DCWParameter.High = Convert.ToSingle(dcw5.First().TargetValue);
                        vml.Main.DataModel.Processmodel.DCWParameter.Low = Convert.ToSingle(dcw6.First().TargetValue);
                        vml.Main.DataModel.Processmodel.DCWParameter.Freq = 0; // DCW不需要频率
                        vml.Main.DataModel.Processmodel.DCWParameter.Arc = 0; // MES未提供，使用默认值
                    }

                    // 兼容旧代码：同步设置TVParameter（根据当前需要的参数来源）
                    if (needACW)
                    {
                        vml.Main.DataModel.Processmodel.TVParameter.Voltage = vml.Main.DataModel.Processmodel.ACWParameter.Voltage;
                        vml.Main.DataModel.Processmodel.TVParameter.TestMode = TestMode.ACW;
                        vml.Main.DataModel.Processmodel.TVParameter.RiseTime = vml.Main.DataModel.Processmodel.ACWParameter.RiseTime;
                        vml.Main.DataModel.Processmodel.TVParameter.TestTime = vml.Main.DataModel.Processmodel.ACWParameter.TestTime;
                        vml.Main.DataModel.Processmodel.TVParameter.FallTime = vml.Main.DataModel.Processmodel.ACWParameter.FallTime;
                        vml.Main.DataModel.Processmodel.TVParameter.High = vml.Main.DataModel.Processmodel.ACWParameter.High;
                        vml.Main.DataModel.Processmodel.TVParameter.Low = vml.Main.DataModel.Processmodel.ACWParameter.Low;
                        vml.Main.DataModel.Processmodel.TVParameter.Freq = vml.Main.DataModel.Processmodel.ACWParameter.Freq;
                        vml.Main.DataModel.Processmodel.TVParameter.Arc = vml.Main.DataModel.Processmodel.ACWParameter.Arc;
                    }
                    else if (needDCW)
                    {
                        vml.Main.DataModel.Processmodel.TVParameter.Voltage = vml.Main.DataModel.Processmodel.DCWParameter.Voltage;
                        vml.Main.DataModel.Processmodel.TVParameter.TestMode = TestMode.DCW;
                        vml.Main.DataModel.Processmodel.TVParameter.RiseTime = vml.Main.DataModel.Processmodel.DCWParameter.RiseTime;
                        vml.Main.DataModel.Processmodel.TVParameter.TestTime = vml.Main.DataModel.Processmodel.DCWParameter.TestTime;
                        vml.Main.DataModel.Processmodel.TVParameter.FallTime = vml.Main.DataModel.Processmodel.DCWParameter.FallTime;
                        vml.Main.DataModel.Processmodel.TVParameter.High = vml.Main.DataModel.Processmodel.DCWParameter.High;
                        vml.Main.DataModel.Processmodel.TVParameter.Low = vml.Main.DataModel.Processmodel.DCWParameter.Low;
                        vml.Main.DataModel.Processmodel.TVParameter.Freq = vml.Main.DataModel.Processmodel.DCWParameter.Freq;
                        vml.Main.DataModel.Processmodel.TVParameter.Arc = vml.Main.DataModel.Processmodel.DCWParameter.Arc;
                    }

                    // 解析IR参数（仅当MES返回了IR相关参数时覆盖默认值）
                    if (irVolt.Any())
                        vml.Main.DataModel.Processmodel.IRParameter.Voltage = Convert.ToInt32(irVolt.First().TargetValue);
                    if (irTime.Any())
                        vml.Main.DataModel.Processmodel.IRParameter.TestTime = Convert.ToInt32(irTime.First().TargetValue);

                    // 备注：当前 AT6835FL 下发只使用“电压/时间/电阻阈值(CompRes)”三类。
                    // MES给了上下限/电压上下限，先保存到参数对象中供日志/后续扩展使用。
                    if (irResLow.Any())
                    {
                        vml.Main.DataModel.Processmodel.IRParameter.ResLow = irResLow.First().TargetValue;
                        vml.Main.DataModel.Processmodel.IRParameter.CompRes = vml.Main.DataModel.Processmodel.IRParameter.ResLow;
                    }
                    if (irResHigh.Any())
                        vml.Main.DataModel.Processmodel.IRParameter.ResHigh = irResHigh.First().TargetValue;
                    if (irVoltLow.Any())
                        vml.Main.DataModel.Processmodel.IRParameter.VoltageLow = Convert.ToInt32(irVoltLow.First().TargetValue);
                    if (irVoltHigh.Any())
                        vml.Main.DataModel.Processmodel.IRParameter.VoltageHigh = Convert.ToInt32(irVoltHigh.First().TargetValue);

                    // 根据测试模式选择压力参数（交流或直流）
                    // 测试模式: 0=只测交流, 1=只测直流, 2=先交后直, 3=先直后交
                    if (testModeValue == 0 || testModeValue == 2) // 只测交流 或 先交后直 → 使用交流压力参数
                    {
                        vml.Main.DataModel.Processmodel.PressureParamter.Pressure = Convert.ToUInt16(r11.First().TargetValue);
                        vml.Main.DataModel.Processmodel.PressureParamter.Max_Pressure = Convert.ToSingle(r9.First().TargetValue);
                        vml.Main.DataModel.Processmodel.PressureParamter.Min_Pressure = Convert.ToSingle(r10.First().TargetValue);
                        vml.Main.writeLog($"[压力参数] 使用交流压力参数: 压力={r11.First().TargetValue}, 上限={r9.First().TargetValue}, 下限={r10.First().TargetValue}");
                    }
                    else if (testModeValue == 1 || testModeValue == 3) // 只测直流 或 先直后交 → 使用直流压力参数
                    {
                        vml.Main.DataModel.Processmodel.PressureParamter.Pressure = Convert.ToUInt16(dcw7.First().TargetValue);
                        vml.Main.DataModel.Processmodel.PressureParamter.Max_Pressure = Convert.ToSingle(dcw8.First().TargetValue);
                        vml.Main.DataModel.Processmodel.PressureParamter.Min_Pressure = Convert.ToSingle(dcw9.First().TargetValue);
                        vml.Main.writeLog($"[压力参数] 使用直流压力参数: 压力={dcw7.First().TargetValue}, 上限={dcw8.First().TargetValue}, 下限={dcw9.First().TargetValue}");
                    }

                    // IR绝缘电阻仪参数下发
                    AT6835FL.Result rdIR;
                    vml.Main.writeLog($"[IR] 可用判定: IRAvailable={vml.Main.DataModel.Processmodel.TVAvailable.IRAvailable}, " +
                        $"M{vml.Main.DataModel.Settingmodel.IRMeterAvailableAddress}(Raw)={vml.Main.DataModel.Processmodel.TVAvailable.IRAvailableRawCoil}");
                    vml.Main.writeLog($"[IR] 现场信号约定: M{vml.Main.DataModel.Settingmodel.IRMeterAvailableAddress}=1 表示开启/可用（IR 与耐压可用逻辑相反）");
                    if (vml.Main.DataModel.Processmodel.TVAvailable.IRAvailable)
                    {
                        vml.Main.writeLog($"[IR] 开始下发参数: 电压={vml.Main.DataModel.Processmodel.IRParameter.Voltage}V, " +
                            $"时间={vml.Main.DataModel.Processmodel.IRParameter.TestTime}s, " +
                            $"下限={vml.Main.DataModel.Processmodel.IRParameter.ResLow}, 上限={vml.Main.DataModel.Processmodel.IRParameter.ResHigh}, " +
                            $"电压下限={vml.Main.DataModel.Processmodel.IRParameter.VoltageLow}V, 电压上限={vml.Main.DataModel.Processmodel.IRParameter.VoltageHigh}V, " +
                            $"(下发阈值={vml.Main.DataModel.Processmodel.IRParameter.CompRes})");

                        vml.Main.SyncIrAt6835RuntimeFlagsFromSettings("[MES参数下发]");
                        vml.Main.DataModel.Settingmodel.AT6835FL_1.IRParameter = vml.Main.DataModel.Processmodel.IRParameter;
                        rdIR = vml.Main.DataModel.Settingmodel.AT6835FL_1.Download();

                        if (rdIR.Success)
                            vml.Main.writeLog($"[IR] 参数下发成功");
                        else
                            vml.Main.writeLog($"[IR] 参数下发失败: {rdIR.Error}");
                    }
                    else
                    {
                        vml.Main.writeLog($"[IR] 绝缘电阻仪不可用，跳过参数下发（依据: IRAvailable=false）");
                        rdIR = new AT6835FL.Result() { Success = true };
                    }

                    // 根据测试模式提前下发参数，避免等到触发时才下发
                    // 测试模式: 0=只测交流, 1=只测直流, 2=先交后直, 3=先直后交
                    vml.Main.writeLog($"========== 参数下发 ==========");
                    vml.Main.writeLog($"测试模式: {GetTestModeDisplayName(electricalTestMode)} (值={testModeValue})");
                    
                    AT9620.Result rd1 = new AT9620.Result() { Success = true };
                    AT9620.Result rd2 = new AT9620.Result() { Success = true };
                    AT9620.Result rd3 = new AT9620.Result() { Success = true };
                    
                    // 根据测试模式决定首次下发哪种参数
                    if (testModeValue == 0 || testModeValue == 2) // 只测交流 或 先交后直
                    {
                        vml.Main.writeLog($"[首次下发] 测试模式需要ACW，下发ACW参数到设备");
                        vml.Main.writeLog($"ACW参数: 电压={vml.Main.DataModel.Processmodel.ACWParameter.Voltage}V, " +
                            $"测试时间={vml.Main.DataModel.Processmodel.ACWParameter.TestTime}s, " +
                            $"上升时间={vml.Main.DataModel.Processmodel.ACWParameter.RiseTime}s, " +
                            $"下降时间={vml.Main.DataModel.Processmodel.ACWParameter.FallTime}s, " +
                            $"电流上限={vml.Main.DataModel.Processmodel.ACWParameter.High}mA, " +
                            $"电流下限={vml.Main.DataModel.Processmodel.ACWParameter.Low}mA, " +
                            $"频率={vml.Main.DataModel.Processmodel.ACWParameter.Freq}Hz");
                        
                        // 设置ACW参数并下发
                        vml.Main.DataModel.Settingmodel.AT9620_1.TVParameter = vml.Main.DataModel.Processmodel.ACWParameter;
                        vml.Main.DataModel.Settingmodel.AT9620_2.TVParameter = vml.Main.DataModel.Processmodel.ACWParameter;
                        vml.Main.DataModel.Settingmodel.AT9620_3.TVParameter = vml.Main.DataModel.Processmodel.ACWParameter;
                        
                        // 记录当前模式为ACW
                        vml.Main.DataModel.Processmodel.LastTV1TestMode = AT9620.TestMode.ACW;
                        vml.Main.DataModel.Processmodel.LastTV2TestMode = AT9620.TestMode.ACW;
                        
                        vml.Main.writeLog($"[耐压1] 开始下发ACW参数...",false);
                        rd1 = vml.Main.DataModel.Settingmodel.AT9620_1.Download();
                        if (rd1.Success)
                        {
                            vml.Main.writeLog($"[耐压1] ✓ ACW参数下发成功", false);
                        }
                        else
                        {
                            vml.Main.writeLog($"[耐压1] ❌ ACW参数下发失败: {rd1.Error}", false);
                        }
                        
                        vml.Main.writeLog($"[耐压2] 开始下发ACW参数...", false);
                        rd2 = vml.Main.DataModel.Settingmodel.AT9620_2.Download();
                        if (rd2.Success)
                        {
                            vml.Main.writeLog($"[耐压2] ✓ ACW参数下发成功", false);
                        }
                        else
                        {
                            vml.Main.writeLog($"[耐压2] ❌ ACW参数下发失败: {rd2.Error}", false);
                        }
                        
                        // 【优化】第三个仪器已禁用，跳过参数下发
                        vml.Main.writeLog($"[耐压3] 已禁用，跳过参数下发",false);
                        rd3 = new AT9620.Result() { Success = true }; // 模拟成功，避免影响整体流程
                    }
                    else if (testModeValue == 1 || testModeValue == 3) // 只测直流 或 先直后交
                    {
                        vml.Main.writeLog($"[首次下发] 测试模式需要DCW，下发DCW参数到设备");
                        vml.Main.writeLog($"DCW参数: 电压={vml.Main.DataModel.Processmodel.DCWParameter.Voltage}V, " +
                            $"测试时间={vml.Main.DataModel.Processmodel.DCWParameter.TestTime}s, " +
                            $"上升时间={vml.Main.DataModel.Processmodel.DCWParameter.RiseTime}s, " +
                            $"下降时间={vml.Main.DataModel.Processmodel.DCWParameter.FallTime}s, " +
                            $"电流上限={vml.Main.DataModel.Processmodel.DCWParameter.High}mA, " +
                            $"电流下限={vml.Main.DataModel.Processmodel.DCWParameter.Low}mA");
                        
                        // 设置DCW参数并下发
                        vml.Main.DataModel.Settingmodel.AT9620_1.TVParameter = vml.Main.DataModel.Processmodel.DCWParameter;
                        vml.Main.DataModel.Settingmodel.AT9620_2.TVParameter = vml.Main.DataModel.Processmodel.DCWParameter;
                        vml.Main.DataModel.Settingmodel.AT9620_3.TVParameter = vml.Main.DataModel.Processmodel.DCWParameter;
                        
                        // 记录当前模式为DCW
                        vml.Main.DataModel.Processmodel.LastTV1TestMode = AT9620.TestMode.DCW;
                        vml.Main.DataModel.Processmodel.LastTV2TestMode = AT9620.TestMode.DCW;
                        
                        vml.Main.writeLog($"[耐压1] 开始下发DCW参数...", false);
                        rd1 = vml.Main.DataModel.Settingmodel.AT9620_1.Download();
                        if (rd1.Success)
                        {
                            vml.Main.writeLog($"[耐压1] ✓ DCW参数下发成功", false);
                        }
                        else
                        {
                            vml.Main.writeLog($"[耐压1] ❌ DCW参数下发失败: {rd1.Error}", false);
                        }
                        
                        vml.Main.writeLog($"[耐压2] 开始下发DCW参数...", false);
                        rd2 = vml.Main.DataModel.Settingmodel.AT9620_2.Download();
                        if (rd2.Success)
                        {
                            vml.Main.writeLog($"[耐压2] ✓ DCW参数下发成功", false);
                        }
                        else
                        {
                            vml.Main.writeLog($"[耐压2] ❌ DCW参数下发失败: {rd2.Error}", false);
                        }
                        
                        // 【优化】第三个仪器已禁用，跳过参数下发
                        vml.Main.writeLog($"[耐压3] 已禁用，跳过参数下发");
                        rd3 = new AT9620.Result() { Success = true }; // 模拟成功，避免影响整体流程
                    }
                    
                    vml.Main.writeLog($"==============================");

                    var rd4 = vml.Main.Download_PressureParameter();

                    if ((!rd1.Success) && vml.Main.DataModel.Processmodel.TVAvailable.TV1Available)
                    {
                        NoticeBox.Show("耐压工位1参数下发失败", "错误", MessageBoxIcon.Error);
                    }
                    else if ((!rd2.Success) && vml.Main.DataModel.Processmodel.TVAvailable.TV2Available)
                    {
                        NoticeBox.Show("耐压工位2参数下发失败", "错误", MessageBoxIcon.Error);
                    }
                    else if ((!rd3.Success) && vml.Main.DataModel.Processmodel.TVAvailable.TV3Available)
                    {
                        NoticeBox.Show("耐压工位3参数下发失败", "错误", MessageBoxIcon.Error);
                    }
                    else if ((!rdIR.Success) && vml.Main.DataModel.Processmodel.TVAvailable.IRAvailable)
                    {
                        NoticeBox.Show("绝缘电阻仪参数下发失败", "错误", MessageBoxIcon.Error);
                    }
                    else if (!rd4)
                    {
                        NoticeBox.Show("压力参数下发失败", "错误", MessageBoxIcon.Error);
                    }
                    else
                    {
                        NoticeBox.Show("参数下发完成", "成功", MessageBoxIcon.Success, true, 5000);

                        // 参数下发成功后读取阻值上限阈值（D2020=Res_Max_Address），缓存到内存供后续 RES OK/NG 判定使用
                        float resMaxThreshold = vml.Main.DataModel.Processmodel.ResParameter.Max_Res;
                        float originalResMaxThreshold = resMaxThreshold;
                        string resMaxSource = "内存默认";
                        try
                        {
                            float fromFloat = vml.Main.PLC_ReadFloat(vml.Main.DataModel.Settingmodel.Res_Max_Address);
                            // 简单范围校验，避免浮点读取地址类型不匹配导致的异常值
                            if (fromFloat > 0 && fromFloat < 1000)
                            {
                                resMaxThreshold = fromFloat;
                                resMaxSource = "D2020(float)";
                            }
                            else
                            {
                                throw new Exception("PLC_ReadFloat范围不合理");
                            }
                        }
                        catch (Exception exFloat)
                        {
                            try
                            {
                                UInt16 fromU16 = vml.Main.PLC_ReadUint16(vml.Main.DataModel.Settingmodel.Res_Max_Address);
                                // 兼容“14”和“14*1000”两种可能的存储方式（例如 14000 => 14.0）
                                if (fromU16 > 0 && fromU16 < 1000)
                                {
                                    resMaxThreshold = fromU16;
                                    resMaxSource = "D2020(uint16)";
                                }
                                else if (fromU16 >= 1000)
                                {
                                    resMaxThreshold = fromU16 / 1000f;
                                    resMaxSource = "D2020(uint16/scale)";
                                }
                                else
                                {
                                    throw new Exception("PLC_ReadUint16范围不合理");
                                }
                            }
                            catch (Exception exU16)
                            {
                                // 读取失败时保持当前内存默认值，避免阻值判定被中断
                                vml.Main.writeLog(
                                    $"[阻值阈值] 读取PLC D{vml.Main.DataModel.Settingmodel.Res_Max_Address}失败( Float:{exFloat.Message}; U16:{exU16.Message} )，使用旧值 Max_Res={originalResMaxThreshold}",
                                    true);
                            }
                        }

                        vml.Main.DataModel.Processmodel.ResParameter.Max_Res = resMaxThreshold;
                        vml.Main.writeLog($"[阻值阈值] 读取PLC D{vml.Main.DataModel.Settingmodel.Res_Max_Address} -> Max_Res={resMaxThreshold} ({resMaxSource})");
                        vml.Main.DataModel.Processmodel.PartNOID = partnoid;
                    }
                }
                else
                {
                    MessageBoxX.Show(error, MessageBoxIcon.Error);

                }




            }
            catch (Exception ex)
            {

                MessageBoxX.Show(ex.ToString(), "错误", MessageBoxIcon.Error);
            }

        }

        /// <summary>
        /// 加载手动参数按钮点击事件
        /// </summary>
        private void Button_Click_Manual(object sender, RoutedEventArgs e)
        {
            try
            {
                // 确保配置目录存在
                string configDir = $"{Environment.CurrentDirectory}\\配置";
                if (!System.IO.Directory.Exists(configDir))
                {
                    System.IO.Directory.CreateDirectory(configDir);
                }

                // 创建示例配置文件（如果不存在）
                string exampleConfigPath = $"{configDir}\\手动下发工艺参数配置（弃用）.json";
                if (!System.IO.File.Exists(exampleConfigPath))
                {
                    CreateExampleConfigFile(exampleConfigPath);
                }

                // 打开文件选择对话框
                Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
                dlg.DefaultExt = ".json";
                dlg.Filter = "JSON配置文件 (*.json)|*.json";
                dlg.InitialDirectory = configDir;

                bool? result = dlg.ShowDialog();
                if (result == true)
                {
                    // 读取JSON文件
                    string jsonContent = System.IO.File.ReadAllText(dlg.FileName);

                    // 反序列化
                    var config = Newtonsoft.Json.JsonConvert.DeserializeObject<BusbarCompressionSystem.Model.ManualParameterConfig>(jsonContent);

                    if (config == null)
                    {
                        MessageBoxX.Show("配置文件格式错误", MessageBoxIcon.Error);
                        return;
                    }

                    // 应用参数到系统 - 赋值到 Processmodel.TVParameter
                    vml.Main.DataModel.Processmodel.TVParameter.TestMode = (TestMode)config.TVParameter.TestMode;
                    vml.Main.DataModel.Processmodel.TVParameter.Voltage = config.TVParameter.Voltage;
                    vml.Main.DataModel.Processmodel.TVParameter.TestTime = config.TVParameter.TestTime;
                    vml.Main.DataModel.Processmodel.TVParameter.RiseTime = config.TVParameter.RiseTime;
                    vml.Main.DataModel.Processmodel.TVParameter.FallTime = config.TVParameter.FallTime;
                    vml.Main.DataModel.Processmodel.TVParameter.High = config.TVParameter.High;
                    vml.Main.DataModel.Processmodel.TVParameter.Low = config.TVParameter.Low;
                    vml.Main.DataModel.Processmodel.TVParameter.Arc = config.TVParameter.Arc;
                    vml.Main.DataModel.Processmodel.TVParameter.Freq = config.TVParameter.Freq;

                    // 应用压力参数到系统
                    vml.Main.DataModel.Processmodel.PressureParamter.Pressure = config.PressureParameter.Pressure;
                    vml.Main.DataModel.Processmodel.PressureParamter.Max_Pressure = config.PressureParameter.Max_Pressure;
                    vml.Main.DataModel.Processmodel.PressureParamter.Min_Pressure = config.PressureParameter.Min_Pressure;

                    // 【新增】写入测试模式到PLC
                    AT9620.ElectricalTestMode electricalTestMode = (AT9620.ElectricalTestMode)config.ElectricalTestModeValue;
                    bool writeModeResult = vml.Main.WriteTestModeToPLC(electricalTestMode);
                    if (writeModeResult)
                    {
                        vml.Main.writeLog($"[手动参数-测试模式] 成功写入PLC: {GetTestModeDisplayName(electricalTestMode)} (值={config.ElectricalTestModeValue})");
                    }
                    else
                    {
                        vml.Main.writeLog($"[手动参数-测试模式] ⚠️ 写入PLC失败: {GetTestModeDisplayName(electricalTestMode)} (值={config.ElectricalTestModeValue})", true);
                    }

                    // 下发参数到设备（完全复用现有逻辑）
                    var rd1 = vml.Main.DataModel.Settingmodel.AT9620_1.Download();
                    var rd2 = vml.Main.DataModel.Settingmodel.AT9620_2.Download();
                    var rd3 = vml.Main.DataModel.Settingmodel.AT9620_3.Download();
                    var rd4 = vml.Main.Download_PressureParameter();

                    // 根据设备启用状态判断结果（完全复用现有逻辑）
                    if ((!rd1.Success) && vml.Main.DataModel.Processmodel.TVAvailable.TV1Available)
                    {
                        NoticeBox.Show("耐压工位1参数下发失败", "错误", MessageBoxIcon.Error);
                    }
                    else if ((!rd2.Success) && vml.Main.DataModel.Processmodel.TVAvailable.TV2Available)
                    {
                        NoticeBox.Show("耐压工位2参数下发失败", "错误", MessageBoxIcon.Error);
                    }
                    else if ((!rd3.Success) && vml.Main.DataModel.Processmodel.TVAvailable.TV3Available)
                    {
                        NoticeBox.Show("耐压工位3参数下发失败", "错误", MessageBoxIcon.Error);
                    }
                    else if (!rd4)
                    {
                        NoticeBox.Show("压力参数下发失败", "错误", MessageBoxIcon.Error);
                    }
                    else
                    {
                        NoticeBox.Show($"手动参数下发完成\n配置：{config.ConfigName}", "成功", MessageBoxIcon.Success, true, 5000);

                        // 手动下发成功后，同步刷新阻值阈值（D2020=Res_Max_Address）
                        float resMaxThreshold = vml.Main.DataModel.Processmodel.ResParameter.Max_Res;
                        float originalResMaxThreshold = resMaxThreshold;
                        string resMaxSource = "内存默认";
                        try
                        {
                            float fromFloat = vml.Main.PLC_ReadFloat(vml.Main.DataModel.Settingmodel.Res_Max_Address);
                            if (fromFloat > 0 && fromFloat < 1000)
                            {
                                resMaxThreshold = fromFloat;
                                resMaxSource = "D2020(float)";
                            }
                            else
                            {
                                throw new Exception("PLC_ReadFloat范围不合理");
                            }
                        }
                        catch (Exception exFloat)
                        {
                            try
                            {
                                UInt16 fromU16 = vml.Main.PLC_ReadUint16(vml.Main.DataModel.Settingmodel.Res_Max_Address);
                                if (fromU16 > 0 && fromU16 < 1000)
                                {
                                    resMaxThreshold = fromU16;
                                    resMaxSource = "D2020(uint16)";
                                }
                                else if (fromU16 >= 1000)
                                {
                                    resMaxThreshold = fromU16 / 1000f;
                                    resMaxSource = "D2020(uint16/scale)";
                                }
                            }
                            catch (Exception exU16)
                            {
                                // 读取失败时保持当前内存默认值
                                vml.Main.writeLog(
                                    $"[阻值阈值] 手动刷新-读取PLC D{vml.Main.DataModel.Settingmodel.Res_Max_Address}失败( Float:{exFloat.Message}; U16:{exU16.Message} )，使用旧值 Max_Res={originalResMaxThreshold}",
                                    true);
                            }
                        }

                        vml.Main.DataModel.Processmodel.ResParameter.Max_Res = resMaxThreshold;
                        vml.Main.writeLog($"[阻值阈值] 手动刷新PLC D{vml.Main.DataModel.Settingmodel.Res_Max_Address} -> Max_Res={resMaxThreshold} ({resMaxSource})");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBoxX.Show($"加载配置文件失败：{ex.Message}", MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 创建示例配置文件
        /// </summary>
        private void CreateExampleConfigFile(string filePath)
        {
            try
            {
                var exampleConfig = new BusbarCompressionSystem.Model.ManualParameterConfig
                {
                    ConfigName = "标准工艺参数_2700V",
                    CreateTime = "2025-01-17",
                    Description = "2700V标准耐压测试参数",
                    ElectricalTestModeValue = 0, // 0=只测交流
                    TVParameter = new BusbarCompressionSystem.Model.TVParameterConfig
                    {
                        TestMode = 0,
                        Voltage = 2700,
                        TestTime = 20,
                        RiseTime = 2,
                        FallTime = 2,
                        High = 3,
                        Low = 0.1f,
                        Arc = 0,
                        Freq = 50
                    },
                    PressureParameter = new BusbarCompressionSystem.Model.PressureParameterConfig
                    {
                        Pressure = 1000,
                        Max_Pressure = 1050,
                        Min_Pressure = 950
                    }
                };

                string jsonContent = Newtonsoft.Json.JsonConvert.SerializeObject(exampleConfig, Newtonsoft.Json.Formatting.Indented);
                System.IO.File.WriteAllText(filePath, jsonContent);
            }
            catch (Exception ex)
            {
                // 忽略创建示例文件的错误
            }
        }

        /// <summary>
        /// 获取测试模式的中文显示名称
        /// </summary>
        private string GetTestModeDisplayName(AT9620.ElectricalTestMode mode)
        {
            switch (mode)
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
}
