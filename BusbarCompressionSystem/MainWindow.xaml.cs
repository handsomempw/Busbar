/*
 * 主窗口控制器
 *
 * MVVM架构中的View层代码，主要负责：
 * 1. 系统启动时初始化所有硬件设备（相机、PLC、测试仪器等）
 * 2. 处理用户界面的事件和交互
 * 3. 调用ViewModel执行业务逻辑
 * 4. 管理多线程任务（耐压测试、视觉检测等）
 *
 * 核心功能模块：
 * - 视觉检测AOI：使用Halcon进行图像处理
 * - 耐压测试：控制AT9620设备进行多工位测试
 * - PLC控制：与工业控制系统通信
 * - 生产数据管理：实时记录和保存测试结果
 */

using BusbarCompressionSystem.Model;
using BusbarCompressionSystem.Model.FaraVision;
using BusbarCompressionSystem.Utils;
using BusbarCompressionSystem.ViewModel;
using HalconDotNet;
using Microsoft.Win32;
using Panuon.WPF.UI;
using SQLITEDATABASE;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace BusbarCompressionSystem
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : WindowX
    {
        ViewModelLocator vml = null;
        public MainWindow()
        {
            InitializeComponent();
            vml = this.FindResource("Locator") as ViewModelLocator;
        }

        private void WindowX_Loaded(object sender, RoutedEventArgs e)
        {

            OnlyOnce.check.OnlyOnce();

            vml.Main.LoadSettingModel();
            vml.Main.LoadRecordModel();
            vml.Main.LoadProcessmodel();

            vml.Main.Faravision_LoadSettingModel();
            vml.Main.Load_Prj();


            inittvparameter();
            vml.Main.InitAt9620();
            vml.Main.PLC_shankhand();
            vml.Main.PLC_Start();
            vml.Main.InitCamera();
            InitHwindow();
            vml.Main.InitRobotServer();

            vml.Main.InitHwindow(Hwindow4.HalconWindow);

            InitFaraVisionCamera();
        }


        public void InitFaraVisionCamera()
        {
            vml.Main.DataModel.FaraVisionDataModel.Processmodel.CameraIDList.Add(vml.Main.DataModel.Settingmodel.camedata4.CameraModel.CameraID);
            vml.Main.DataModel.FaraVisionDataModel.Processmodel.CameraIDList.Add(vml.Main.DataModel.Settingmodel.camedata5.CameraModel.CameraID);

            vml.Main.DataModel.FaraVisionDataModel.Processmodel.CameraList.Add(vml.Main.DataModel.Settingmodel.camedata4);
            vml.Main.DataModel.FaraVisionDataModel.Processmodel.CameraList.Add(vml.Main.DataModel.Settingmodel.camedata5);

        }


        public void InitHwindow()
        {
            vml.Main.DataModel.Settingmodel.HWindow1 = Hwindow1.HalconWindow;
            vml.Main.DataModel.Settingmodel.HWindow2 = Hwindow2.HalconWindow;
            vml.Main.DataModel.Settingmodel.HWindow3 = Hwindow3.HalconWindow;
            vml.Main.DataModel.Settingmodel.HWindow4 = Hwindow4.HalconWindow;
        }

        private void WindowX_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (MessageBoxX.Show("是否确定关闭运行软件?", "提示", MessageBoxButton.YesNo, MessageBoxIcon.Question) == MessageBoxResult.Yes)
            {
                vml.Main.SaveSettingModel();
                vml.Main.SaveRecordModel();
                vml.Main.SaveProcessmodel();

                vml.Main.Faravision_SaveSettingModel();

                vml.Main.SavePrjXmls();

                vml.Main.CloseCamera();
                
                // 关闭扫码器连接，防止资源残留
                try
                {
                    if (vml.Main.DataModel.Settingmodel.ScannerMode == "HF800")
                    {
                        vml.Main.DataModel.Settingmodel.HF800.disconnect();
                    }
                }
                catch { }

                Environment.Exit(0);
            }
            else
            {
                e.Cancel = true;
            }
        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                vml.Main.ScanSN();
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            vml.Main.ScanSN();
        }

        private void inittvparameter()
        {

            //vml.Main.DataModel.Processmodel.TVParameter.RiseTime = 5;
            //vml.Main.DataModel.Processmodel.TVParameter.FallTime = 5;
            //vml.Main.DataModel.Processmodel.TVParameter.TestTime = 20;
            //vml.Main.DataModel.Processmodel.TVParameter.Voltage = 50;
            //vml.Main.DataModel.Processmodel.TVParameter.High = 10;
            //vml.Main.DataModel.Processmodel.TVParameter.Low = 0;
            vml.Main.DataModel.Settingmodel.AT9620_1.TVParameter = vml.Main.DataModel.Processmodel.TVParameter;
            vml.Main.DataModel.Settingmodel.AT9620_2.TVParameter = vml.Main.DataModel.Processmodel.TVParameter;
            vml.Main.DataModel.Settingmodel.AT9620_3.TVParameter = vml.Main.DataModel.Processmodel.TVParameter;
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {

            new Thread(() =>
            {
                vml.Main.DataModel.Settingmodel.AT9620_1.Start();
                vml.Main.DataModel.Processmodel.TVTestTestModel1.TVMaxVoltage = 0;
                vml.Main.DataModel.Processmodel.TVTestTestModel1.TVMaxCurrent = 0;


                vml.Main.DataModel.Settingmodel.AT9620_2.Start();
                vml.Main.DataModel.Processmodel.TVTestTestModel2.TVMaxVoltage = 0;
                vml.Main.DataModel.Processmodel.TVTestTestModel2.TVMaxCurrent = 0;

                vml.Main.DataModel.Settingmodel.AT9620_3.Start();
                vml.Main.DataModel.Processmodel.TVTestTestModel3.TVMaxVoltage = 0;
                vml.Main.DataModel.Processmodel.TVTestTestModel3.TVMaxCurrent = 0;

            }).Start();



        }

        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            new Thread(() =>
            {
                // 【日志】记录参数下发开始，包含当前耐压参数详细信息
                var tvParam = vml.Main.DataModel.Processmodel.TVParameter;
                vml.Main.writeLog($"==========开始耐压参数下发==========");
                vml.Main.writeLog($"参数详情: 电压={tvParam.Voltage}V, 测试时间={tvParam.TestTime}s, 上升时间={tvParam.RiseTime}s, 下降时间={tvParam.FallTime}s");
                vml.Main.writeLog($"参数详情: 电流上限={tvParam.High}mA, 电流下限={tvParam.Low}mA, 电弧值={tvParam.Arc}, 频率={tvParam.Freq}Hz");
                vml.Main.writeLog($"参数详情: 测试模式={tvParam.TestMode}");
                
                // 【日志】耐压仪器1参数下发
                vml.Main.writeLog($"[耐压1] 开始连接仪器... IP={vml.Main.DataModel.Settingmodel.AT9620_1.IP}, 端口={vml.Main.DataModel.Settingmodel.AT9620_1.Port}");
                var r1 = vml.Main.DataModel.Settingmodel.AT9620_1.Download();
                if (!r1.Success)
                {
                    vml.Main.writeLog($"[耐压1] 参数下发失败! 错误信息: {r1.Error}", true);
                    NoticeBox.Show("耐压工位1参数下发失败", "错误", MessageBoxIcon.Error);
                }
                else
                {
                    vml.Main.writeLog($"[耐压1] 参数下发成功！");
                }
                
                // 【日志】耐压仪器2参数下发
                vml.Main.writeLog($"[耐压2] 开始连接仪器... IP={vml.Main.DataModel.Settingmodel.AT9620_2.IP}, 端口={vml.Main.DataModel.Settingmodel.AT9620_2.Port}");
                var r2 = vml.Main.DataModel.Settingmodel.AT9620_2.Download();
                if (!r2.Success)
                {
                    vml.Main.writeLog($"[耐压2] 参数下发失败! 错误信息: {r2.Error}", true);
                    NoticeBox.Show("耐压工位2参数下发失败", "错误", MessageBoxIcon.Error);
                }
                else
                {
                    vml.Main.writeLog($"[耐压2] 参数下发成功！");
                }
                
                // 【优化】第三个仪器已禁用，跳过参数下发，但保留检查逻辑以兼容旧代码
                vml.Main.writeLog($"[耐压3] 已禁用，跳过参数下发（设备已更新）");
                var r3 = new AT9620.Result() { Success = true }; // 模拟成功，避免影响整体流程
                //var r3 = vml.Main.DataModel.Settingmodel.AT9620_3.Download();
                if (!r3.Success)
                {
                    vml.Main.writeLog($"[耐压3] 参数下发失败（此仪器已禁用，不应该执行到这里）", true);
                    NoticeBox.Show("耐压工位3参数下发失败", "错误", MessageBoxIcon.Error);
                }

                // 【日志】汇总结果
                if (r1.Success && r2.Success && r3.Success)
                {
                    vml.Main.writeLog($"==========参数下发全部完成==========");
                    NoticeBox.Show("参数下发完成", "成功", MessageBoxIcon.Success, true, 3000);
                }
                else
                {
                    vml.Main.writeLog($"==========参数下发存在失败项==========", true);
                }
            }).Start();
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBoxX.Show("是否确定清空数据", "提示", MessageBoxButton.YesNo, MessageBoxIcon.Question, DefaultButton.NoCancel) != MessageBoxResult.Yes)
            {
                return;
            }
            vml.Main.DataModel.Recordmodel.ProductInfoRecords.Clear();

        }

        private void Button_Click_3(object sender, RoutedEventArgs e)
        {
            WOCODEINPUTFORM Wf = new WOCODEINPUTFORM();
            Wf.ShowDialog();
        }

        private void MenuItem_Click_1(object sender, RoutedEventArgs e)
        {
            if (MessageBoxX.Show("是否确定清空数据", "提示", MessageBoxButton.YesNo, MessageBoxIcon.Question, DefaultButton.NoCancel) != MessageBoxResult.Yes)
            {
                return;
            }


            for (int i = vml.Main.DataModel.Recordmodel.ProductInfoRecords.Count - 1; i >= 10; i--)
            {
                try
                {
                    vml.Main.DataModel.Recordmodel.ProductInfoRecords.RemoveAt(i);
                }
                catch (Exception ex) {; }
            }
        }

        private void check1_Click(object sender, RoutedEventArgs e)
        {
            sqlite.Check1("N2T070A446", "BTBBC3623562D001", "MT03956915");
        }


        #region AOI
        private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (vml.Main.DataModel.FaraVisionDataModel.Processmodel.selectedindex >= 0)
            {
                vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool = vml.Main.DataModel.FaraVisionDataModel.Processmodel.Tools[vml.Main.DataModel.FaraVisionDataModel.Processmodel.selectedindex];
            }
        }
        private void ListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (!vml.Main.DataModel.FaraVisionDataModel.Settingmodel.permission)
            {
                NoticeBox.Show("请先打开权限，再进行编辑", "提示", MessageBoxIcon.Warning, true, 5000);
                return;
            }
            if (MessageBoxX.Show("是否确定打开编辑工具？", "提示", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                vml.Main.DataModel.FaraVisionDataModel.Processmodel.tool = vml.Main.DataModel.FaraVisionDataModel.Processmodel.Tools[vml.Main.DataModel.FaraVisionDataModel.Processmodel.selectedindex];
                vml.Main.LoadBitmapSource();
                //vml.Main.DataModel.Processmodel.tool = @vml.Main.DataModel.Processmodel.Tools[vml.Main.DataModel.Processmodel.selectedindex];
                Model.FaraVision.SettingForm settingForm = new Model.FaraVision.SettingForm();
                //settingForm.Topmost = true;
                settingForm.ShowDialog();
                //settingForm.Show();


            }
        }

        private void ChangePrj_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                vml.Main.Load_Prj(vml.Main.DataModel.FaraVisionDataModel.Settingmodel.Prjs[vml.Main.DataModel.FaraVisionDataModel.Settingmodel.prjselected]);
                vml.Main.InitHwindow(Hwindow4.HalconWindow);
                vml.Main.InitShm();

            }
            catch (Exception ex)
            {
                NoticeBox.Show($"工程加载错误:{ex.ToString()}", "错误", MessageBoxIcon.Error, true, 5000);
            }
        }


        private void permissionbtn_Click(object sender, RoutedEventArgs e)
        {
            if (vml.Main.DataModel.FaraVisionDataModel.Settingmodel.permission)
            {
                vml.Main.DataModel.FaraVisionDataModel.Settingmodel.permission = false;
                return;
            }
            InputPassword ip = new InputPassword("faracheck");
            if (ip.ShowDialog() == true)
            {
                vml.Main.DataModel.FaraVisionDataModel.Settingmodel.permission = true;
            }
        }


        private void tooltest_Click(object sender, RoutedEventArgs e)
        {

            try
            {
                OpenFileDialog ofd = new OpenFileDialog();
                ofd.Filter = "*.jpg|*.jpg";
                if (ofd.ShowDialog() == true)
                {
                    HTuple W = new HTuple(), H = new HTuple();

                    HObject image;
                    HOperatorSet.GenEmptyObj(out image);
                    HOperatorSet.ReadImage(out image, ofd.FileName);
                    HOperatorSet.GetImageSize(image, out W, out H);
                    vml.Main.DataModel.FaraVisionDataModel.Processmodel.ToolIndex = vml.Main.DataModel.FaraVisionDataModel.Processmodel.selectedindex;
                    vml.Main.DataModel.FaraVisionDataModel.Processmodel.RCMD = vml.Main.DataModel.FaraVisionDataModel.Processmodel.Tools[vml.Main.DataModel.FaraVisionDataModel.Processmodel.selectedindex].Command;
                    //vml.Main.OnReceiveProcess(image, (int)H, (int)W);
                    vml.Main.OnReceiveProcessAOI(image, (int)H, (int)W);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"发生错误，请先选择需要测试的工具再选择测试照片:\r\n{ex.ToString()}");
                ;
            }
        }





        #endregion

        private void Button_Click_4(object sender, RoutedEventArgs e)
        {
            SettingForm sf = new SettingForm();
            sf.ShowDialog();
        }

        private void REPORT2MES_Click(object sender, RoutedEventArgs e)
        {


            foreach (var pi in vml.Main.DataModel.Recordmodel.ProductInfoRecords)
            {
                if ("MT03956957" == pi.Productinfo.SN)
                {
                    var persistedTvInfo = TvStatusTranslator.Translate(pi.TVInfo);
                    MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.SaveBusBarData(
                vml.Main.DataModel.Settingmodel.SETTING_DATA.StationCode, vml.Main.DataModel.Settingmodel.SETTING_DATA.MachineID, pi.Productinfo.PartNOID, pi.Productinfo.WOCODE, pi.Productinfo.SN,
                        pi.TakePhoto1, pi.Res, pi.TVMaxVoltage, pi.TVMaxCurrent, pi.TVMeterID, persistedTvInfo, pi.TVResult,
                        pi.Pressure_Max, pi.Pressure_Average, pi.Pressure_Min, pi.Pressure_Result, pi.AppearanceInspection, "OK");
                    break;
                }
            }
        }
    }
}
