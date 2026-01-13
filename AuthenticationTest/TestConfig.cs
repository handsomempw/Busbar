using System;
using System.Configuration;
using System.Xml;

namespace AuthenticationTest
{
    /// <summary>
    /// 测试配置管理类
    /// 功能：负责读取和管理配置文件中的参数，支持测试模式和生产模式的切换
    /// </summary>
    /// <remarks>
    /// 通俗解释：就像一个设置面板，管理所有的配置选项
    /// - 可以读取配置文件中的各种参数（设备编号、应用名称等）
    /// - 可以一键切换测试模式和生产模式
    /// - 确保程序使用正确的参数运行
    /// </remarks>
    public class TestConfig
    {
        #region 配置属性 - Configuration Properties

        /// <summary>
        /// 获取测试模式开关
        /// </summary>
        /// <remarks>
        /// 专业说明：控制程序是否在测试模式下运行
        /// 通俗解释：true = 练习模式（不发真实请求），false = 正式模式（会发真实请求）
        /// </remarks>
        public static bool TestMode
        {
            get
            {
                string value = ConfigurationManager.AppSettings["TestMode"];
                return string.IsNullOrEmpty(value) ? true : bool.Parse(value);
            }
        }

        /// <summary>
        /// 获取设备编号
        /// </summary>
        /// <remarks>
        /// 专业说明：设备的唯一标识符，用于标识请求权限的设备
        /// 通俗解释：就像设备的身份证号码
        /// </remarks>
        public static string EquipNo
        {
            get { return ConfigurationManager.AppSettings["EquipNo"] ?? "E11DMJ000500"; }
        }

        /// <summary>
        /// 获取应用程序名称
        /// </summary>
        /// <remarks>
        /// 专业说明：请求权限的应用程序标识
        /// 通俗解释：告诉系统是哪个程序在申请权限
        /// </remarks>
        public static string ApplicationName
        {
            get { return ConfigurationManager.AppSettings["ApplicationName"] ?? "BusbarCompressionSystem"; }
        }

        /// <summary>
        /// 获取默认权限等级
        /// </summary>
        /// <remarks>
        /// 专业说明：申请的权限级别，范围1-10，数字越大权限越高
        /// 通俗解释：权限等级就像员工级别，等级越高能做的事越多
        /// </remarks>
        public static int DefaultPrivilegeLevel
        {
            get
            {
                string value = ConfigurationManager.AppSettings["DefaultPrivilegeLevel"];
                return string.IsNullOrEmpty(value) ? 5 : int.Parse(value);
            }
        }

        /// <summary>
        /// 获取默认有效期（分钟）
        /// </summary>
        /// <remarks>
        /// 专业说明：动态密码的有效时间，单位为分钟
        /// 通俗解释：密码能用多久，就像停车券的有效时间
        /// </remarks>
        public static int DefaultPeriod
        {
            get
            {
                string value = ConfigurationManager.AppSettings["DefaultPeriod"];
                return string.IsNullOrEmpty(value) ? 60 : int.Parse(value);
            }
        }

        /// <summary>
        /// 获取接收人工号
        /// </summary>
        /// <remarks>
        /// 专业说明：接收动态密码的管理员工号
        /// 通俗解释：指定哪个管理员来审批你的申请
        /// </remarks>
        public static string ReceiverNo
        {
            get { return ConfigurationManager.AppSettings["ReceiverNo"] ?? "H10009868"; }
        }

        /// <summary>
        /// 获取接收人姓名
        /// </summary>
        /// <remarks>
        /// 专业说明：接收人的姓名，仅用于界面显示
        /// 通俗解释：管理员的名字，方便知道找谁
        /// </remarks>
        public static string ReceiverName
        {
            get { return ConfigurationManager.AppSettings["ReceiverName"] ?? "管理员"; }
        }

        /// <summary>
        /// 获取测试用密码
        /// </summary>
        /// <remarks>
        /// 专业说明：测试模式下使用的模拟密码
        /// 通俗解释：练习时用的假密码，不是真的
        /// </remarks>
        public static string TestPassword
        {
            get { return ConfigurationManager.AppSettings["TestPassword"] ?? "937345"; }
        }

        #endregion

        #region 模式切换方法 - Mode Switch Methods

