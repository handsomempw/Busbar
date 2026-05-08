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
using BusbarCompressionSystem.Model.FaraVision.Tool;
using BusbarCompressionSystem.Utils;
using BusbarCompressionSystem.ViewModel;
using HalconDotNet;
using Microsoft.Win32;
using Panuon.WPF.UI;
using SQLITEDATABASE;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
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

        // 用于维护“动态密码授权”生命周期，密码过期后自动回收 AOI 编辑权限
        private DynamicPasswordAuthService aoiPermissionAuthService;
        private EventHandler aoiPermissionExpiredHandler;
        private CancellationTokenSource aoiPermissionCts;

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
            ApplyProductInfoRecordsSort();

            vml.Main.Faravision_LoadSettingModel();
            vml.Main.Load_Prj();


            inittvparameter();
            vml.Main.InitAt9620();
            vml.Main.InitAT6835FL();
            vml.Main.PLC_shankhand();
            vml.Main.PLC_Start();
            vml.Main.InitCamera();
            InitHwindow();
            vml.Main.InitRobotServer();

            vml.Main.InitHwindow(Hwindow4.HalconWindow);

            InitFaraVisionCamera();
        }

        private void ApplyProductInfoRecordsSort()
        {
            var records = vml?.Main?.DataModel?.Recordmodel?.ProductInfoRecords;
            if (records == null)
            {
                return;
            }

            var view = CollectionViewSource.GetDefaultView(records);
            if (view == null)
            {
                return;
            }

            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription("DateTime", ListSortDirection.Descending));
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

        /// <summary>
        /// 处理操作员确认后的软件关闭流程。
        /// 关闭前保存运行配置、过程数据和工程 XML；若 AOI 编辑权限仍处于授权会话内，
        /// 工程保存必须先于动态密码资源释放执行，以保留参数审计需要的授权人上下文。
        /// </summary>
        /// <param name="sender">WPF 关闭事件来源，当前流程不依赖具体控件实例。</param>
        /// <param name="e">关闭控制参数；操作员取消关闭时设置为取消，避免中断现场运行界面。</param>
        private void WindowX_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (MessageBoxX.Show("是否确定关闭运行软件?", "提示", MessageBoxButton.YesNo, MessageBoxIcon.Question) == MessageBoxResult.Yes)
            {
                vml.Main.SaveSettingModel();
                vml.Main.SaveRecordModel();
                vml.Main.SaveProcessmodel();

                vml.Main.Faravision_SaveSettingModel();

                vml.Main.SavePrjXmls();

                // 工程保存完成后再释放动态密码授权，保留保存审计需要的授权人上下文。
                DisposeAoiPermissionAuthService();

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
            // 初始化ACW参数（默认使用TVParameter的值）
            vml.Main.DataModel.Processmodel.ACWParameter.TestMode = AT9620.TestMode.ACW;
            
            // 初始化DCW参数（默认使用TVParameter的值，TestMode设为DCW）
            vml.Main.DataModel.Processmodel.DCWParameter.TestMode = AT9620.TestMode.DCW;
            
            // 默认使用ACW参数关联到AT9620设备
            // 实际测试时会根据TV1Trig/TV2Trig的值动态切换
            vml.Main.DataModel.Settingmodel.AT9620_1.TVParameter = vml.Main.DataModel.Processmodel.ACWParameter;
            vml.Main.DataModel.Settingmodel.AT9620_2.TVParameter = vml.Main.DataModel.Processmodel.ACWParameter;
            vml.Main.DataModel.Settingmodel.AT9620_3.TVParameter = vml.Main.DataModel.Processmodel.ACWParameter;
            
            // 初始化上次测试模式为ACW
            vml.Main.DataModel.Processmodel.LastTV1TestMode = AT9620.TestMode.ACW;
            vml.Main.DataModel.Processmodel.LastTV2TestMode = AT9620.TestMode.ACW;

            //vml.Main.DataModel.Processmodel.TVParameter.RiseTime = 5;
            //vml.Main.DataModel.Processmodel.TVParameter.FallTime = 5;
            //vml.Main.DataModel.Processmodel.TVParameter.TestTime = 20;
            //vml.Main.DataModel.Processmodel.TVParameter.Voltage = 50;
            //vml.Main.DataModel.Processmodel.TVParameter.High = 10;
            //vml.Main.DataModel.Processmodel.TVParameter.Low = 0;
            // 旧逻辑
            // vml.Main.DataModel.Settingmodel.AT9620_1.TVParameter = vml.Main.DataModel.Processmodel.TVParameter;
            // vml.Main.DataModel.Settingmodel.AT9620_2.TVParameter = vml.Main.DataModel.Processmodel.TVParameter;
            // vml.Main.DataModel.Settingmodel.AT9620_3.TVParameter = vml.Main.DataModel.Processmodel.TVParameter;
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


            var records = vml.Main.DataModel.Recordmodel.ProductInfoRecords;
            var latestRecords = records
                .OrderByDescending(p => p?.DateTime ?? DateTime.MinValue)
                .Take(10)
                .ToList();

            for (int i = records.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (!latestRecords.Contains(records[i]))
                    {
                        records.RemoveAt(i);
                    }
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

                // 进入编辑界面前保存工具快照，作为“保存工程时参数差异审计”的旧值来源
                CaptureEditingToolSnapshotForAudit();

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


        /// <summary>
        /// 处理标题栏权限按钮的动态密码授权和手动锁定流程。
        /// 已授权时先执行权限关闭前保存，再回收编辑权限和授权上下文；未授权时打开动态密码验证窗口，
        /// 验证通过后保存本次授权人信息，供后续工程保存时生成 AOI 参数差异审计。
        /// </summary>
        /// <param name="sender">权限按钮实例，用于在动态密码验证期间临时禁用重复点击。</param>
        /// <param name="e">WPF 点击事件参数，当前流程不读取附加事件数据。</param>
        private void permissionbtn_Click(object sender, RoutedEventArgs e)
        {
            if (vml.Main.DataModel.FaraVisionDataModel.Settingmodel.permission)
            {
                bool saved = TrySaveBeforePermissionClose();

                vml.Main.DataModel.FaraVisionDataModel.Settingmodel.permission = false;
                DisposeAoiPermissionAuthService();

                if (saved)
                {
                    NoticeBox.Show("权限已关闭，已执行工具配置自动保存", "提示", MessageBoxIcon.Success, true, 3000);
                }
                else
                {
                    NoticeBox.Show("权限已关闭，自动保存失败，请检查日志", "提示", MessageBoxIcon.Warning, true, 5000);
                }
                return;
            }

            // 将固定口令改为动态密码（通过 Faratronic.EquipUtils.Authentication 接入）
            var authSetting = vml.Main.DataModel.Settingmodel.SETTING_DATA.DynamicPasswordAuth;
            if (authSetting == null || !authSetting.Enabled)
            {
                NoticeBox.Show("动态密码认证未启用，请在“配置\\配置数据.xml”中开启后重试", "提示", MessageBoxIcon.Warning, true, 5000);
                return;
            }

            string equipNo = vml.Main.DataModel.Settingmodel.SETTING_DATA.MachineID;
            if (string.IsNullOrWhiteSpace(equipNo) || equipNo == "设备编号")
            {
                NoticeBox.Show("设备编号未配置（用于动态密码认证），请先在配置中填写设备编号", "提示", MessageBoxIcon.Warning, true, 5000);
                return;
            }

            // 防止重复点击（模板内按钮无法直接字段访问，使用 sender）
            var permissionButton = sender as Button;
            if (permissionButton != null)
            {
                permissionButton.IsEnabled = false;
            }
            try
            {
                // 若之前存在授权对象，先释放，避免多路计时/事件叠加
                DisposeAoiPermissionAuthService();

                CancellationTokenSource cts = new CancellationTokenSource();
                DynamicPasswordAuthService service = new DynamicPasswordAuthService(authSetting, equipNo);
                try
                {
                    // UI-2：在授权窗口内完成“申请→显示接收人→输入→验证”的闭环
                    DynamicPasswordAuthWindow window = new DynamicPasswordAuthWindow(service, "AOI工具编辑/参数修改", cts.Token);
                    if (window.ShowDialog() != true)
                    {
                        return;
                    }

                    if (window.VerifyInfo == null || !window.VerifyInfo.VerifyStatus)
                    {
                        NoticeBox.Show("动态密码验证未通过，无法开启权限", "提示", MessageBoxIcon.Warning, true, 5000);
                        return;
                    }

                    // 验证通过：打开权限，并保持 service 存活以便监听“密码过期”事件自动回收权限
                    aoiPermissionCts = cts;
                    aoiPermissionAuthService = service;
                    cts = null;
                    service = null;

                    // 保存授权上下文到模型中，供“保存工程”时生成参数差异日志使用
                    vml.Main.DataModel.FaraVisionDataModel.Settingmodel.PermissionGrantedAt = DateTime.Now;
                    vml.Main.DataModel.FaraVisionDataModel.Settingmodel.PermissionAuthorizerName = window.VerifyInfo.AuthorizerName ?? string.Empty;
                    vml.Main.DataModel.FaraVisionDataModel.Settingmodel.PermissionAuthorizerNo = window.VerifyInfo.AuthorizerNo ?? string.Empty;
                    vml.Main.DataModel.FaraVisionDataModel.Settingmodel.PermissionPrivilegeLevel = window.VerifyInfo.PrivilegeLevel;
                    vml.Main.DataModel.FaraVisionDataModel.Settingmodel.PermissionPeriodMinutes = authSetting.PeriodMinutes;
                    vml.Main.DataModel.FaraVisionDataModel.Settingmodel.PermissionReason = "AOI工具编辑/参数修改";

                    if (window.RequestedReceivers != null && window.RequestedReceivers.Count > 0)
                    {
                        vml.Main.DataModel.FaraVisionDataModel.Settingmodel.PermissionRequestedReceivers =
                            string.Join("；", window.RequestedReceivers.Select(r => $"{r.ReceiverName}({r.ReceiverNo})"));
                    }
                    else
                    {
                        vml.Main.DataModel.FaraVisionDataModel.Settingmodel.PermissionRequestedReceivers = string.Empty;
                    }

                    aoiPermissionExpiredHandler = (s, ex) =>
                    {
                        // 密码过期回调来自第三方库计时器线程，这里切回 UI 线程更新状态
                        Dispatcher.Invoke(() =>
                        {
                            bool saved = TrySaveBeforePermissionClose();

                            vml.Main.DataModel.FaraVisionDataModel.Settingmodel.permission = false;
                            DisposeAoiPermissionAuthService();
                            NoticeBox.Show(
                                saved
                                    ? "动态密码已过期，权限已自动关闭，已执行工具配置自动保存"
                                    : "动态密码已过期，权限已自动关闭，自动保存失败，请检查日志",
                                "提示",
                                MessageBoxIcon.Warning,
                                true,
                                6000);
                        });
                    };
                    aoiPermissionAuthService.PasswordExpired += aoiPermissionExpiredHandler;

                    vml.Main.DataModel.FaraVisionDataModel.Settingmodel.permission = true;

                    string periodTip = authSetting.PeriodMinutes > 0 ? $"（有效期 {authSetting.PeriodMinutes} 分钟）" : string.Empty;
                    NoticeBox.Show($"动态密码验证通过，已开启编辑权限{periodTip}", "提示", MessageBoxIcon.Success, true, 4000);
                }
                finally
                {
                    // 若未成功开启权限，则释放本次申请/验证过程中的资源
                    service?.Dispose();
                    cts?.Dispose();
                }
            }
            catch (Exception ex)
            {
                // 异常信息尽量简洁，避免把敏感信息（如动态密码）带入日志/提示
                NoticeBox.Show($"动态密码认证失败：{ex.Message}", "错误", MessageBoxIcon.Error, true, 6000);
            }
            finally
            {
                if (permissionButton != null)
                {
                    permissionButton.IsEnabled = true;
                }
            }
        }

        private void CaptureEditingToolSnapshotForAudit()
        {
            try
            {
                int selectedIndex = vml.Main.DataModel.FaraVisionDataModel.Processmodel.selectedindex;
                if (selectedIndex < 0 || selectedIndex >= vml.Main.DataModel.FaraVisionDataModel.Processmodel.Tools.Count)
                {
                    vml.Main.DataModel.FaraVisionDataModel.Processmodel.EditingToolIndex = -1;
                    vml.Main.DataModel.FaraVisionDataModel.Processmodel.EditingToolSnapshot = null;
                    vml.Main.DataModel.FaraVisionDataModel.Processmodel.EditingToolSnapshotTime = DateTime.MinValue;
                    return;
                }

                ToolModel tool = vml.Main.DataModel.FaraVisionDataModel.Processmodel.Tools[selectedIndex];
                vml.Main.DataModel.FaraVisionDataModel.Processmodel.EditingToolIndex = selectedIndex;
                vml.Main.DataModel.FaraVisionDataModel.Processmodel.EditingToolSnapshot = CloneToolModelForAudit(tool);
                vml.Main.DataModel.FaraVisionDataModel.Processmodel.EditingToolSnapshotTime = DateTime.Now;
            }
            catch
            {
                // 捕获异常，避免影响主流程
            }
        }

        private ToolModel CloneToolModelForAudit(ToolModel tool)
        {
            try
            {
                if (tool == null)
                {
                    return null;
                }

                // 使用XmlSerializer做深拷贝，确保快照与后续编辑互不影响
                var serializer = new XmlSerializer(typeof(ToolModel));
                using (var ms = new MemoryStream())
                {
                    serializer.Serialize(ms, tool);
                    ms.Position = 0;
                    return serializer.Deserialize(ms) as ToolModel;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 权限会话结束前执行工程保存，使 <c>permission</c> 和动态密码授权人工号仍处于可审计状态时完成项目 XML 落盘。
        /// 用于手动锁定和动态密码到期回收权限；保存失败不阻断权限关闭流程，异常原因写入现场日志供运维追溯。
        /// </summary>
        /// <returns>
        /// 未捕获到保存异常返回 <c>true</c>；若持久化调用向外抛出异常则返回 <c>false</c>。
        /// <c>SavePrjXmls</c> 内部已吞掉的异常不在该返回值范围内。
        /// </returns>
        private bool TrySaveBeforePermissionClose()
        {
            try
            {
                vml.Main.SaveProcessmodel();
                vml.Main.SaveSettingModel();
                vml.Main.SavePrjXmls();
                return true;
            }
            catch (Exception ex)
            {
                vml.Main.writeLog($"[动态密码][自动保存] 权限关闭前自动保存失败：{ex.Message}", true);
                return false;
            }
        }

        /// <summary>
        /// 释放动态密码授权会话资源，并清空仅用于运行期审计追溯的授权上下文。
        /// 调用方若需要记录本次授权会话内的 AOI 参数变更，应先完成工程保存，再调用该方法；
        /// 方法本身不负责修改 <c>permission</c>，只负责计时器、事件订阅和授权人信息的生命周期收尾。
        /// </summary>
        private void DisposeAoiPermissionAuthService()
        {
            try
            {
                aoiPermissionCts?.Cancel();
                aoiPermissionCts?.Dispose();
                aoiPermissionCts = null;
            }
            catch { }

            try
            {
                if (aoiPermissionAuthService != null && aoiPermissionExpiredHandler != null)
                {
                    aoiPermissionAuthService.PasswordExpired -= aoiPermissionExpiredHandler;
                }
            }
            catch { }
            finally
            {
                aoiPermissionExpiredHandler = null;
            }

            try
            {
                aoiPermissionAuthService?.Dispose();
                aoiPermissionAuthService = null;
            }
            catch { }

            // 权限关闭后清理授权上下文，避免后续误用
            try
            {
                vml.Main.DataModel.FaraVisionDataModel.Settingmodel.PermissionGrantedAt = DateTime.MinValue;
                vml.Main.DataModel.FaraVisionDataModel.Settingmodel.PermissionAuthorizerName = string.Empty;
                vml.Main.DataModel.FaraVisionDataModel.Settingmodel.PermissionAuthorizerNo = string.Empty;
                vml.Main.DataModel.FaraVisionDataModel.Settingmodel.PermissionRequestedReceivers = string.Empty;
                vml.Main.DataModel.FaraVisionDataModel.Settingmodel.PermissionPrivilegeLevel = 0;
                vml.Main.DataModel.FaraVisionDataModel.Settingmodel.PermissionPeriodMinutes = 0;
                vml.Main.DataModel.FaraVisionDataModel.Settingmodel.PermissionReason = string.Empty;
            }
            catch { }
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
