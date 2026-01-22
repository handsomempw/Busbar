using BusbarCompressionSystem.Model.Setting1;
using Faratronic.EquipUtils.Authentication;
using Faratronic.EquipUtils.Authentication.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BusbarCompressionSystem.Utils
{
    /// <summary>
    /// 动态密码授权服务（轻量封装）
    ///
    /// 设计目标：
    /// - 将 UI 与第三方认证库解耦（MainWindow 只关心“申请/验证/过期”）
    /// - 统一处理：测试模式/严格模式、指定接收人、异步调用避免卡UI
    /// </summary>
    public sealed class DynamicPasswordAuthService : IDisposable
    {
        private readonly DynamicPasswordAuthenticationSetting setting;
        private readonly DynamicPassword dynamicPassword;
        private readonly string equipNo;
        private bool disposed;

        /// <summary>
        /// 动态密码过期事件（用于自动回收权限）
        /// </summary>
        public event EventHandler PasswordExpired;

        public DynamicPasswordAuthService(DynamicPasswordAuthenticationSetting setting, string equipNo)
        {
            this.setting = setting ?? throw new ArgumentNullException(nameof(setting));

            if (string.IsNullOrWhiteSpace(equipNo))
            {
                throw new ArgumentException("设备编号不能为空（用于动态密码认证）", nameof(equipNo));
            }

            this.equipNo = equipNo;

            // 中文说明：
            // - 先允许“测试模式开关”以便联调
            // - 后期切换为严格模式时，只需要把 StrictMode 置为 true，即可强制走生产模式
            bool testMode = !this.setting.StrictMode && this.setting.TestMode;
            dynamicPassword = new DynamicPassword(testMode);
            dynamicPassword.PasswordExpired += OnPasswordExpired;
        }

        /// <summary>
        /// 申请动态密码（可能自动选择接收人，也可能指定管理员工号）
        /// </summary>
        public Task<IReadOnlyList<Receiver>> RequestPasswordAsync(string reason, CancellationToken cancellationToken)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(DynamicPasswordAuthService));
            }

            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                string applicationName = setting.ApplicationName ?? string.Empty;
                int privilegeLevel = setting.PrivilegeLevel;
                int periodMinutes = setting.PeriodMinutes;

                // 防御性处理：避免因配置异常导致库内部抛出难定位异常
                if (string.IsNullOrWhiteSpace(applicationName))
                {
                    throw new InvalidOperationException("动态密码认证配置：应用名称不能为空");
                }
                if (privilegeLevel <= 0)
                {
                    throw new InvalidOperationException("动态密码认证配置：权限等级必须大于0");
                }
                if (periodMinutes <= 0)
                {
                    throw new InvalidOperationException("动态密码认证配置：有效期分钟必须大于0");
                }

                List<Receiver> result;

                // 中文说明：接收人工号不为空时，按现场要求指定管理员；否则走系统自动选择
                if (!string.IsNullOrWhiteSpace(setting.ReceiverNo))
                {
                    result = dynamicPassword.RequestPasswordManualReceiver(
                        setting.ReceiverNo,
                        equipNo,
                        applicationName,
                        privilegeLevel,
                        periodMinutes,
                        reason) ?? new List<Receiver>();
                }
                else
                {
                    result = dynamicPassword.RequestPassword(
                        equipNo,
                        applicationName,
                        privilegeLevel,
                        periodMinutes,
                        reason) ?? new List<Receiver>();
                }

                return (IReadOnlyList<Receiver>)result;
            }, cancellationToken);
        }

        /// <summary>
        /// 验证动态密码（返回业务友好的结果，避免 UI 直接依赖第三方返回类型）
        /// </summary>
        public Task<DynamicPasswordVerifyInfo> VerifyPasswordAsync(string password, CancellationToken cancellationToken)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(DynamicPasswordAuthService));
            }

            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                string applicationName = setting.ApplicationName ?? string.Empty;
                if (string.IsNullOrWhiteSpace(applicationName))
                {
                    throw new InvalidOperationException("动态密码认证配置：应用名称不能为空");
                }

                // 注意：不要记录或打印动态密码本身（避免泄露）
                var verifyResult = dynamicPassword.VerifyPassword(equipNo, applicationName, password);

                // 第三方库返回类型不强依赖：这里用 dynamic 映射必要字段
                dynamic v = verifyResult;
                bool ok = v != null && v.VerifyStatus;
                string message = v != null ? (string)(v.VerifyMessage ?? string.Empty) : "验证失败（无返回结果）";

                return new DynamicPasswordVerifyInfo(
                    ok,
                    message,
                    v != null ? (string)(v.AuthorizerName ?? string.Empty) : string.Empty,
                    v != null ? (string)(v.AuthorizerNo ?? string.Empty) : string.Empty,
                    v != null ? (int)v.PrivilegeLevel : 0,
                    v != null ? (string)(v.ApplicationName ?? string.Empty) : applicationName);
            }, cancellationToken);
        }

        private void OnPasswordExpired(object sender, EventArgs e)
        {
            // 中文说明：密码过期后通过事件通知上层，便于自动回收“编辑权限”
            PasswordExpired?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            dynamicPassword.PasswordExpired -= OnPasswordExpired;
        }
    }

    /// <summary>
    /// 动态密码验证结果（UI 使用）
    /// </summary>
    public sealed class DynamicPasswordVerifyInfo
    {
        public DynamicPasswordVerifyInfo(
            bool verifyStatus,
            string verifyMessage,
            string authorizerName,
            string authorizerNo,
            int privilegeLevel,
            string applicationName)
        {
            VerifyStatus = verifyStatus;
            VerifyMessage = verifyMessage ?? string.Empty;
            AuthorizerName = authorizerName ?? string.Empty;
            AuthorizerNo = authorizerNo ?? string.Empty;
            PrivilegeLevel = privilegeLevel;
            ApplicationName = applicationName ?? string.Empty;
        }

        public bool VerifyStatus { get; }
        public string VerifyMessage { get; }
        public string AuthorizerName { get; }
        public string AuthorizerNo { get; }
        public int PrivilegeLevel { get; }
        public string ApplicationName { get; }
    }
}

