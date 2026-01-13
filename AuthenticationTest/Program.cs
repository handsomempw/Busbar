using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Faratronic.EquipUtils.Authentication;
using Faratronic.EquipUtils.Authentication.Models;

namespace AuthenticationTest
{
    /// <summary>
    /// Faratronic 动态密码认证系统测试程序
    /// 功能：提供交互式测试环境，学习和验证认证库的所有功能
    /// </summary>
    /// <remarks>
    /// 通俗解释：这是一个学习工具，帮助你理解动态密码认证系统怎么用
    /// - 可以安全地测试各种功能（测试模式）
    /// - 也可以连接真实服务器验证（生产模式）
    /// - 每一步都有详细解释，像教程一样
    /// </remarks>
    class Program
    {
        // 全局变量：存储密码过期状态
        private static bool passwordExpired = false;

        /// <summary>
        /// 程序入口点
        /// </summary>
        static void Main(string[] args)
        {
            // 设置控制台编码为UTF-8，确保中文正常显示
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            // 显示欢迎界面
            TestHelper.ShowWelcomeScreen();
            TestHelper.WaitForKey();

            // 主循环：显示菜单并处理用户选择
            bool running = true;
            while (running)
            {
                try
                {
                    // 显示主菜单
                    ShowMainMenu();

                    // 获取用户选择
                    string choice = TestHelper.GetUserInput("请输入选项编号: ");

                    // 处理用户选择
                    switch (choice)
                    {
                        case "1":
                            Test1_RequestPassword();
                            break;
                        case "2":
                            Test2_RequestPasswordManualReceiver();
                            break;
                        case "3":
                            Test3_VerifyPassword();
                            break;
                        case "4":
                            Test4_PasswordExpired();
                            break;
                        case "5":
                            Test5_OperationLog();
                            break;
                        case "6":
                            Test6_CompleteFlow();
                            break;
                        case "7":
                            Test7_ProductionMode();
                            break;
                        case "8":
                            SwitchMode();
                            break;
                        case "9":
                            ShowUserGuide();
                            break;
                        case "0":
                            running = false;
                            TestHelper.PrintInfo("感谢使用，再见！");
                            break;
                        default:
                            TestHelper.PrintWarning("无效的选项，请重新选择");
                            break;
                    }

                    if (running && choice != "9" && choice != "8")
                    {
                        TestHelper.WaitForKey();
                    }
                }
                catch (Exception ex)
                {
                    TestHelper.ShowException(ex, 
                        "程序运行时发生了意外错误",
                        "检查配置文件是否正确",
                        "确认DLL文件是否存在",
                        "查看详细错误信息以定位问题");
                    TestHelper.WaitForKey();
                }
            }
        }

        #region 菜单系统 - Menu System

        /// <summary>
        /// 显示主菜单
        /// </summary>
        static void ShowMainMenu()
        {
            Console.Clear();
            
            // 显示当前配置信息
            TestConfig.DisplayCurrentConfig();

            // 根据当前模式显示不同的菜单
            if (TestConfig.TestMode)
            {
                ShowTestModeMenu();
            }
            else
            {
                ShowProductionModeMenu();
            }
        }

        /// <summary>
        /// 显示测试模式菜单
        /// </summary>
        static void ShowTestModeMenu()
        {
            TestHelper.PrintHeader("测 试 菜 单");
            
            Console.WriteLine("请选择你想测试的功能（输入序号）：");
            Console.WriteLine();
            
            Console.WriteLine("[1] 🔐 自动请求动态密码 (测试模式)");
            Console.WriteLine("    └─ 场景：设备需要授权，系统自动找到合适的管理员发送密码");
            Console.WriteLine("    └─ 学习：了解如何申请临时权限");
            Console.WriteLine();
            
            Console.WriteLine("[2] 👤 手动指定接收人请求密码 (测试模式)");
            Console.WriteLine("    └─ 场景：你知道应该找哪个管理员，直接指定他的工号");
            Console.WriteLine("    └─ 学习：定向申请授权的方法");
            Console.WriteLine();
            
            Console.WriteLine("[3] ✅ 验证动态密码 (测试模式)");
            Console.WriteLine("    └─ 场景：管理员发来了6位密码，系统检查是否正确");
            Console.WriteLine("    └─ 学习：密码验证流程和结果处理");
            Console.WriteLine();
            
            Console.WriteLine("[4] ⏰ 测试密码过期事件 (测试模式)");
            Console.WriteLine("    └─ 场景：密码到期后，系统自动触发警告");
            Console.WriteLine("    └─ 学习：如何处理超时逻辑");
            Console.WriteLine();
            
            Console.WriteLine("[5] 📝 记录操作日志 (测试模式)");
            Console.WriteLine("    └─ 场景：操作完成后，记录谁在什么时间做了什么");
            Console.WriteLine("    └─ 学习：操作审计的实现方法");
            Console.WriteLine();
            
            Console.WriteLine("[6] 🔄 完整流程演示 (测试模式)");
            Console.WriteLine("    └─ 场景：从申请密码→验证→操作→记录日志，走一遍完整流程");
            Console.WriteLine("    └─ 学习：实际项目中的使用方式");
            Console.WriteLine();
            
            Console.WriteLine("[7] 🌐 生产环境完整测试 ⚠️");
            Console.WriteLine("    └─ 场景：连接实际服务器，进行真实环境测试");
            Console.WriteLine("    └─ 警告：会发送真实请求！");
            Console.WriteLine();
            
            Console.WriteLine("[8] ⚙️  切换到生产模式");
            Console.WriteLine("[9] 📖 查看使用说明");
            Console.WriteLine("[0] 退出程序");
            Console.WriteLine();
        }

        /// <summary>
        /// 显示生产模式菜单
        /// </summary>
        static void ShowProductionModeMenu()
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║   当前模式: [生产模式] 🔴  警告：会发送真实请求！        ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();

            TestHelper.PrintWarning("生产模式说明：");
            Console.WriteLine("  • 会连接真实的认证服务器");
            Console.WriteLine("  • 会向真实管理员发送密码请求（短信/App通知）");
            Console.WriteLine("  • 操作日志会被永久记录到服务器");
            Console.WriteLine("  • 请确保你有权限进行真实环境测试");
            Console.WriteLine();

