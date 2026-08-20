using System;
using System.IO;
using System.Text;

namespace BusbarCompressionSystem.Utils
{
    /// <summary>
    /// 动态密码授权与 AOI 参数变更的本地审计落盘入口。
    /// 审计文件按日期写入独立目录；写入异常不改变工程保存、认证和设备流程。
    /// </summary>
    internal static class DynamicPasswordAuditLogger
    {
        private static readonly object SyncRoot = new object();

        /// <summary>
        /// 为一次从申请到权限关闭的授权会话生成关联编号。
        /// </summary>
        internal static string CreateAuditId()
        {
            return Guid.NewGuid().ToString("N");
        }

        /// <summary>
        /// 将动态密码业务事件写入程序目录下的独立审计文件。
        /// 动态密码原文不进入日志，审计文件只记录业务上下文和结果。
        /// </summary>
        /// <param name="auditId">授权会话关联编号，不包含动态密码。</param>
        /// <param name="eventName">事件名称，例如申请、验证、授权开启或参数审计。</param>
        /// <param name="message">可供运维检索的业务摘要；调用方不得传入密码原文。</param>
        internal static void Write(string auditId, string eventName, string message)
        {
            if (string.IsNullOrWhiteSpace(auditId))
            {
                return;
            }

            try
            {
                string fileName = Path.Combine(
                    Environment.CurrentDirectory,
                    "日志",
                    "动态密码审计",
                    DateTime.Now.ToString("yyyyMMdd") + ".txt");
                string directory = Path.GetDirectoryName(fileName);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string line = string.Format(
                    "[{0}][AuditId={1}][{2}] {3}",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    Sanitize(auditId),
                    Sanitize(eventName),
                    Sanitize(message));

                lock (SyncRoot)
                {
                    using (var writer = new StreamWriter(fileName, true, new UTF8Encoding(false)))
                    {
                        writer.WriteLine(line);
                    }
                }
            }
            catch
            {
                // 审计文件属于诊断旁路，写入异常不能阻断现场认证或工程保存。
            }
        }

        private static string Sanitize(string value)
        {
            return (value ?? string.Empty)
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }
    }
}
