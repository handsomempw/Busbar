using BusbarCompressionSystem.ViewModel;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace BusbarCompressionSystem.View
{
    /// <summary>
    /// 双Y小屏生产视图。该视图提供两工位状态、过程记录和运行日志交互；
    /// 共享 MainViewModel 负责测试执行、PLC 反馈、SQLite 持久化和 MES 报工。
    /// </summary>
    public partial class DualYProductionView : UserControl
    {
        private readonly ViewModelLocator _viewModelLocator;
        private bool _y1ManualScanSubmitting;
        private bool _y2ManualScanSubmitting;

        public DualYProductionView()
        {
            InitializeComponent();
            _viewModelLocator = FindResource("Locator") as ViewModelLocator;
        }

        /// <summary>
        /// 展开或收起指定双Y工位的手动扫码面板。工位号由卡片按钮固定，
        /// 操作员无需在提交前额外选择 Y1/Y2，从交互入口消除写错工位的风险。
        /// </summary>
        /// <param name="sender">带有工位号 Tag 的 Y1/Y2 手动扫码按钮。</param>
        /// <param name="e">按钮点击事件参数，当前流程不读取附加事件数据。</param>
        private void ManualScanToggle_Click(object sender, RoutedEventArgs e)
        {
            int stationIndex = GetStationIndex(sender as FrameworkElement);
            if (stationIndex == 0 || IsManualScanSubmitting(stationIndex))
            {
                return;
            }

            Popup popup = GetManualScanPopup(stationIndex);
            Popup otherPopup = GetManualScanPopup(stationIndex == 1 ? 2 : 1);
            otherPopup.IsOpen = false;
            popup.IsOpen = !popup.IsOpen;
            if (popup.IsOpen)
            {
                FocusManualScanInput(stationIndex, false);
            }
        }

        /// <summary>
        /// 处理手动扫码输入框的键盘操作。Enter 提交当前固定工位，Escape 收起面板；
        /// 输入内容在业务失败时继续保留，便于操作员核对或修正。
        /// </summary>
        /// <param name="sender">带有工位号 Tag 的手动扫码输入框。</param>
        /// <param name="e">键盘事件，Enter 和 Escape 在此消费以避免向父级继续传播。</param>
        private async void ManualScanTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            int stationIndex = GetStationIndex(sender as FrameworkElement);
            if (stationIndex == 0)
            {
                return;
            }

            if (e.Key == Key.Escape)
            {
                GetManualScanPopup(stationIndex).IsOpen = false;
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await SubmitManualScanAsync(stationIndex);
            }
        }

        /// <summary>
        /// 处理手动扫码确认按钮，将输入提交到按钮固定的 Y1 或 Y2 工位业务入口。
        /// </summary>
        /// <param name="sender">带有工位号 Tag 的确认按钮。</param>
        /// <param name="e">按钮点击事件参数，当前流程不读取附加事件数据。</param>
        private async void ManualScanSubmit_Click(object sender, RoutedEventArgs e)
        {
            int stationIndex = GetStationIndex(sender as FrameworkElement);
            if (stationIndex != 0)
            {
                await SubmitManualScanAsync(stationIndex);
            }
        }

        /// <summary>
        /// 在后台线程执行指定工位的手动扫码业务，避免 MES、SQLite 和 PLC 通信阻塞小屏界面。
        /// 业务成功后清空输入；PLC 反馈失败时保留警告面板并提示避免重复扫码。
        /// </summary>
        /// <param name="stationIndex">卡片入口固定的双Y物理工位号，取值 1 或 2。</param>
        /// <returns>表示本次界面提交完成的异步任务。</returns>
        private async Task SubmitManualScanAsync(int stationIndex)
        {
            if (IsManualScanSubmitting(stationIndex))
            {
                return;
            }

            TextBox input = GetManualScanTextBox(stationIndex);
            string scanRaw = (input.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(scanRaw))
            {
                SetManualScanMessage(stationIndex, "请输入产品编号", Brushes.Firebrick);
                FocusManualScanInput(stationIndex, false);
                return;
            }

            MainViewModel mainViewModel = _viewModelLocator?.Main;
            if (mainViewModel == null)
            {
                SetManualScanMessage(stationIndex, "扫码业务尚未就绪", Brushes.Firebrick);
                return;
            }

            SetManualScanSubmitting(stationIndex, true);
            SetManualScanMessage(stationIndex, "正在处理...", Brushes.SlateGray);
            try
            {
                DualYStationScanResult result = await Task.Run(() => mainViewModel.ProcessDualYManualScan(scanRaw, stationIndex));
                if (result.BusinessCompleted)
                {
                    input.Clear();
                }

                if (result.IsSuccess)
                {
                    SetManualScanMessage(stationIndex, string.Empty, Brushes.SlateGray);
                    GetManualScanPopup(stationIndex).IsOpen = false;
                    return;
                }

                Brush messageBrush = result.BusinessCompleted ? Brushes.DarkOrange : Brushes.Firebrick;
                SetManualScanMessage(stationIndex, result.Message, messageBrush);
                FocusManualScanInput(stationIndex, !result.BusinessCompleted);
            }
            catch (Exception ex)
            {
                mainViewModel.writeLog($"[双Y-工位{stationIndex}手动扫码] 界面提交异常：{ex.Message}", true);
                SetManualScanMessage(stationIndex, "手动扫码异常，请查看运行日志", Brushes.Firebrick);
                FocusManualScanInput(stationIndex, true);
            }
            finally
            {
                SetManualScanSubmitting(stationIndex, false);
            }
        }

        private static int GetStationIndex(FrameworkElement element)
        {
            int stationIndex;
            return element != null && int.TryParse(Convert.ToString(element.Tag), out stationIndex)
                ? stationIndex
                : 0;
        }

        private Popup GetManualScanPopup(int stationIndex)
        {
            return stationIndex == 1 ? Y1ManualScanPopup : Y2ManualScanPopup;
        }

        private TextBox GetManualScanTextBox(int stationIndex)
        {
            return stationIndex == 1 ? Y1ManualScanTextBox : Y2ManualScanTextBox;
        }

        private TextBlock GetManualScanMessage(int stationIndex)
        {
            return stationIndex == 1 ? Y1ManualScanMessage : Y2ManualScanMessage;
        }

        private Button GetManualScanToggleButton(int stationIndex)
        {
            return stationIndex == 1 ? Y1ManualScanButton : Y2ManualScanButton;
        }

        private Button GetManualScanSubmitButton(int stationIndex)
        {
            return stationIndex == 1 ? Y1ManualScanSubmitButton : Y2ManualScanSubmitButton;
        }

        private bool IsManualScanSubmitting(int stationIndex)
        {
            return stationIndex == 1 ? _y1ManualScanSubmitting : _y2ManualScanSubmitting;
        }

        private void SetManualScanSubmitting(int stationIndex, bool submitting)
        {
            if (stationIndex == 1)
            {
                _y1ManualScanSubmitting = submitting;
            }
            else
            {
                _y2ManualScanSubmitting = submitting;
            }

            GetManualScanTextBox(stationIndex).IsEnabled = !submitting;
            GetManualScanSubmitButton(stationIndex).IsEnabled = !submitting;
            GetManualScanToggleButton(stationIndex).IsEnabled = !submitting;
        }

        private void SetManualScanMessage(int stationIndex, string message, Brush foreground)
        {
            TextBlock messageBlock = GetManualScanMessage(stationIndex);
            messageBlock.Text = message ?? string.Empty;
            messageBlock.Foreground = foreground;
        }

        private void FocusManualScanInput(int stationIndex, bool selectAll)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                TextBox input = GetManualScanTextBox(stationIndex);
                input.Focus();
                if (selectAll)
                {
                    input.SelectAll();
                }
                else
                {
                    input.CaretIndex = input.Text == null ? 0 : input.Text.Length;
                }
            }), DispatcherPriority.Input);
        }
    }
}