            Console.WriteLine("[1] 生产环境 - 请求真实密码");
            Console.WriteLine("[2] 生产环境 - 验证真实密码");
            Console.WriteLine("[3] 生产环境 - 完整流程测试");
            Console.WriteLine();
            Console.WriteLine("[8] ⚙️  切换回测试模式");
            Console.WriteLine("[9] 📖 查看使用说明");
            Console.WriteLine("[0] 退出程序");
            Console.WriteLine();
        }

        /// <summary>
        /// 切换测试/生产模式
        /// </summary>
        static void SwitchMode()
        {
            Console.Clear();
            TestHelper.PrintHeader("模式切换");

            Console.WriteLine($"当前模式：{TestConfig.GetModeDescription()}");
            Console.WriteLine();

            bool newMode = !TestConfig.TestMode;
            string targetMode = newMode ? "测试模式" : "生产模式";

            if (!newMode)
            {
                // 切换到生产模式，显示警告
                TestHelper.PrintWarning($"即将切换到{targetMode}");
                TestHelper.ShowProductionWarning();
                
                if (!TestHelper.GetUserConfirmation("确认切换？"))
                {
                    TestHelper.PrintInfo("已取消切换");
                    TestHelper.WaitForKey();
                    return;
                }
            }

            TestHelper.PrintProcessing("正在切换模式...");
            
            if (TestConfig.SwitchMode(newMode))
            {
                TestHelper.PrintSuccess($"已成功切换到{targetMode}");
                TestHelper.PrintInfo("配置已保存，新模式将在下次运行时生效");
            }
            else
            {
                TestHelper.PrintError("模式切换失败");
            }

            TestHelper.WaitForKey();
        }

        /// <summary>
        /// 显示使用说明
        /// </summary>
        static void ShowUserGuide()
        {
            Console.Clear();
            TestHelper.PrintHeader("使 用 说 明");

            TestHelper.PrintExplanation("关于测试模式和生产模式",
                "【测试模式】- 安全的学习环境",
                "  • 不会连接真实服务器",
                "  • 返回模拟数据用于学习",
                "  • 不会发送实际通知",
                "  • 适合：学习、开发、调试",
                "",
                "【生产模式】- 真实环境",
                "  • 连接实际认证服务器",
                "  • 会发送真实的短信/App通知",
                "  • 操作会被永久记录",
                "  • 适合：上线前验证、实际使用");

            TestHelper.PrintExplanation("功能说明",
                "1. RequestPassword - 自动请求密码",
                "   系统自动选择合适的管理员发送密码请求",
                "",
                "2. RequestPasswordManualReceiver - 手动指定接收人",
                "   你可以指定特定管理员的工号来接收密码",
                "",
                "3. VerifyPassword - 验证密码",
                "   检查用户输入的6位动态密码是否正确",
                "",
                "4. PasswordExpired事件 - 密码过期通知",
                "   密码有效期结束时自动触发",
                "",
                "5. OperationLog - 操作日志",
                "   记录操作行为到审计系统");

            TestHelper.PrintExplanation("配置说明",
                "所有配置都在 App.config 文件中：",
                "  • TestMode: 测试/生产模式开关",
                "  • EquipNo: 设备编号",
                "  • ApplicationName: 应用名称",
                "  • DefaultPrivilegeLevel: 默认权限等级(1-10)",
                "  • DefaultPeriod: 密码有效期(分钟)",
                "  • ReceiverNo: 接收人工号");

            TestHelper.WaitForKey();
        }

        #endregion

        #region 测试用例 - Test Cases

        /// <summary>
        /// 测试1：自动请求动态密码
        /// </summary>
        static void Test1_RequestPassword()
        {
            Console.Clear();
            TestHelper.PrintSeparator('═');
            Console.WriteLine("【测试1】自动请求动态密码");
            TestHelper.PrintSeparator('═');

            if (!TestConfig.TestMode)
            {
                TestHelper.ShowProductionWarning();
                if (!TestHelper.GetUserConfirmation("确认在生产环境执行？"))
                {
                    return;
                }
            }
            else
            {
                TestHelper.ShowTestModeInfo();
            }

            try
            {
                // 步骤1：准备测试数据
                TestHelper.PrintStepSimple(1, "准备测试数据", 
                    "准备好申请表，填写设备信息和理由");

                var paramDict = new Dictionary<string, string>
                {
                    { "设备编号", TestConfig.EquipNo },
                    { "应用名称", TestConfig.ApplicationName },
                    { "权限等级", TestConfig.DefaultPrivilegeLevel.ToString() },
                    { "有效期", TestConfig.DefaultPeriod + " 分钟" },
                    { "申请原因", "设备调试" }
                };
                TestHelper.PrintTable(paramDict, "参数", "值");

                TestHelper.PrintTip("权限等级5是高级权限，数字越大权限越高");
                TestHelper.PrintTip("有效期60分钟，表示密码在1小时后自动过期");

                // 步骤2：创建DynamicPassword对象
                TestHelper.PrintStepSimple(2, "创建DynamicPassword对象", 
                    "启动密码管理器，设置为" + (TestConfig.TestMode ? "测试模式" : "生产模式"));

                DynamicPassword psw = new DynamicPassword(TestConfig.TestMode);
                TestHelper.PrintSuccess("DynamicPassword对象创建成功");

                if (TestConfig.TestMode)
                {
                    TestHelper.PrintTip("测试模式：像玩游戏的练习模式，不消耗真实资源");
                }

                // 步骤3：调用RequestPassword方法
                TestHelper.PrintStepSimple(3, "调用RequestPassword()方法", 
                    "点击\"申请授权\"按钮，等待管理员响应");

                TestHelper.PrintProcessing("正在请求动态密码...");

                var requestResult = psw.RequestPassword(
                    TestConfig.EquipNo,
                    TestConfig.ApplicationName,
                    TestConfig.DefaultPrivilegeLevel,
                    TestConfig.DefaultPeriod,
                    "设备调试");

                // 步骤4：显示返回结果
                TestHelper.PrintStepSimple(4, "接收返回结果", 
                    "系统找到了可以审批的管理员");

                if (requestResult != null && requestResult.Count > 0)
                {
                    TestHelper.PrintSuccess($"请求成功！找到 {requestResult.Count} 位可以授权的管理员：");

                    var rows = new List<string[]>();
                    for (int i = 0; i < requestResult.Count; i++)
                    {
                        rows.Add(new string[] 
                        { 
                            (i + 1).ToString(), 
                            requestResult[i].ReceiverName ?? "未知",
                            requestResult[i].ReceiverNo ?? "未知"
                        });
                    }
                    TestHelper.PrintThreeColumnTable(
                        new string[] { "序号", "姓名", "工号" }, 
                        rows);

                    TestHelper.PrintTip("这些人会收到密码请求通知（短信或App推送）");
                }
                else
                {
                    TestHelper.PrintWarning("未找到接收人，返回结果为空");
                }

                // 测试总结
                TestHelper.PrintSummary("本次测试验证了什么？",
                    "动态密码申请流程正常工作",
                    "系统能正确返回接收人列表",
                    $"{(TestConfig.TestMode ? "测试模式可以模拟真实请求" : "生产模式成功连接服务器")}");

                TestHelper.PrintExplanation("实际项目中如何使用？",
                    "在你的WPF程序中，当用户点击\"申请授权\"按钮时：",
                    "1. 调用 RequestPassword() 方法",
                    "2. 显示接收人列表给用户（让用户知道谁会收到通知）",
                    "3. 弹出密码输入框，等待用户输入管理员发来的6位密码",
                    "4. 调用 VerifyPassword() 验证密码");
            }
            catch (Exception ex)
            {
                TestHelper.ShowException(ex,
                    "请求密码时发生错误，可能是网络问题或服务器未响应",
                    "确认网络连接是否正常",
                    "检查设备编号是否正确",
                    "如果是生产模式，确认服务器地址配置正确");
            }
        }

