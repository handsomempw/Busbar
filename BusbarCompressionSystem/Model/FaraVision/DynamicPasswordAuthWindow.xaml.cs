using BusbarCompressionSystem.Utils;
using Faratronic.EquipUtils.Authentication.Models;
using Panuon.WPF.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace BusbarCompressionSystem.Model.FaraVision
{
    /// <summary>
    /// 动态密码授权窗口
    /// </summary>
    public partial class DynamicPasswordAuthWindow : WindowX
    {
        private readonly DynamicPasswordAuthService authService;
        private readonly CancellationToken cancellationToken;
        private readonly string reason;

        private IReadOnlyList<Receiver> requestedReceivers = Array.Empty<Receiver>();

        /// <summary>
        /// 本次申请返回的接收人列表（用于在主界面提示/记录）
        /// </summary>
        public IReadOnlyList<Receiver> RequestedReceivers => requestedReceivers;

        /// <summary>
        /// 验证通过后的结果信息
        /// </summary>
        public DynamicPasswordVerifyInfo VerifyInfo { get; private set; }

        public DynamicPasswordAuthWindow(
            DynamicPasswordAuthService authService,
            string reason,
            CancellationToken cancellationToken)
        {
            this.authService = authService ?? throw new ArgumentNullException(nameof(authService));
            this.reason = string.IsNullOrWhiteSpace(reason) ? "授权操作" : reason;
            this.cancellationToken = cancellationToken;

            InitializeComponent();
            reasonTextBlock.Text = this.reason;
        }

        private async void RequestButton_Click(object sender, RoutedEventArgs e)
        {
            requestButton.IsEnabled = false;
            try
            {
                requestStatusTextBlock.Text = "正在申请动态密码...";
                receiverListBox.ItemsSource = null;
                requestedReceivers = Array.Empty<Receiver>();

                var receivers = await authService.RequestPasswordAsync(reason, cancellationToken);
                requestedReceivers = receivers ?? Array.Empty<Receiver>();

                if (requestedReceivers.Count == 0)
                {
                    requestStatusTextBlock.Text = "未找到可审批管理员（申请失败）";
                    NoticeBox.Show("未找到可审批管理员，请检查认证服务状态或改用“接收人工号”指定管理员", "提示", MessageBoxIcon.Warning, true, 6000);
                    return;
                }

                var receiverLines = requestedReceivers
                    .Select(r => $"{r.ReceiverName} ({r.ReceiverNo})")
                    .ToList();
                receiverListBox.ItemsSource = receiverLines;

                requestStatusTextBlock.Text = $"申请成功，已发送给 {requestedReceivers.Count} 人";

                // 弹框告知“已发送给谁”，方便操作者知道该联系谁要密码
                string receiverTip = requestedReceivers.Count == 1
                    ? receiverLines[0]
                    : $"{receiverLines[0]} 等 {requestedReceivers.Count} 人";
                NoticeBox.Show($"动态密码已发送给：{receiverTip}", "提示", MessageBoxIcon.Info, true, 6000);
            }
            catch (OperationCanceledException)
            {
                requestStatusTextBlock.Text = "申请已取消";
            }
            catch (Exception ex)
            {
                requestStatusTextBlock.Text = "申请失败";
                NoticeBox.Show($"申请动态密码失败：{ex.Message}", "错误", MessageBoxIcon.Error, true, 6000);
            }
            finally
            {
                requestButton.IsEnabled = true;
            }
        }

        private async void VerifyButton_Click(object sender, RoutedEventArgs e)
        {
            verifyButton.IsEnabled = false;
            try
            {
                if (requestedReceivers == null || requestedReceivers.Count == 0)
                {
                    NoticeBox.Show("请先点击“申请密码”发送动态密码", "提示", MessageBoxIcon.Warning, true, 5000);
                    return;
                }

                string value = (passwordBox.Password ?? string.Empty).Trim();
                if (value.Length != 6 || value.Any(c => c < '0' || c > '9'))
                {
                    NoticeBox.Show("动态密码格式不正确（应为6位数字）", "提示", MessageBoxIcon.Warning, true, 5000);
                    return;
                }

                requestStatusTextBlock.Text = "正在验证动态密码...";
                var verifyInfo = await authService.VerifyPasswordAsync(value, cancellationToken);
                if (verifyInfo == null || !verifyInfo.VerifyStatus)
                {
                    string msg = verifyInfo?.VerifyMessage ?? "验证失败";
                    requestStatusTextBlock.Text = $"验证失败：{msg}";
                    NoticeBox.Show($"动态密码验证失败：{msg}", "提示", MessageBoxIcon.Warning, true, 6000);
                    return;
                }

                VerifyInfo = verifyInfo;
                requestStatusTextBlock.Text = "验证通过";
                this.DialogResult = true;
            }
            catch (OperationCanceledException)
            {
                requestStatusTextBlock.Text = "验证已取消";
            }
            catch (Exception ex)
            {
                requestStatusTextBlock.Text = "验证异常";
                NoticeBox.Show($"验证动态密码失败：{ex.Message}", "错误", MessageBoxIcon.Error, true, 6000);
            }
            finally
            {
                verifyButton.IsEnabled = true;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }
    }
}

