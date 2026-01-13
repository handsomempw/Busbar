using System;
using System.Collections.Generic;
using System.Text;

namespace AuthenticationTest
{
    /// <summary>
    /// 测试辅助工具类
    /// 功能：提供美化的控制台输出方法，让测试结果更易读
    /// </summary>
    /// <remarks>
    /// 通俗解释：就像一个美工，负责把测试结果打扮得漂亮、清晰
    /// - 可以打印各种好看的边框、表格
    /// - 用不同颜色标记成功/失败/警告
    /// - 让控制台输出像书本一样易读
    /// </remarks>
    public static class TestHelper
    {
        #region 颜色常量 - Color Constants

        // 定义常用的控制台颜色
        private static readonly ConsoleColor DefaultColor = ConsoleColor.Gray;
        private static readonly ConsoleColor SuccessColor = ConsoleColor.Green;
        private static readonly ConsoleColor ErrorColor = ConsoleColor.Red;
        private static readonly ConsoleColor WarningColor = ConsoleColor.Yellow;
        private static readonly ConsoleColor InfoColor = ConsoleColor.Cyan;
        private static readonly ConsoleColor HighlightColor = ConsoleColor.White;

        #endregion

        #region 标题和边框方法 - Header and Border Methods

        /// <summary>
        /// 打印大标题（带双层边框）
        /// </summary>
        /// <param name="title">标题文本</param>
        /// <remarks>
        /// 通俗解释：打印一个醒目的大标题，像书的章节名
        /// </remarks>
        public static void PrintHeader(string title)
        {
            Console.WriteLine();
            Console.ForegroundColor = HighlightColor;
            Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
            Console.WriteLine($"║   {title.PadRight(54)}║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();
        }

        /// <summary>
        /// 打印分隔线
        /// </summary>
        /// <param name="character">用于绘制分隔线的字符</param>
        /// <remarks>
        /// 通俗解释：画一条横线，分隔不同的内容区域
        /// </remarks>
        public static void PrintSeparator(char character = '═')
        {
            Console.WriteLine(new string(character, 60));
        }

        /// <summary>
        /// 打印小节标题（单层边框）
        /// </summary>
        /// <param name="title">标题文本</param>
        /// <remarks>
        /// 通俗解释：打印一个小标题，像段落标题
        /// </remarks>
        public static void PrintSubHeader(string title)
        {
            Console.WriteLine();
            Console.ForegroundColor = InfoColor;
            Console.WriteLine("┌─────────────────────────────────────────────────┐");
            Console.WriteLine($"│ {title.PadRight(47)}│");
            Console.WriteLine("└─────────────────────────────────────────────────┘");
            Console.ResetColor();
        }

        #endregion

        #region 步骤说明方法 - Step Description Methods

        /// <summary>
        /// 打印测试步骤（包含专业说明和通俗解释）
        /// </summary>
        /// <param name="stepNumber">步骤编号</param>
        /// <param name="stepTitle">步骤标题</param>
        /// <param name="professionalDesc">专业说明</param>
        /// <param name="popularDesc">通俗解释</param>
        /// <remarks>
        /// 通俗解释：显示每个测试步骤，既有专业术语，也有大白话解释
        /// </remarks>
        public static void PrintStep(int stepNumber, string stepTitle, string professionalDesc, string popularDesc)
        {
            Console.WriteLine();
            Console.ForegroundColor = InfoColor;
            Console.WriteLine($"┌─ 步骤{stepNumber}：{stepTitle} {'─'.ToString().PadLeft(40 - stepTitle.Length, '─')}┐");
            Console.ResetColor();
            
            Console.WriteLine($"│ 专业说明：{professionalDesc.PadRight(38)}│");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"│ 通俗解释：{popularDesc.PadRight(38)}│");
            Console.ResetColor();
            
            Console.WriteLine("└───────────────────────────────────────────────┘");
        }