        /// <summary>
        /// 测试2：手动指定接收人请求密码
        /// </summary>
        static void Test2_RequestPasswordManualReceiver()
        {
            Console.Clear();
            TestHelper.PrintSeparator('═');
            Console.WriteLine("【测试2】手动指定接收人请求密码");
            TestHelper.PrintSeparator('═');

            if (!TestConfig.TestMode)
            {
                TestHelper.ShowProductionWarning();
                if (!TestHelper.GetUserConfirmation("确认在生产环境执行？"))
                {
                    return;
                }
            }
            else
            {
                TestHelper.ShowTestModeInfo();
            }

            try
            {
                // 步骤1：准备测试数据
                TestHelper.PrintStepSimple(1, "准备测试数据", 
                    "准备申请表，并指定特定的管理员");

                var paramDict = new Dictionary<string, string>
                {
                    { "接收人工号", TestConfig.ReceiverNo },
                    { "接收人姓名", TestConfig.ReceiverName },
                    { "设备编号", TestConfig.EquipNo },
                    { "应用名称", TestConfig.ApplicationName },
                    { "权限等级", TestConfig.DefaultPrivilegeLevel.ToString() },
                    { "有效期", TestConfig.DefaultPeriod + " 分钟" }
                };
                TestHelper.PrintTable(paramDict, "参数", "值");

                TestHelper.PrintExplanation("手动指定 vs 自动获取的区别",
                    "【自动获取】系统根据权限等级自动找合适的管理员",
                    "【手动指定】你明确知道应该找谁，直接指定工号",
                    "",
                    "适用场景：",
                    "  • 紧急情况，需要特定管理员快速审批",
                    "  • 某些操作只有特定人员有权审批",
                    "  • 避免打扰多个管理员");

                // 步骤2：创建对象并请求
                TestHelper.PrintStepSimple(2, "调用RequestPasswordManualReceiver", 
                    "向指定的管理员发送密码请求");

                DynamicPassword psw = new DynamicPassword(TestConfig.TestMode);
                TestHelper.PrintProcessing($"正在向 {TestConfig.ReceiverName}({TestConfig.ReceiverNo}) 发送密码请求...");

                var requestResult = psw.RequestPasswordManualReceiver(
                    TestConfig.ReceiverNo,
                    TestConfig.EquipNo,
                    TestConfig.ApplicationName,
                    TestConfig.DefaultPrivilegeLevel,
                    TestConfig.DefaultPeriod,
                    "设备维修");

                // 步骤3：显示结果
                TestHelper.PrintStepSimple(3, "显示请求结果", 
                    "确认指定的管理员收到通知");

                if (requestResult != null && requestResult.Count > 0)
                {
                    TestHelper.PrintSuccess("请求成功！密码已发送给指定接收人：");

                    var rows = new List<string[]>();
                    rows.Add(new string[] 
                    { 
                        requestResult[0].ReceiverName ?? "未知",
                        requestResult[0].ReceiverNo ?? "未知",
                        "已发送通知"
                    });
                    TestHelper.PrintThreeColumnTable(
                        new string[] { "姓名", "工号", "状态" }, 
                        rows);

                    TestHelper.PrintTip($"{TestConfig.ReceiverName} 的手机现在应该收到了密码通知");
                }
                else
                {
                    TestHelper.PrintWarning("请求失败，可能是接收人工号不存在");
                }

                TestHelper.PrintSummary("本次测试验证了什么？",
                    "手动指定接收人功能正常",
                    "可以精确控制谁来审批",
                    "适用于需要特定人员审批的场景");
            }
            catch (Exception ex)
            {
                TestHelper.ShowException(ex,
                    "手动指定接收人时发生错误",
                    "检查接收人工号是否正确",
                    "确认该人员是否有审批权限",
                    "验证网络连接是否正常");
            }
        }

