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

                string error = "缺少以下工艺参数:\r\n";
                bool r = true;
                
                // 检查ACW交流参数
                if (r1.Count() == 0)
                {
                    error += "测试电压\r\n";
                    r = false;
                }
                if (r2.Count() == 0)
                {
                    error += "测试模式\r\n";
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
                
                // 检查DCW直流参数（MES总是提供完整的26个参数）
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

                if (r)
                {
                    // 解析测试模式
                    int testModeValue = Convert.ToInt16(r2.First().TargetValue);
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

                    // 解析ACW交流参数
                    vml.Main.DataModel.Processmodel.ACWParameter.Voltage = Convert.ToSingle(r1.First().TargetValue);
                    vml.Main.DataModel.Processmodel.ACWParameter.TestMode = TestMode.ACW;
                    vml.Main.DataModel.Processmodel.ACWParameter.RiseTime = Convert.ToSingle(r3.First().TargetValue);
                    vml.Main.DataModel.Processmodel.ACWParameter.TestTime = Convert.ToSingle(r4.First().TargetValue);
                    vml.Main.DataModel.Processmodel.ACWParameter.FallTime = Convert.ToSingle(r5.First().TargetValue);
                    vml.Main.DataModel.Processmodel.ACWParameter.High = Convert.ToSingle(r6.First().TargetValue);
                    vml.Main.DataModel.Processmodel.ACWParameter.Low = Convert.ToSingle(r7.First().TargetValue);
                    vml.Main.DataModel.Processmodel.ACWParameter.Freq = Convert.ToSingle(r8.First().TargetValue);
                    vml.Main.DataModel.Processmodel.ACWParameter.Arc = 0; // 使用默认值

                    // 解析DCW直流参数
                    vml.Main.DataModel.Processmodel.DCWParameter.Voltage = Convert.ToSingle(dcw1.First().TargetValue);
                    vml.Main.DataModel.Processmodel.DCWParameter.TestMode = TestMode.DCW;
                    vml.Main.DataModel.Processmodel.DCWParameter.RiseTime = Convert.ToSingle(dcw2.First().TargetValue);
                    vml.Main.DataModel.Processmodel.DCWParameter.TestTime = Convert.ToSingle(dcw3.First().TargetValue);
                    vml.Main.DataModel.Processmodel.DCWParameter.FallTime = Convert.ToSingle(dcw4.First().TargetValue);
                    vml.Main.DataModel.Processmodel.DCWParameter.High = Convert.ToSingle(dcw5.First().TargetValue);
                    vml.Main.DataModel.Processmodel.DCWParameter.Low = Convert.ToSingle(dcw6.First().TargetValue);
                    vml.Main.DataModel.Processmodel.DCWParameter.Freq = 0; // DCW不需要频率
                    vml.Main.DataModel.Processmodel.DCWParameter.Arc = 0; // MES未提供，使用默认值

                    // 兼容旧代码：同步设置TVParameter（使用ACW参数）
                    vml.Main.DataModel.Processmodel.TVParameter.Voltage = vml.Main.DataModel.Processmodel.ACWParameter.Voltage;
                    vml.Main.DataModel.Processmodel.TVParameter.TestMode = TestMode.ACW;
                    vml.Main.DataModel.Processmodel.TVParameter.RiseTime = vml.Main.DataModel.Processmodel.ACWParameter.RiseTime;
                    vml.Main.DataModel.Processmodel.TVParameter.TestTime = vml.Main.DataModel.Processmodel.ACWParameter.TestTime;
                    vml.Main.DataModel.Processmodel.TVParameter.FallTime = vml.Main.DataModel.Processmodel.ACWParameter.FallTime;
                    vml.Main.DataModel.Processmodel.TVParameter.High = vml.Main.DataModel.Processmodel.ACWParameter.High;
                    vml.Main.DataModel.Processmodel.TVParameter.Low = vml.Main.DataModel.Processmodel.ACWParameter.Low;
                    vml.Main.DataModel.Processmodel.TVParameter.Freq = vml.Main.DataModel.Processmodel.ACWParameter.Freq;
                    vml.Main.DataModel.Processmodel.TVParameter.Arc = vml.Main.DataModel.Processmodel.ACWParameter.Arc;

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
                    else if (!rd4)
                    {
                        NoticeBox.Show("压力参数下发失败", "错误", MessageBoxIcon.Error);
                    }
                    else
                    {
                        NoticeBox.Show("参数下发完成", "成功", MessageBoxIcon.Success, true, 5000);
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
                string exampleConfigPath = $"{configDir}\\手动下发工艺参数配置.json";
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