        /// <summary>
        /// 打印简化的步骤说明（只有通俗解释）
        /// </summary>
        /// <param name="stepNumber">步骤编号</param>
        /// <param name="stepTitle">步骤标题</param>
        /// <param name="description">说明文本</param>
        public static void PrintStepSimple(int stepNumber, string stepTitle, string description)
        {
            Console.WriteLine();
            Console.ForegroundColor = InfoColor;
            Console.WriteLine($"┌─ 步骤{stepNumber}：{stepTitle} ─────────────────────┐");
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"│ {description.PadRight(47)}│");
            Console.ResetColor();
            Console.WriteLine("└─────────────────────────────────────────────────┘");
        }

        #endregion

        #region 状态标记方法 - Status Marker Methods

        /// <summary>
        /// 打印成功信息
        /// </summary>
        /// <param name="message">成功消息</param>
        /// <remarks>
        /// 通俗解释：用绿色打印成功的消息，让人一眼看出测试通过了
        /// </remarks>
        public static void PrintSuccess(string message)
        {
            Console.ForegroundColor = SuccessColor;
            Console.WriteLine($"  ✅ {message}");
            Console.ResetColor();
        }

        /// <summary>
        /// 打印错误信息
        /// </summary>
        /// <param name="message">错误消息</param>
        /// <remarks>
        /// 通俗解释：用红色打印错误消息，醒目地提示出了问题
        /// </remarks>
        public static void PrintError(string message)
        {
            Console.ForegroundColor = ErrorColor;
            Console.WriteLine($"  ❌ {message}");
            Console.ResetColor();
        }

        /// <summary>
        /// 打印警告信息
        /// </summary>
        /// <param name="message">警告消息</param>
        /// <remarks>
        /// 通俗解释：用黄色打印警告，提醒注意但不是错误
        /// </remarks>
        public static void PrintWarning(string message)
        {
            Console.ForegroundColor = WarningColor;
            Console.WriteLine($"  ⚠️  {message}");
            Console.ResetColor();
        }

        /// <summary>
        /// 打印信息提示
        /// </summary>
        /// <param name="message">提示消息</param>
        /// <remarks>
        /// 通俗解释：打印一般的提示信息
        /// </remarks>
        public static void PrintInfo(string message)
        {
            Console.ForegroundColor = InfoColor;
            Console.WriteLine($"  ℹ️  {message}");
            Console.ResetColor();
        }