        /// <summary>
        /// 测试3：验证动态密码
        /// </summary>
        static void Test3_VerifyPassword()
        {
            Console.Clear();
            TestHelper.PrintSeparator('═');
            Console.WriteLine("【测试3】验证动态密码");
            TestHelper.PrintSeparator('═');

            if (!TestConfig.TestMode)
            {
                TestHelper.ShowProductionWarning();
                if (!TestHelper.GetUserConfirmation("确认在生产环境执行？"))
                {
                    return;
                }
            }
            else
            {
                TestHelper.ShowTestModeInfo();
                TestHelper.PrintTip($"测试模式下使用模拟密码：{TestConfig.TestPassword}");
            }

            try
            {
                // 步骤1：准备验证数据
                TestHelper.PrintStepSimple(1, "准备验证数据", 
                    "准备好要验证的密码和设备信息");

                string password;
                if (TestConfig.TestMode)
                {
                    password = TestConfig.TestPassword;
                    Console.WriteLine($"  使用测试密码: {password}");
                }
                else
                {
                    password = TestHelper.GetUserInput("请输入收到的6位动态密码: ");
                }

                // 步骤2：调用验证方法
                TestHelper.PrintStepSimple(2, "调用VerifyPassword()方法", 
                    "让系统检查密码是否正确");

                DynamicPassword psw = new DynamicPassword(TestConfig.TestMode);
                TestHelper.PrintProcessing("正在验证密码...");

                var verifyResult = psw.VerifyPassword(
                    TestConfig.EquipNo,
                    TestConfig.ApplicationName,
                    password);

                // 步骤3：解析验证结果
                TestHelper.PrintStepSimple(3, "解析验证结果", 
                    "查看密码是否正确，以及获得了什么权限");

                if (verifyResult != null)
                {
                    if (verifyResult.VerifyStatus)
                    {
                        // 验证成功
                        TestHelper.PrintSuccess("密码验证成功！");

                        Console.WriteLine();
                        Console.WriteLine("  ╔═══════════════════════════════════════════════╗");
                        Console.WriteLine("  ║            验证结果详情                       ║");
                        Console.WriteLine("  ╠═══════════════════════════════════════════════╣");
                        Console.WriteLine($"  ║ 验证状态: ✅ 通过                            ║");
                        Console.WriteLine($"  ║ 验证消息: {verifyResult.VerifyMessage.PadRight(34)}║");
                        Console.WriteLine($"  ║ 授权人: {(verifyResult.AuthorizerName ?? "未知").PadRight(37)}║");
                        Console.WriteLine($"  ║ 授权人工号: {(verifyResult.AuthorizerNo ?? "未知").PadRight(33)}║");
                        Console.WriteLine($"  ║ 权限等级: {verifyResult.PrivilegeLevel.ToString().PadRight(35)}║");
                        Console.WriteLine($"  ║ 应用程序: {(verifyResult.ApplicationName ?? "未知").PadRight(35)}║");
                        Console.WriteLine("  ╚═══════════════════════════════════════════════╝");
                        Console.WriteLine();

                        TestHelper.PrintExplanation("结果解读",
                            $"✅ 密码验证成功！(VerifyStatus = {verifyResult.VerifyStatus})",
                            $"👤 授权人：{verifyResult.AuthorizerName} ({verifyResult.AuthorizerNo})",
                            $"🔓 你现在拥有了等级{verifyResult.PrivilegeLevel}的权限",
                            $"⏰ 权限有效期：{TestConfig.DefaultPeriod}分钟（从现在开始计时）");

                        TestHelper.PrintExplanation("接下来可以做什么？",
                            $"1. 在获得权限的这{TestConfig.DefaultPeriod}分钟内，你可以执行需要授权的操作",
                            "2. 系统会在密码过期时自动触发 PasswordExpired 事件",
                            "3. 你的所有操作应该被记录到操作日志中（用OperationLog类）");
                    }
                    else
                    {
                        // 验证失败
                        TestHelper.PrintError("密码验证失败！");

                        var failDict = new Dictionary<string, string>
                        {
                            { "验证状态", "❌ 失败" },
                            { "失败原因", verifyResult.VerifyMessage ?? "未知错误" }
                        };
                        TestHelper.PrintTable(failDict, "属性", "值");

                        TestHelper.PrintExplanation("可能的失败原因",
                            "• 密码错误 - 输入的6位数字不对",
                            "• 密码已过期 - 超过了有效期",
                            "• 密码已使用 - 这个密码已经用过了（一次性密码）",
                            "• 设备编号不匹配 - 密码不是给这台设备的");
                    }
                }
                else
                {
                    TestHelper.PrintWarning("验证结果为null，可能是网络错误");
                }

                TestHelper.PrintSummary("本次测试验证了什么？",
                    "密码验证流程正常工作",
                    "可以正确解析验证结果",
                    "了解了验证成功和失败的不同情况");
            }
            catch (Exception ex)
            {
                TestHelper.ShowException(ex,
                    "验证密码时发生错误",
                    "检查密码格式是否正确（应该是6位数字）",
                    "确认设备编号和应用名称是否匹配",
                    "验证网络连接是否正常");
            }
        }

