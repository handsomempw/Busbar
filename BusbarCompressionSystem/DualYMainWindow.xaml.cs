using BusbarCompressionSystem.Utils;
using BusbarCompressionSystem.ViewModel;
using Panuon.WPF.UI;
using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Data;

namespace BusbarCompressionSystem
{
    /// <summary>
    /// 双Y专用机台主窗口。该窗口承载参数下发、双工位扫码、电测监控和归档记录，
    /// HALCON、相机和机器人界面资源在本窗口生命周期内保持未创建状态。
    /// </summary>
    public partial class DualYMainWindow : WindowX
    {
        /// <summary>
        /// 应用级主视图模型定位器，供双Y窗口复用参数下发、PLC、电测和记录持久化业务。
        /// </summary>
        private ViewModelLocator _viewModelLocator;

        /// <summary>
        /// 关闭流程门闩。操作员确认关闭后保持为 true，避免窗口事件重复保存配置或释放设备连接。
        /// </summary>
        private bool _shutdownStarted;

        /// <summary>
        /// 创建双Y小屏主窗口并绑定共享视图模型。视觉控件由标准主窗口持有，本窗口保持纯电测资源边界。
        /// </summary>
        public DualYMainWindow()
        {
            InitializeComponent();
            ApplyWindowVersionTitle();
            _viewModelLocator = FindResource("Locator") as ViewModelLocator;
        }

        /// <summary>
        /// 加载双Y生产所需的系统配置、过程记录、耐压仪、绝缘仪和 PLC 轮询。
        /// 相机、HALCON、AOI 工程和机器人监听保持未创建状态。
        /// </summary>
        /// <param name="sender">双Y主窗口加载事件来源。</param>
        /// <param name="e">窗口加载事件参数，业务流程不读取附加状态。</param>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            OnlyOnce.check.OnlyOnce();

            _viewModelLocator.Main.BackupConfigXmlsBeforeLoad();
            _viewModelLocator.Main.LoadSettingModel();
            _viewModelLocator.Main.LoadRecordModel();
            _viewModelLocator.Main.LoadProcessmodel();
            DataContext = _viewModelLocator.Main.DataModel;
            ApplyProductInfoRecordsSort();

            _viewModelLocator.Main.InitializeElectricalRuntimeParameters();
            _viewModelLocator.Main.InitAt9620();
            _viewModelLocator.Main.InitAT6835FL();
            _viewModelLocator.Main.PLC_shankhand();
            _viewModelLocator.Main.PLC_Start();
            _viewModelLocator.Main.writeLog("[双Y电测] 已启动纯电测窗口；本进程未创建标准 HALCON、相机和机器人界面资源");
        }

        /// <summary>
        /// 打开系统设置窗口，供设备维护人员调整 PLC、MES、电测仪和扫码器配置。
        /// </summary>
        /// <param name="sender">系统设置按钮。</param>
        /// <param name="e">按钮点击事件参数。</param>
        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            new SettingForm { Owner = this }.ShowDialog();
        }

        /// <summary>
        /// 打开批号参数下发窗口。普通部署与双Y部署共用 MES 查询、参数完整性校验和仪器下发业务。
        /// </summary>
        /// <param name="sender">参数下发按钮。</param>
        /// <param name="e">按钮点击事件参数。</param>
        private void OpenParameterDownload_Click(object sender, RoutedEventArgs e)
        {
            new WOCODEINPUTFORM { Owner = this }.ShowDialog();
        }

        /// <summary>
        /// 执行双Y窗口关闭确认、配置保存和纯电测设备释放。
        /// 原生确认框可在 WPF Closing 阶段稳定显示；确认关闭后启动 20 秒进程退出兜底。
        /// </summary>
        /// <param name="sender">双Y主窗口关闭事件来源。</param>
        /// <param name="e">关闭控制参数；操作员选择继续运行时设置为取消。</param>
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_shutdownStarted)
            {
                return;
            }

            if (MessageBox.Show(
                    this,
                    "是否确定关闭运行软件?",
                    "提示",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            _shutdownStarted = true;
            ShutdownWatchdog.Start(TimeSpan.FromSeconds(20));
            try
            {
                _viewModelLocator.Main.SaveSettingModel();
                _viewModelLocator.Main.SaveRecordModel();
                _viewModelLocator.Main.SaveProcessmodel();
            }
            catch (Exception ex)
            {
                _viewModelLocator.Main.writeLog($"[软件退出] 运行数据保存异常：{ex}", true);
            }
            finally
            {
                _viewModelLocator.Main.ShutdownRuntimeConnections(false);
            }
        }

        /// <summary>
        /// 将双Y产品过程记录按时间倒序显示，便于操作员优先查看最近扫码和电测结果。
        /// 集合本身的持久化顺序保持原业务口径。
        /// </summary>
        private void ApplyProductInfoRecordsSort()
        {
            var records = _viewModelLocator?.Main?.DataModel?.Recordmodel?.ProductInfoRecords;
            if (records == null)
            {
                return;
            }

            var view = CollectionViewSource.GetDefaultView(records);
            view?.SortDescriptions.Clear();
            view?.SortDescriptions.Add(new SortDescription("DateTime", ListSortDirection.Descending));
        }

        /// <summary>
        /// 在双Y窗口标题显示程序集版本，供现场截图、发布包核对和远程诊断使用。
        /// </summary>
        private void ApplyWindowVersionTitle()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var informationalVersion = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                assembly,
                typeof(AssemblyInformationalVersionAttribute));
            string versionText = informationalVersion?.InformationalVersion?.Split('+').FirstOrDefault();
            if (string.IsNullOrWhiteSpace(versionText))
            {
                versionText = assembly.GetName().Version?.ToString();
            }

            if (!string.IsNullOrWhiteSpace(versionText))
            {
                Title = string.Format("{0} v{1}", Title, versionText);
            }
        }
    }
}