        /// <summary>
        /// 打印加载中/处理中信息
        /// </summary>
        /// <param name="message">处理消息</param>
        public static void PrintProcessing(string message)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"  ⏳ {message}");
            Console.ResetColor();
        }

        /// <summary>
        /// 打印提示/建议信息
        /// </summary>
        /// <param name="message">提示消息</param>
        public static void PrintTip(string message)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  💡 {message}");
            Console.ResetColor();
        }

        #endregion

        #region 表格输出方法 - Table Output Methods

        /// <summary>
        /// 打印简单的两列表格
        /// </summary>
        /// <param name="data">数据字典（键值对）</param>
        /// <param name="header1">第一列标题</param>
        /// <param name="header2">第二列标题</param>
        /// <remarks>
        /// 通俗解释：把数据整理成表格，像Excel一样整齐
        /// </remarks>
        public static void PrintTable(Dictionary<string, string> data, string header1 = "属性", string header2 = "值")
        {
            Console.WriteLine();
            Console.WriteLine($"  ╔{new string('═', 20)}╦{new string('═', 35)}╗");
            Console.ForegroundColor = HighlightColor;
            Console.WriteLine($"  ║ {header1.PadRight(18)}║ {header2.PadRight(33)}║");
            Console.ResetColor();
            Console.WriteLine($"  ╠{new string('═', 20)}╬{new string('═', 35)}╣");

            foreach (var item in data)
            {
                Console.WriteLine($"  ║ {item.Key.PadRight(18)}║ {item.Value.PadRight(33)}║");
            }

            Console.WriteLine($"  ╚{new string('═', 20)}╩{new string('═', 35)}╝");
            Console.WriteLine();
        }

        /// <summary>
        /// 打印三列表格（适合显示接收人信息等）
        /// </summary>
        /// <param name="headers">列标题数组</param>
        /// <param name="rows">数据行列表</param>
        /// <remarks>
        /// 通俗解释：打印更复杂的三列表格
        /// </remarks>
        public static void PrintThreeColumnTable(string[] headers, List<string[]> rows)
        {
            Console.WriteLine();
            Console.WriteLine($"  ╔{new string('═', 10)}╦{new string('═', 12)}╦{new string('═', 26)}╗");
            
            Console.ForegroundColor = HighlightColor;
            Console.WriteLine($"  ║ {headers[0].PadRight(8)}║ {headers[1].PadRight(10)}║ {headers[2].PadRight(24)}║");
            Console.ResetColor();
            
            Console.WriteLine($"  ╠{new string('═', 10)}╬{new string('═', 12)}╬{new string('═', 26)}╣");

            foreach (var row in rows)
            {
                Console.WriteLine($"  ║ {row[0].PadRight(8)}║ {row[1].PadRight(10)}║ {row[2].PadRight(24)}║");
            }

            Console.WriteLine($"  ╚{new string('═', 10)}╩{new string('═', 12)}╩{new string('═', 26)}╝");
            Console.WriteLine();
        }

        #endregion

        #region 解释说明方法 - Explanation Methods

        /// <summary>
        /// 打印通俗解释区块
        /// </summary>
        /// <param name="title">解释标题</param>
        /// <param name="content">解释内容（支持多行）</param>
        /// <remarks>
        /// 通俗解释：打印一段详细的解释文字，帮助理解
        /// </remarks>
        public static void PrintExplanation(string title, params string[] content)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"【{title}】");
            Console.ResetColor();

            foreach (var line in content)
            {
                Console.WriteLine($"  {line}");
            }
            Console.WriteLine();
        }

        /// <summary>
        /// 打印测试总结
        /// </summary>
        /// <param name="title">总结标题</param>
        /// <param name="items">总结项列表</param>
        public static void PrintSummary(string title, params string[] items)
        {
            Console.WriteLine();
            Console.ForegroundColor = HighlightColor;
            Console.WriteLine($"【{title}】");
            Console.ResetColor();

            foreach (var item in items)
            {
                Console.ForegroundColor = SuccessColor;
                Console.WriteLine($"  ✓ {item}");
                Console.ResetColor();
            }
            Console.WriteLine();
        }

        #endregion

        #region 用户交互方法 - User Interaction Methods

        /// <summary>
        /// 等待用户按任意键继续
        /// </summary>
        /// <param name="message">提示消息</param>
        public static void WaitForKey(string message = "按任意键继续...")
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  {message}");
            Console.ResetColor();
            Console.ReadKey(true);
        }

        /// <summary>
        /// 获取用户输入（带提示）
        /// </summary>
        /// <param name="prompt">输入提示</param>
        /// <returns>用户输入的字符串</returns>
        public static string GetUserInput(string prompt)
        {
            Console.Write($"  {prompt}");
            Console.ForegroundColor = HighlightColor;
            string input = Console.ReadLine();
            Console.ResetColor();
            return input;
        }

        /// <summary>
        /// 获取用户确认（Y/N）
        /// </summary>
        /// <param name="prompt">确认提示</param>
        /// <returns>true=确认, false=取消</returns>
        public static bool GetUserConfirmation(string prompt)
        {
            Console.Write($"  {prompt} (Y/N): ");
            Console.ForegroundColor = HighlightColor;
            string input = Console.ReadLine();
            Console.ResetColor();
            return input?.ToUpper() == "Y" || input?.ToUpper() == "YES";
        }

        #endregion

        #region 生产模式警告方法 - Production Mode Warning Methods

        /// <summary>
        /// 显示生产模式警告框
        /// </summary>
        /// <remarks>
        /// 通俗解释：在进行真实操作前，显示醒目的警告提示
        /// </remarks>
        public static void ShowProductionWarning()
        {
            Console.WriteLine();
            Console.ForegroundColor = ErrorColor;
            Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                  ⚠️  生产环境警告 ⚠️                      ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();
            
            Console.ForegroundColor = WarningColor;
            Console.WriteLine("  ⚠️  重要提醒：");
            Console.WriteLine("    • 即将连接真实认证服务器");
            Console.WriteLine("    • 会向真实管理员发送密码请求（短信/App通知）");
            Console.WriteLine("    • 操作会被永久记录到审计日志");
            Console.WriteLine("    • 请确保你有权限进行真实环境测试");
            Console.ResetColor();
            Console.WriteLine();
        }

        /// <summary>
        /// 显示测试模式提示
        /// </summary>
        public static void ShowTestModeInfo()
        {
            Console.WriteLine();
            Console.ForegroundColor = SuccessColor;
            Console.WriteLine("  🧪 测试模式说明：");
            Console.ResetColor();
            Console.WriteLine("    • 不会连接真实服务器");
            Console.WriteLine("    • 不会发送实际的短信或通知");
            Console.WriteLine("    • 返回的是模拟数据，用于学习和测试");
            Console.WriteLine("    • 安全的练习环境，可以放心操作");
            Console.WriteLine();
        }

        #endregion

        #region 欢迎界面方法 - Welcome Screen Methods

        /// <summary>
        /// 显示程序欢迎界面
        /// </summary>
        public static void ShowWelcomeScreen()
        {
            Console.Clear();
            Console.ForegroundColor = HighlightColor;
            Console.WriteLine();
            Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║   Faratronic 动态密码认证系统 - 测试学习程序 v1.0        ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();

            PrintExplanation("什么是动态密码认证系统？",
                "就像你用手机银行转账时，银行会发一个短信验证码给你，",
                "这个验证码只能用一次，而且有时间限制。",
                "",
                "本系统也类似：",
                "✓ 设备需要高级权限时，向管理员申请临时密码",
                "✓ 管理员收到通知，发送6位数字密码",
                "✓ 操作人员输入密码，系统验证通过后才能操作",
                "✓ 密码有效期到了自动失效，保证安全",
                "✓ 所有操作都有日志记录，可以追溯");

            PrintExplanation("为什么要学习这个系统？",
                "在工业设备管理中，某些危险或重要操作需要授权。",
                "学会使用这个系统，你就能为你的项目添加安全控制功能！");
        }

        #endregion

        #region 异常显示方法 - Exception Display Methods

        /// <summary>
        /// 显示友好的异常信息
        /// </summary>
        /// <param name="ex">异常对象</param>
        /// <param name="friendlyMessage">通俗的错误说明</param>
        /// <param name="suggestions">解决建议列表</param>
        public static void ShowException(Exception ex, string friendlyMessage, params string[] suggestions)
        {
            Console.WriteLine();
            Console.ForegroundColor = ErrorColor;
            Console.WriteLine("⚠️ 【异常捕获】发生了一个错误");
            Console.ResetColor();
            Console.WriteLine();

            Console.WriteLine("专业信息：");
            Console.WriteLine($"  异常类型: {ex.GetType().Name}");
            Console.WriteLine($"  错误消息: {ex.Message}");
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("通俗解释：");
            Console.WriteLine($"  {friendlyMessage}");
            Console.ResetColor();
            Console.WriteLine();

            if (suggestions != null && suggestions.Length > 0)
            {
                Console.WriteLine("建议解决方法：");
                foreach (var suggestion in suggestions)
                {
                    Console.WriteLine($"  • {suggestion}");
                }
                Console.WriteLine();
            }
        }

        #endregion
    }
}