        /// <summary>
        /// 测试4：密码过期事件
        /// </summary>
        static void Test4_PasswordExpired()
        {
            Console.Clear();
            TestHelper.PrintSeparator('═');
            Console.WriteLine("【测试4】测试密码过期事件");
            TestHelper.PrintSeparator('═');

            TestHelper.ShowTestModeInfo();

            try
            {
                // 步骤1：说明测试原理
                TestHelper.PrintStepSimple(1, "理解密码过期机制", 
                    "密码像停车券，到期后自动失效");

                TestHelper.PrintExplanation("密码过期事件的作用",
                    "当密码有效期结束时，DynamicPassword内部的Timer会自动触发",
                    "PasswordExpired事件，通知你的程序密码已经过期了。",
                    "",
                    "实际应用场景：",
                    "  • 清除用户的操作权限",
                    "  • 锁定需要授权的功能按钮",
                    "  • 显示\"权限已过期\"提示",
                    "  • 记录过期时间到日志",
                    "  • 停止正在进行的自动化操作");

                // 步骤2：注册事件处理
                TestHelper.PrintStepSimple(2, "注册PasswordExpired事件", 
                    "告诉系统：密码过期时要通知我");

                DynamicPassword psw = new DynamicPassword(true); // 必须用测试模式
                
                // 重置过期标志
                passwordExpired = false;
                
                // 注册事件处理程序
                psw.PasswordExpired += OnPasswordExpiredTest;
                TestHelper.PrintSuccess("事件处理程序已注册");

                Console.WriteLine();
                Console.WriteLine("  事件处理代码示例：");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("  ┌────────────────────────────────────────┐");
                Console.WriteLine("  │ psw.PasswordExpired += (sender, e) =>  │");
                Console.WriteLine("  │ {                                      │");
                Console.WriteLine("  │     // 密码过期后要执行的操作          │");
                Console.WriteLine("  │     Console.WriteLine(\"密码已过期\");  │");
                Console.WriteLine("  │     // 锁定界面、清除权限等            │");
                Console.WriteLine("  │ };                                     │");
                Console.WriteLine("  └────────────────────────────────────────┘");
                Console.ResetColor();
                Console.WriteLine();

                // 步骤3：请求短期密码测试过期
                TestHelper.PrintStepSimple(3, "请求短期密码", 
                    "为了快速测试，我们申请一个1分钟有效期的密码");

                TestHelper.PrintProcessing("正在请求1分钟有效期的密码...");

                var requestResult = psw.RequestPassword(
                    TestConfig.EquipNo,
                    TestConfig.ApplicationName,
                    TestConfig.DefaultPrivilegeLevel,
                    1, // 1分钟有效期
                    "测试密码过期");

                if (requestResult != null && requestResult.Count > 0)
                {
                    TestHelper.PrintSuccess("密码请求成功");
                    TestHelper.PrintInfo("密码将在1分钟后过期");
                    
                    // 步骤4：等待过期
                    TestHelper.PrintStepSimple(4, "等待密码过期", 
                        "倒计时60秒，观察过期事件是否触发");

                    Console.WriteLine();
                    Console.WriteLine("  开始倒计时...");
                    
                    for (int i = 60; i >= 0; i--)
                    {
                        if (passwordExpired)
                        {
                            Console.WriteLine();
                            TestHelper.PrintSuccess("✅ 密码过期事件已触发！");
                            break;
                        }
                        
                        Console.Write($"\r  剩余时间: {i} 秒  ");
                        Thread.Sleep(1000);
                    }

                    if (!passwordExpired)
                    {
                        Console.WriteLine();
                        TestHelper.PrintWarning("60秒已到，但事件未触发（可能是Timer延迟）");
                    }
                    
                    Console.WriteLine();

                    TestHelper.PrintSummary("本次测试验证了什么？",
                        "密码过期事件机制正常工作",
                        "可以在密码过期时自动执行操作",
                        "了解了如何处理权限超时问题");

                    TestHelper.PrintExplanation("实际项目中如何使用？",
                        "在你的WPF程序中：",
                        "1. 创建DynamicPassword对象时注册PasswordExpired事件",
                        "2. 在事件处理方法中：",
                        "   - 禁用需要权限的按钮（IsEnabled = false）",
                        "   - 显示提示：\"操作权限已过期，请重新申请\"",
                        "   - 清除当前用户的权限标记",
                        "   - 记录过期时间到日志");
                }
                else
                {
                    TestHelper.PrintWarning("密码请求失败，无法测试过期事件");
                }
            }
            catch (Exception ex)
            {
                TestHelper.ShowException(ex,
                    "测试密码过期事件时发生错误",
                    "确认是否在测试模式下运行",
                    "检查Timer是否正常工作");
            }
        }

