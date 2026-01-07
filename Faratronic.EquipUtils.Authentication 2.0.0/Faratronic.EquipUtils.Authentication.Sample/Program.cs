/*
 * 项目名: Faratronic.EquipUtils.Authentication.Sample
 * 描述: 动态密码认证系统示例程序
 *
 * 功能说明:
 * 本文件演示了如何使用Faratronic动态密码认证工具库的主要功能:
 * 1. 动态密码的请求与验证 (RequestPassword/VerifyPassword)
 * 2. 手动指定接收人的密码请求 (RequestPasswordManualReceiver)
 * 3. TOTP时间-based一次性密码验证 (VerifyTOTP)
 * 4. 密码过期事件的处理 (PasswordExpired事件)
 * 5. 操作日志的记录 (OperationLog)
 *
 * API接口说明:
 * - DynamicPassword构造函数支持testMode参数，true时不发送实际请求
 * - RequestPassword自动获取接收人，RequestPasswordManualReceiver手动指定接收人
 * - VerifyPassword验证动态密码，VerifyTOTP验证TOTP令牌
 * - 密码过期通过Timer自动触发PasswordExpired事件
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Faratronic.EquipUtils.Authentication.Models;

namespace Faratronic.EquipUtils.Authentication.Sample
{
    /// <summary>
    /// 动态密码认证示例程序主类
    /// </summary>
    /// <remarks>
    /// 该类展示了Faratronic.EquipUtils.Authentication.DynamicPassword类的完整使用流程：
    /// - 动态密码请求：RequestPassword() - 自动获取接收人
    /// - 手动密码请求：RequestPasswordManualReceiver() - 指定接收人
    /// - 密码验证：VerifyPassword() - 验证动态密码
    /// - TOTP验证：VerifyTOTP() - 验证时间-based一次性密码
    /// - 事件处理：PasswordExpired事件 - 密码过期自动触发
    /// - 操作日志：OperationLog类 - 记录用户操作行为
    ///
    /// 测试模式说明：
    /// 当DynamicPassword构造函数传入testMode=true时：
    /// - RequestPassword返回测试用户数据，不发送实际请求
    /// - VerifyPassword返回模拟验证成功结果
    /// - 便于开发调试和接口对接测试
    /// </remarks>
    internal class Program
    {
        /// <summary>
        /// 程序入口点，演示动态密码认证系统的基本用法
        /// </summary>
        /// <param name="args">命令行参数（本示例中未使用）</param>
        /// <remarks>
        /// 完整的功能演示流程：
        /// 1. 初始化DynamicPassword对象 (testMode=false表示生产模式)
        /// 2. 注册PasswordExpired事件处理程序，处理密码过期逻辑
        /// 3. 调用RequestPassword()请求动态密码：
        ///    - equipNo: 设备编号
        ///    - applicationName: 应用程序名称
        ///    - privilegeLevel: 权限等级(5)
        ///    - period: 密码有效期(60分钟)
        ///    - reason: 请求原因(维修)
        ///    - 返回接收人列表，系统自动选择合适接收人
        /// 4. 调用VerifyPassword()验证用户输入的动态密码
        /// 5. 调用OperationLog.Log()记录用户操作行为到服务器
        ///
        /// 其他可用接口：
        /// - RequestPasswordManualReceiver(): 手动指定接收人工号
        /// - VerifyTOTP(): 验证TOTP时间-based一次性密码
        /// </remarks>
        static void Main(string[] args)
        {
            //初始化DynamicPassword对象，testMode=false表示生产模式，true时返回测试数据不发送实际请求
            DynamicPassword psw = new DynamicPassword(false);

            //注册密码过期事件
            psw.PasswordExpired += OnPasswordExpired;

            //请求动态密码 - RequestPassword() 自动获取接收人并发送密码，启动定时器监控过期
            //参数说明: (设备编号, 应用名称, 权限等级, 有效期分钟, 请求原因)
            //var requestResult = psw.RequestPasswordManualReceiver("H10009868","E11DMJ000500", 5, 60 ,"维修");
            var requestResult = psw.RequestPassword("E11DMJ000500","ApplicationName" , 5, 60 ,"维修");
            //返回List<Receiver> - 接收人列表
            //requestResult[i].ReceiverNo 接收人工号
            //requestResult[i].ReceiverName 接收人姓名
            foreach (var receiver in requestResult)
            {
                Console.WriteLine(String.Format("{0};{1}",receiver.ReceiverName,receiver.ReceiverNo));
            }

            Console.ReadLine();

            //验证动态密码 - VerifyPassword() 验证用户输入的6位动态密码
            //参数说明: (设备编号, 应用名称, 用户输入的密码)
            var verifyResult = psw.VerifyPassword("E11DMJ000500", "ApplicationName", "937345");
            Console.WriteLine(String.Format("{0}/{1}/{2}/{3}/{4}/{5}",
                verifyResult.AuthorizerName,    // 许可用户名称
                verifyResult.AuthorizerNo,      // 许可用户工号
                verifyResult.PrivilegeLevel,    // 权限等级
                verifyResult.VerifyMessage,     // 验证消息("PASS"/错误信息)
                verifyResult.VerifyStatus,      // 验证状态(true/false)
                verifyResult.ApplicationName)); // 程序名称

            //记录用户操作日志 - OperationLog.Log() 将操作行为发送到服务器进行审计
            //testMode=false表示生产模式，true时不发送实际请求用于测试
            OperationLog opLog = new OperationLog(false);
            var result = opLog.Log(new OperationRecord()
            {
                AuthorizerName = "张彬",
                AuthorizerNo = "H10000000",
                EquipNo = "E11DMJ000500",
                OperateEndTime = DateTime.Now,
                OperateStartTime = DateTime.Now.AddMinutes(-10),
                Datas = new List<OperationData>()
                {
                    new OperationData()
                    {
                        DataName = "设备编号",
                        DataNewValue = "新值",
                        DataOldValue = "旧值",
                        DataType = "数据类型"
                    }
                }
            });
        }

        /// <summary>
        /// 密码过期事件处理方法
        /// </summary>
        /// <param name="sender">事件发送者对象</param>
        /// <param name="e">事件参数</param>
        /// <remarks>
        /// 当动态密码过期时，DynamicPassword内部的Timer会自动触发此事件处理方法。
        /// 密码过期时间由RequestPassword()方法的period参数决定（单位：分钟）。
        /// 您可以在此方法中添加密码过期后需要执行的自定义操作，例如：
        /// - 清除用户的操作权限
        /// - 显示密码过期提示信息
        /// - 记录密码过期日志
        /// - 锁定相关功能界面
        /// - 停止相关自动化操作
        /// - 通知相关人员密码已过期
        /// </remarks>
        private static void OnPasswordExpired(object sender, EventArgs e)
        {
            //在此处增加密码到期后所需要执行的动作
            Console.WriteLine("Password Expired.");
            Console.WriteLine(DateTime.Now);
        }
    }
}