        /// <summary>
        /// 切换测试模式和生产模式
        /// </summary>
        /// <param name="toTestMode">true=切换到测试模式, false=切换到生产模式</param>
        /// <returns>切换是否成功</returns>
        /// <remarks>
        /// 专业说明：修改配置文件中的TestMode值，实现模式切换
        /// 通俗解释：在"练习模式"和"正式模式"之间切换
        /// 
        /// 工作原理：
        /// 1. 打开配置文件(App.config)
        /// 2. 找到TestMode这一行
        /// 3. 修改它的值为true或false
        /// 4. 保存文件
        /// 
        /// 注意事项：
        /// - 切换后需要重启程序才能生效
        /// - 生产模式下会发送真实请求，请谨慎操作
        /// </remarks>
        public static bool SwitchMode(bool toTestMode)
        {
            try
            {
                // 专业操作：加载配置文件
                // 通俗解释：打开设置文件准备修改
                Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

                // 专业操作：修改TestMode配置项的值
                // 通俗解释：把"测试模式"开关改成想要的状态
                if (config.AppSettings.Settings["TestMode"] != null)
                {
                    config.AppSettings.Settings["TestMode"].Value = toTestMode.ToString().ToLower();
                }
                else
                {
                    config.AppSettings.Settings.Add("TestMode", toTestMode.ToString().ToLower());
                }

                // 专业操作：保存配置更改
                // 通俗解释：保存修改，就像按下"保存"按钮
                config.Save(ConfigurationSaveMode.Modified);

                // 专业操作：刷新配置节，使更改立即可读
                // 通俗解释：让程序重新读取设置，看到最新的值
                ConfigurationManager.RefreshSection("appSettings");

                return true;
            }
            catch (Exception ex)
            {
                // 捕获异常，防止程序崩溃
                Console.WriteLine($"切换模式失败：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取当前模式的描述文本
        /// </summary>
        /// <returns>模式描述字符串</returns>
        /// <remarks>
        /// 专业说明：返回当前运行模式的友好描述
        /// 通俗解释：告诉你现在是在"练习"还是"实战"
        /// </remarks>
        public static string GetModeDescription()
        {
            if (TestMode)
            {
                return "[测试模式] 🧪  (安全的练习环境，不会发送真实请求)";
            }
            else
            {
                return "[生产模式] 🔴  (警告：会连接真实服务器，发送实际请求！)";
            }
        }

        /// <summary>
        /// 显示当前所有配置信息
        /// </summary>
        /// <remarks>
        /// 专业说明：以表格形式展示所有配置参数
        /// 通俗解释：把所有设置列出来，一目了然
        /// </remarks>
        public static void DisplayCurrentConfig()
        {
            Console.WriteLine();
            Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                    当前配置信息                            ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine($"  运行模式       : {GetModeDescription()}");
            Console.WriteLine($"  设备编号       : {EquipNo}");
            Console.WriteLine($"  应用程序名称   : {ApplicationName}");
            Console.WriteLine($"  默认权限等级   : {DefaultPrivilegeLevel}");
            Console.WriteLine($"  默认有效期     : {DefaultPeriod} 分钟");
            Console.WriteLine($"  接收人工号     : {ReceiverNo}");
            Console.WriteLine($"  接收人姓名     : {ReceiverName}");
            if (TestMode)
            {
                Console.WriteLine($"  测试用密码     : {TestPassword}");
            }
            Console.WriteLine();
        }

        #endregion

        #region 配置验证方法 - Validation Methods

        /// <summary>
        /// 验证配置是否完整有效
        /// </summary>
        /// <returns>true=配置有效, false=配置无效</returns>
        /// <remarks>
        /// 专业说明：检查所有必需的配置项是否存在且格式正确
        /// 通俗解释：检查设置是否填写完整，避免运行时出错
        /// </remarks>
        public static bool ValidateConfig()
        {
            bool isValid = true;

            // 检查设备编号
            if (string.IsNullOrWhiteSpace(EquipNo))
            {
                Console.WriteLine("⚠️  配置错误：设备编号不能为空");
                isValid = false;
            }

            // 检查应用程序名称
            if (string.IsNullOrWhiteSpace(ApplicationName))
            {
                Console.WriteLine("⚠️  配置错误：应用程序名称不能为空");
                isValid = false;
            }

            // 检查权限等级范围
            if (DefaultPrivilegeLevel < 1 || DefaultPrivilegeLevel > 10)
            {
                Console.WriteLine("⚠️  配置错误：权限等级必须在1-10之间");
                isValid = false;
            }

            // 检查有效期范围
            if (DefaultPeriod < 1 || DefaultPeriod > 1440)
            {
                Console.WriteLine("⚠️  配置错误：有效期必须在1-1440分钟之间（最多24小时）");
                isValid = false;
            }

            return isValid;
        }

        #endregion
    }
}