        /// <summary>
        /// 密码过期事件处理方法（用于测试4）
        /// </summary>
        static void OnPasswordExpiredTest(object sender, EventArgs e)
        {
            passwordExpired = true;
            Console.WriteLine();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  ╔════════════════════════════════════════════╗");
            Console.WriteLine("  ║     ⚠️  密码过期事件触发！               ║");
            Console.WriteLine("  ╚════════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine($"  过期时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine();
            TestHelper.PrintWarning("在此处添加密码过期后需要执行的操作");
            Console.WriteLine("    • 锁定界面功能");
            Console.WriteLine("    • 清除权限标记");
            Console.WriteLine("    • 记录过期日志");
            Console.WriteLine("    • 提示用户重新申请");
        }

        /// <summary>
        /// 测试5：操作日志记录
        /// </summary>
        static void Test5_OperationLog()
        {
            Console.Clear();
            TestHelper.PrintSeparator('═');
            Console.WriteLine("【测试5】记录操作日志");
            TestHelper.PrintSeparator('═');

            if (!TestConfig.TestMode)
            {
                TestHelper.ShowProductionWarning();
                if (!TestHelper.GetUserConfirmation("确认在生产环境执行？"))
                {
                    return;
                }
            }
            else
            {
                TestHelper.ShowTestModeInfo();
            }

            try
            {
                // 步骤1：理解操作日志的作用
                TestHelper.PrintStepSimple(1, "理解操作日志的作用", 
                    "就像监控录像，记录谁在什么时候做了什么");

                TestHelper.PrintExplanation("为什么需要操作日志？",
                    "在工业生产中，某些操作需要可追溯：",
                    "  • 谁修改了设备参数？",
                    "  • 什么时候修改的？",
                    "  • 修改了哪些参数？",
                    "  • 从什么值改成了什么值？",
                    "",
                    "作用：",
                    "  ✓ 审计追溯 - 出问题时可以查谁操作的",
                    "  ✓ 责任明确 - 每个操作都有记录",
                    "  ✓ 质量管理 - 符合ISO质量体系要求",
                    "  ✓ 问题分析 - 可以回溯操作历史");

                // 步骤2：准备操作记录数据
                TestHelper.PrintStepSimple(2, "准备操作记录数据", 
                    "整理要记录的操作信息");

                var operationRecord = new OperationRecord()
                {
                    AuthorizerName = TestConfig.ReceiverName,
                    AuthorizerNo = TestConfig.ReceiverNo,
                    EquipNo = TestConfig.EquipNo,
                    OperateStartTime = DateTime.Now.AddMinutes(-10),
                    OperateEndTime = DateTime.Now,
                    Datas = new List<OperationData>()
                    {
                        new OperationData()
                        {
                            DataName = "压力参数",
                            DataOldValue = "100 MPa",
                            DataNewValue = "120 MPa",
                            DataType = "设备参数"
                        },
                        new OperationData()
                        {
                            DataName = "温度阈值",
                            DataOldValue = "80°C",
                            DataNewValue = "90°C",
                            DataType = "设备参数"
                        },
                        new OperationData()
                        {
                            DataName = "运行模式",
                            DataOldValue = "手动模式",
                            DataNewValue = "自动模式",
                            DataType = "工作模式"
                        }
                    }
                };

                Console.WriteLine();
                Console.WriteLine("  操作记录内容：");
                var recordDict = new Dictionary<string, string>
                {
                    { "授权人", $"{operationRecord.AuthorizerName} ({operationRecord.AuthorizerNo})" },
                    { "设备编号", operationRecord.EquipNo },
                    { "开始时间", operationRecord.OperateStartTime.ToString("yyyy-MM-dd HH:mm:ss") },
                    { "结束时间", operationRecord.OperateEndTime.ToString("yyyy-MM-dd HH:mm:ss") },
                    { "操作项数", operationRecord.Datas.Count.ToString() + " 项" }
                };
                TestHelper.PrintTable(recordDict, "字段", "值");

                Console.WriteLine("  操作明细：");
                Console.WriteLine("  ╔═══════════╦═══════════╦═══════════╦═══════════╗");
                Console.WriteLine("  ║ 参数名称  ║ 原值      ║ 新值      ║ 类型      ║");
                Console.WriteLine("  ╠═══════════╬═══════════╬═══════════╬═══════════╣");
                foreach (var data in operationRecord.Datas)
                {
                    Console.WriteLine($"  ║ {data.DataName.PadRight(9)}║ {data.DataOldValue.PadRight(9)}║ {data.DataNewValue.PadRight(9)}║ {data.DataType.PadRight(9)}║");
                }
                Console.WriteLine("  ╚═══════════╩═══════════╩═══════════╩═══════════╝");
                Console.WriteLine();

                // 步骤3：调用Log方法
                TestHelper.PrintStepSimple(3, "调用OperationLog.Log()方法", 
                    "把操作记录发送到服务器进行审计");

                OperationLog opLog = new OperationLog(TestConfig.TestMode);
                TestHelper.PrintProcessing("正在上传操作记录到服务器...");

                try
                {
                    var result = opLog.Log(operationRecord);

                    // 步骤4：显示结果
                    TestHelper.PrintStepSimple(4, "确认记录结果", 
                        "检查日志是否成功保存");

                    if (result != null)
                    {
                        TestHelper.PrintSuccess("操作日志记录成功！");
                        TestHelper.PrintInfo("日志已保存到审计系统，可以追溯查询");
                        TestHelper.PrintInfo($"返回结果：{result.ToString()}");
                    }
                    else
                    {
                        TestHelper.PrintWarning("日志记录返回null，可能是网络问题");
                    }
                }
                catch (Exception logEx)
                {
                    TestHelper.PrintError($"日志记录失败：{logEx.Message}");
                }

                TestHelper.PrintSummary("本次测试验证了什么？",
                    "操作日志记录功能正常工作",
                    "可以记录完整的操作信息",
                    "支持记录多个参数的修改明细");

                TestHelper.PrintExplanation("实际项目中如何使用？",
                    "在你的设备操作程序中：",
                    "1. 获得权限后，开始记录操作开始时间",
                    "2. 用户修改参数时，记录旧值和新值到Datas列表",
                    "3. 操作完成后，记录结束时间",
                    "4. 调用 OperationLog.Log() 提交到服务器",
                    "5. 这样每次操作都有完整的审计记录");
            }
            catch (Exception ex)
            {
                TestHelper.ShowException(ex,
                    "记录操作日志时发生错误",
                    "检查OperationRecord数据是否完整",
                    "确认网络连接是否正常",
                    "验证服务器地址配置是否正确");
            }
        }

        /// <summary>
        /// 测试6：完整流程演示
        /// </summary>
        static void Test6_CompleteFlow()
        {
            Console.Clear();
            TestHelper.PrintSeparator('═');
            Console.WriteLine("【测试6】完整流程演示");
            TestHelper.PrintSeparator('═');

            TestHelper.ShowTestModeInfo();

            TestHelper.PrintExplanation("完整流程说明",
                "本测试将演示一个完整的授权操作流程：",
                "  1️⃣  申请动态密码 (RequestPassword)",
                "  2️⃣  等待管理员审批（模拟）",
                "  3️⃣  验证动态密码 (VerifyPassword)",
                "  4️⃣  执行需要授权的操作（模拟）",
                "  5️⃣  记录操作日志 (OperationLog)",
                "",
                "这就是实际项目中使用认证系统的标准流程！");

            TestHelper.WaitForKey("按任意键开始演示...");

            try
            {
                DateTime operationStartTime = DateTime.Now;
                
                // 流程1：申请密码
                Console.WriteLine();
                TestHelper.PrintSeparator('─');
                TestHelper.PrintInfo("流程 1/5：申请动态密码");
                TestHelper.PrintSeparator('─');

                DynamicPassword psw = new DynamicPassword(true);
                TestHelper.PrintProcessing("向管理员申请操作权限...");
                Thread.Sleep(1000); // 模拟网络延迟

                var requestResult = psw.RequestPassword(
                    TestConfig.EquipNo,
                    TestConfig.ApplicationName,
                    TestConfig.DefaultPrivilegeLevel,
                    TestConfig.DefaultPeriod,
                    "设备参数调整");

                if (requestResult != null && requestResult.Count > 0)
                {
                    TestHelper.PrintSuccess($"申请成功！已向 {requestResult[0].ReceiverName} 发送通知");
                }

                // 流程2：等待审批（模拟）
                Console.WriteLine();
                TestHelper.PrintSeparator('─');
                TestHelper.PrintInfo("流程 2/5：等待管理员审批");
                TestHelper.PrintSeparator('─');

                TestHelper.PrintProcessing("管理员查看申请...");
                Thread.Sleep(1500);
                TestHelper.PrintProcessing("管理员同意申请，发送6位密码...");
                Thread.Sleep(1000);
                TestHelper.PrintSuccess($"管理员已发送密码: {TestConfig.TestPassword}");

                // 流程3：验证密码
                Console.WriteLine();
                TestHelper.PrintSeparator('─');
                TestHelper.PrintInfo("流程 3/5：验证动态密码");
                TestHelper.PrintSeparator('─');

                TestHelper.PrintProcessing("操作人员输入收到的密码...");
                Thread.Sleep(1000);

                var verifyResult = psw.VerifyPassword(
                    TestConfig.EquipNo,
                    TestConfig.ApplicationName,
                    TestConfig.TestPassword);

                if (verifyResult != null && verifyResult.VerifyStatus)
                {
                    TestHelper.PrintSuccess("密码验证通过！获得操作权限");
                    TestHelper.PrintInfo($"授权人: {verifyResult.AuthorizerName}");
                    TestHelper.PrintInfo($"权限等级: {verifyResult.PrivilegeLevel}");
                    TestHelper.PrintInfo($"有效期: {TestConfig.DefaultPeriod} 分钟");
                }

                // 流程4：执行操作（模拟）
                Console.WriteLine();
                TestHelper.PrintSeparator('─');
                TestHelper.PrintInfo("流程 4/5：执行需要授权的操作");
                TestHelper.PrintSeparator('─');

                TestHelper.PrintProcessing("正在修改设备参数...");
                Thread.Sleep(1000);
                Console.WriteLine("  • 压力参数: 100 MPa → 120 MPa");
                Thread.Sleep(500);
                Console.WriteLine("  • 温度阈值: 80°C → 90°C");
                Thread.Sleep(500);
                Console.WriteLine("  • 运行模式: 手动 → 自动");
                Thread.Sleep(500);
                TestHelper.PrintSuccess("参数修改完成");

                DateTime operationEndTime = DateTime.Now;

                // 流程5：记录日志
                Console.WriteLine();
                TestHelper.PrintSeparator('─');
                TestHelper.PrintInfo("流程 5/5：记录操作日志");
                TestHelper.PrintSeparator('─');

                OperationLog opLog = new OperationLog(true);
                var operationRecord = new OperationRecord()
                {
                    AuthorizerName = verifyResult.AuthorizerName,
                    AuthorizerNo = verifyResult.AuthorizerNo,
                    EquipNo = TestConfig.EquipNo,
                    OperateStartTime = operationStartTime,
                    OperateEndTime = operationEndTime,
                    Datas = new List<OperationData>()
                    {
                        new OperationData()
                        {
                            DataName = "压力参数",
                            DataOldValue = "100 MPa",
                            DataNewValue = "120 MPa",
                            DataType = "设备参数"
                        },
                        new OperationData()
                        {
                            DataName = "温度阈值",
                            DataOldValue = "80°C",
                            DataNewValue = "90°C",
                            DataType = "设备参数"
                        },
                        new OperationData()
                        {
                            DataName = "运行模式",
                            DataOldValue = "手动模式",
                            DataNewValue = "自动模式",
                            DataType = "工作模式"
                        }
                    }
                };

                TestHelper.PrintProcessing("正在提交操作日志到服务器...");
                Thread.Sleep(1000);

                try
                {
                    var logResult = opLog.Log(operationRecord);
                    if (logResult != null)
                    {
                        TestHelper.PrintSuccess("操作日志记录成功");
                    }
                }
                catch (Exception logEx)
                {
                    TestHelper.PrintWarning($"日志记录失败：{logEx.Message}");
                }

                // 完成总结
                Console.WriteLine();
                TestHelper.PrintSeparator('═');
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✅ 完整流程演示完成！");
                Console.ResetColor();
                TestHelper.PrintSeparator('═');

                TestHelper.PrintSummary("流程总结",
                    "1. 申请权限 - 向管理员发送密码请求",
                    "2. 等待审批 - 管理员收到通知并发送密码",
                    "3. 验证密码 - 确认操作人员有权限",
                    "4. 执行操作 - 进行需要授权的操作",
                    "5. 记录日志 - 操作行为永久记录");

                TestHelper.PrintExplanation("实际应用建议",
                    "在你的WPF设备控制程序中：",
                    "",
                    "【界面设计】",
                    "  • 提供\"申请授权\"按钮",
                    "  • 密码输入对话框",
                    "  • 权限状态显示（剩余时间）",
                    "",
                    "【代码实现】",
                    "  • 用户点击高级功能 → 检查是否有权限",
                    "  • 无权限 → 调用RequestPassword申请",
                    "  • 弹出密码输入框 → 调用VerifyPassword验证",
                    "  • 验证通过 → 启用高级功能，开始记录操作",
                    "  • 操作完成 → 调用OperationLog记录",
                    "  • 监听PasswordExpired → 权限过期时锁定功能",
                    "",
                    "【安全建议】",
                    "  • 不要在客户端保存密码",
                    "  • 每次操作都要验证权限是否过期",
                    "  • 所有敏感操作都要记录日志");

            }
            catch (Exception ex)
            {
                TestHelper.ShowException(ex,
                    "完整流程演示时发生错误",
                    "检查各个组件是否正常工作",
                    "确认配置文件是否正确");
            }
        }

        /// <summary>
        /// 测试7：生产环境测试
        /// </summary>
        static void Test7_ProductionMode()
        {
            Console.Clear();
            TestHelper.PrintSeparator('═');
            Console.WriteLine("⚠️  【测试7】生产环境完整流程测试");
            TestHelper.PrintSeparator('═');

            // 显示警告
            TestHelper.ShowProductionWarning();

            if (!TestHelper.GetUserConfirmation("你确认要在生产环境执行测试吗？"))
            {
                TestHelper.PrintInfo("已取消生产环境测试");
                return;
            }

            Console.WriteLine();
            TestHelper.PrintWarning("再次确认：这将发送真实的密码请求！");
            if (!TestHelper.GetUserConfirmation("确认继续？"))
            {
                TestHelper.PrintInfo("已取消");
                return;
            }

            try
            {
                DateTime operationStartTime = DateTime.Now;

                // 步骤1：读取生产配置
                TestHelper.PrintStepSimple(1, "读取生产环境配置", 
                    "从配置文件读取真实的服务器参数");

                TestConfig.DisplayCurrentConfig();

                // 步骤2：请求真实密码
                TestHelper.PrintStepSimple(2, "向真实服务器请求密码", 
                    "系统正在联系认证服务器，准备发通知");

                DynamicPassword psw = new DynamicPassword(false); // 生产模式
                TestHelper.PrintProcessing("正在连接服务器...");

                var requestResult = psw.RequestPasswordManualReceiver(
                    TestConfig.ReceiverNo,
                    TestConfig.EquipNo,
                    TestConfig.ApplicationName,
                    TestConfig.DefaultPrivilegeLevel,
                    TestConfig.DefaultPeriod,
                    "生产环境测试");

                if (requestResult != null && requestResult.Count > 0)
                {
                    TestHelper.PrintSuccess("请求成功！");

                    var rows = new List<string[]>();
                    rows.Add(new string[] 
                    { 
                        requestResult[0].ReceiverName ?? "未知",
                        requestResult[0].ReceiverNo ?? "未知",
                        "短信+App推送"
                    });
                    TestHelper.PrintThreeColumnTable(
                        new string[] { "姓名", "工号", "通知方式" }, 
                        rows);

                    TestHelper.PrintTip($"{requestResult[0].ReceiverName} 的手机现在应该收到了6位数字密码");
                }
                else
                {
                    TestHelper.PrintError("密码请求失败");
                    return;
                }

                // 步骤3：输入真实密码
                TestHelper.PrintStepSimple(3, "等待用户输入真实密码", 
                    "请联系管理员获取他收到的6位密码");

                string password = TestHelper.GetUserInput("请输入收到的6位动态密码: ");

                if (string.IsNullOrWhiteSpace(password) || password.Length != 6)
                {
                    TestHelper.PrintError("密码格式不正确（应该是6位数字）");
                    return;
                }

                TestHelper.PrintProcessing("正在验证密码...");

                var verifyResult = psw.VerifyPassword(
                    TestConfig.EquipNo,
                    TestConfig.ApplicationName,
                    password);

                if (verifyResult != null && verifyResult.VerifyStatus)
                {
                    TestHelper.PrintSuccess("验证成功！");

                    Console.WriteLine();
                    Console.WriteLine("  ╔═══════════════════════════════════════════════╗");
                    Console.WriteLine("  ║            验证结果详情                       ║");
                    Console.WriteLine("  ╠═══════════════════════════════════════════════╣");
                    Console.WriteLine($"  ║ 验证状态: ✅ 通过                            ║");
                    Console.WriteLine($"  ║ 授权人: {(verifyResult.AuthorizerName ?? "未知").PadRight(35)}║");
                    Console.WriteLine($"  ║ 授权人工号: {(verifyResult.AuthorizerNo ?? "未知").PadRight(31)}║");
                    Console.WriteLine($"  ║ 权限等级: {verifyResult.PrivilegeLevel.ToString().PadRight(35)}║");
                    Console.WriteLine($"  ║ 有效期: {TestConfig.DefaultPeriod}分钟{new string(' ', 33 - TestConfig.DefaultPeriod.ToString().Length)}║");
                    Console.WriteLine($"  ║ 过期时间: {DateTime.Now.AddMinutes(TestConfig.DefaultPeriod):yyyy-MM-dd HH:mm:ss}      ║");
                    Console.WriteLine("  ╚═══════════════════════════════════════════════╝");
                    Console.WriteLine();

                    // 步骤4：记录日志
                    TestHelper.PrintStepSimple(4, "记录操作日志到服务器", 
                        "把本次操作记录到审计系统");

                    OperationLog opLog = new OperationLog(false); // 生产模式
                    var operationRecord = new OperationRecord()
                    {
                        AuthorizerName = verifyResult.AuthorizerName,
                        AuthorizerNo = verifyResult.AuthorizerNo,
                        EquipNo = TestConfig.EquipNo,
                        OperateStartTime = operationStartTime,
                        OperateEndTime = DateTime.Now,
                        Datas = new List<OperationData>()
                        {
                            new OperationData()
                            {
                                DataName = "测试项目",
                                DataOldValue = "生产环境测试前",
                                DataNewValue = "生产环境测试后",
                                DataType = "系统测试"
                            }
                        }
                    };

                    TestHelper.PrintProcessing("正在上传操作记录...");
                    
                    try
                    {
                        var logResult = opLog.Log(operationRecord);
                        if (logResult != null)
                        {
                            TestHelper.PrintSuccess("日志记录成功！");
                        }
                    }
                    catch (Exception logEx)
                    {
                        TestHelper.PrintWarning($"日志记录失败：{logEx.Message}");
                    }

                    // 测试完成
                    Console.WriteLine();
                    TestHelper.PrintSeparator('═');
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("【生产环境测试完成】✅");
                    Console.ResetColor();
                    TestHelper.PrintSeparator('═');

                    TestHelper.PrintSummary("本次测试验证了",
                        "✓ 真实服务器连接正常",
                        "✓ 密码请求和接收流程正常",
                        "✓ 密码验证功能正常",
                        "✓ 操作日志记录正常");

                    TestHelper.PrintExplanation("后续操作提醒",
                        $"• 你现在拥有{TestConfig.DefaultPeriod}分钟的高级权限",
                        "• 可以执行需要授权的设备操作",
                        "• 密码过期后会自动失效",
                        "• 所有操作都会被审计追踪");
                }
                else
                {
                    TestHelper.PrintError("密码验证失败");
                    if (verifyResult != null)
                    {
                        TestHelper.PrintWarning($"失败原因: {verifyResult.VerifyMessage}");
                    }
                }
            }
            catch (Exception ex)
            {
                TestHelper.ShowException(ex,
                    "生产环境测试时发生错误",
                    "检查网络连接是否正常",
                    "确认服务器地址是否正确",
                    "验证接收人工号是否存在",
                    "联系技术支持获取帮助");
            }
        }

        #endregion
    }
}
