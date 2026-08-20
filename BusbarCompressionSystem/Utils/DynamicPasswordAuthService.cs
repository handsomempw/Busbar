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
    /// 动态密码授权服务。
    /// 主程序的 AOI 编辑权限固定通过生产认证服务申请和验证；现场 XML 仅承载设备编号、接收人和有效期等业务参数。
    /// 动态密码联调由独立的 <c>AuthenticationTest</c> 开发工具承担，避免发布程序出现可由配置文件改变的认证通道。
    /// </summary>
    public sealed class DynamicPasswordAuthService : IDisposable
    {
        private readonly DynamicPasswordAuthenticationSetting setting;
        private readonly DynamicPassword dynamicPassword;
        private readonly string equipNo;
        private bool disposed;

        /// <summary>
        /// 本次动态密码申请、验证和权限回收共用的审计关联编号。
        /// </summary>
        public string AuditId { get; }

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
            AuditId = DynamicPasswordAuditLogger.CreateAuditId();

            // AOI 编辑权限属于现场受控操作，主程序每次均连接生产认证服务。
            const bool useTestAuthentication = false;
            dynamicPassword = new DynamicPassword(useTestAuthentication);
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

                DynamicPasswordAuditLogger.Write(AuditId, "申请开始", $"应用={applicationName}, 权限等级={privilegeLevel}, 有效期={periodMinutes}分钟, 原因={reason}");

                List<Receiver> result;
                try
                {
                    // 接收人工号不为空时，按现场要求指定管理员；否则走系统自动选择
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

                    DynamicPasswordAuditLogger.Write(AuditId, "申请结果", $"结果={(result.Count > 0 ? "成功" : "未找到接收人")}, 接收人数={result.Count}");
                    return (IReadOnlyList<Receiver>)result;
                }
                catch (OperationCanceledException)
                {
                    DynamicPasswordAuditLogger.Write(AuditId, "申请取消", "操作已取消");
                    throw;
                }
                catch (Exception ex)
                {
                    DynamicPasswordAuditLogger.Write(AuditId, "申请失败", ex.Message);
                    throw;
                }
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

                DynamicPasswordAuditLogger.Write(AuditId, "验证开始", $"应用={applicationName}");
                try
                {
                    // 注意：不要记录或打印动态密码本身（避免泄露）
                    var verifyResult = dynamicPassword.VerifyPassword(equipNo, applicationName, password);

                    // 第三方库返回类型不强依赖：这里用 dynamic 映射必要字段
                    dynamic v = verifyResult;
                    bool ok = v != null && v.VerifyStatus;
                    string message = v != null ? (string)(v.VerifyMessage ?? string.Empty) : "验证失败（无返回结果）";

                    var verifyInfo = new DynamicPasswordVerifyInfo(
                        ok,
                        message,
                        v != null ? (string)(v.AuthorizerName ?? string.Empty) : string.Empty,
                        v != null ? (string)(v.AuthorizerNo ?? string.Empty) : string.Empty,
                        v != null ? (int)v.PrivilegeLevel : 0,
                        v != null ? (string)(v.ApplicationName ?? string.Empty) : applicationName);
                    DynamicPasswordAuditLogger.Write(AuditId, "验证结果", $"结果={(verifyInfo.VerifyStatus ? "成功" : "失败")}, 授权人={verifyInfo.AuthorizerName}({verifyInfo.AuthorizerNo}), 原因={verifyInfo.VerifyMessage}");
                    return verifyInfo;
                }
                catch (OperationCanceledException)
                {
                    DynamicPasswordAuditLogger.Write(AuditId, "验证取消", "操作已取消");
                    throw;
                }
                catch (Exception ex)
                {
                    DynamicPasswordAuditLogger.Write(AuditId, "验证异常", ex.Message);
                    throw;
                }
            }, cancellationToken);
        }

        private void OnPasswordExpired(object sender, EventArgs e)
        {
            DynamicPasswordAuditLogger.Write(AuditId, "密码过期", "认证服务通知授权会话到期");
            // 密码过期后通过事件通知上层，便于自动回收“编辑权限”
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

