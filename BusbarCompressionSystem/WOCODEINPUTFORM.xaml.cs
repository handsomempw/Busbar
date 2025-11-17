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

                var r1 = ps.Where(p => p.ParameterName == "测试电压");
                var r2 = ps.Where(p => p.ParameterName == "电流类型");
                var r3 = ps.Where(p => p.ParameterName == "上升时间");
                var r4 = ps.Where(p => p.ParameterName == "测试时间");
                var r5 = ps.Where(p => p.ParameterName == "下降时间");
                var r6 = ps.Where(p => p.ParameterName == "测试电流");
                var r7 = ps.Where(p => p.ParameterName == "充电电流下限");
                var r8 = ps.Where(p => p.ParameterName == "测试频率");
                var r9 = ps.Where(p => p.ParameterName == "极壳压力上限");
                var r10 = ps.Where(p => p.ParameterName == "极壳压力下限");
                var r11 = ps.Where(p => p.ParameterName == "极壳压力");

                string error = "缺少以下工艺参数:\r\n";
                bool r = true;
                if (r1.Count() == 0)
                {
                    error += "测试电压\r\n";
                    r = false;
                }
                if (r2.Count() == 0)
                {
                    error += "电流类型\r\n";
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

                if (r)
                {
                    vml.Main.DataModel.Processmodel.TVParameter.Voltage = Convert.ToSingle(r1.First().TargetValue);
                    vml.Main.DataModel.Processmodel.TVParameter.TestMode = (TestMode)Convert.ToInt16(r2.First().TargetValue);
                    vml.Main.DataModel.Processmodel.TVParameter.RiseTime = Convert.ToSingle(r3.First().TargetValue);
                    vml.Main.DataModel.Processmodel.TVParameter.TestTime = Convert.ToSingle(r4.First().TargetValue);
                    vml.Main.DataModel.Processmodel.TVParameter.FallTime = Convert.ToSingle(r5.First().TargetValue);
                    vml.Main.DataModel.Processmodel.TVParameter.High = Convert.ToSingle(r6.First().TargetValue);
                    vml.Main.DataModel.Processmodel.TVParameter.Low = Convert.ToSingle(r7.First().TargetValue);
                    vml.Main.DataModel.Processmodel.TVParameter.Freq = Convert.ToSingle(r8.First().TargetValue);

                    vml.Main.DataModel.Processmodel.PressureParamter.Max_Pressure = Convert.ToSingle(r9.First().TargetValue);
                    vml.Main.DataModel.Processmodel.PressureParamter.Min_Pressure = Convert.ToSingle(r10.First().TargetValue);
                    vml.Main.DataModel.Processmodel.PressureParamter.Pressure=Convert.ToUInt16(r11.First().TargetValue);    


                    var rd1 = vml.Main.DataModel.Settingmodel.AT9620_1.Download();
                    var rd2 = vml.Main.DataModel.Settingmodel.AT9620_2.Download();
                    var rd3 = vml.Main.DataModel.Settingmodel.AT9620_3.Download();

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
    }
}
