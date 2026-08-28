using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using BusbarCompressionSystem.Model;

namespace BusbarCompressionSystem.Utils
{
    /// <summary>
    /// 耐压测试状态翻译器
    /// 负责将AT9620设备返回的英文状态码转换为中文描述
    /// 支持从独立XML配置文件加载映射表
    /// </summary>
    public static class TvStatusTranslator
    {
        private static readonly Dictionary<string, string> DefaultMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "PASS", "测试合格" },
            { "SHORT", "短路保护" },
            { "ARC", "电弧保护" },
            { "GFI", "触电短路" },
            { "BREAKDOWN", "击穿保护" },
            { "ERROR", "功放板错误" },
            { "OV", "过压保护" },
            { "UPPER", "超出上限" },
            { "LOWER", "低于下限" },
            { "RISELOW", "缓升上限报警" }
        };

        private static Dictionary<string, string> _runtimeMappings = new Dictionary<string, string>(DefaultMappings, StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyDictionary<string, string> Defaults => DefaultMappings;

        public static void Reload(IEnumerable<TvStatusMapping> mappings)
        {
            var map = new Dictionary<string, string>(DefaultMappings, StringComparer.OrdinalIgnoreCase);

            if (mappings != null)
            {
                foreach (var item in mappings.Where(m => !string.IsNullOrWhiteSpace(m?.Code) && !string.IsNullOrWhiteSpace(m.Display)))
                {
                    map[item.Code.Trim()] = item.Display.Trim();
                }
            }

            _runtimeMappings = map;
        }

        /// <summary>
        /// 测试模式前缀常量
        /// </summary>
        public const string ACW_PREFIX = "[ACW]";
        public const string DCW_PREFIX = "[DCW]";

        /// <summary>
        /// 耐压仪未给出可信结束态时写入 SQLite 的固定状态。
        /// 该状态表示本轮没有形成仪器判定；CHECK 阶段据此保留过程追溯，
        /// 标准 CHECK1 可归入 NG3 待复测，CHECK2 与其它流程继续执行各自现有报工门禁。
        /// </summary>
        public const string CommunicationFailureStatus = "通信异常";

        /// <summary>
        /// 判断状态是否代表耐压通信或流程异常导致的无效测试结果。
        /// ACW/DCW 模式前缀允许存在；普通仪器结束态不命中该规则。
        /// </summary>
        /// <param name="status">SQLite 或内存中的耐压状态文本，可带 ACW/DCW 模式前缀。</param>
        /// <returns>状态表示未取得可信仪器结束结果时返回 true。</returns>
        public static bool IsCommunicationFailure(string status)
        {
            string text = RemoveTestModePrefix(status)?.Trim() ?? string.Empty;
            return text.StartsWith(CommunicationFailureStatus, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 判断耐压状态是否属于仪器已知结束码。
        /// 该白名单与 AT9620 结束状态保持一致，用于在主程序仅引用既有仪器程序集时识别正常测试结论。
        /// </summary>
        /// <param name="status">SQLite 或内存中的耐压状态文本，可带 ACW/DCW 模式前缀。</param>
        /// <returns>状态属于 PASS、SHORT、ARC 等已知结束码时返回 true。</returns>
        public static bool IsTrustedTerminalStatus(string status)
        {
            string text = RemoveTestModePrefix(status)?.Trim() ?? string.Empty;
            switch (text.ToUpperInvariant())
            {
                case "PASS":
                case "SHORT":
                case "ARC":
                case "GFI":
                case "BREAKDOWN":
                case "ERROR":
                case "OV":
                case "UPPER":
                case "LOWER":
                case "RISELOW":
                    return true;
                default:
                    return false;
            }
        }

        public static string Translate(string rawStatus)
        {
            if (string.IsNullOrWhiteSpace(rawStatus))
            {
                return rawStatus;
            }

            return _runtimeMappings.TryGetValue(rawStatus.Trim(), out var display)
                ? display
                : rawStatus;
        }

        /// <summary>
        /// 带测试模式前缀的翻译方法
        /// 先提取前缀（如[ACW]或[DCW]），翻译状态码后再拼接前缀
        /// </summary>
        /// <param name="rawStatus">原始状态（可能带前缀）</param>
        /// <returns>翻译后的状态（保留前缀）</returns>
        public static string TranslateWithPrefix(string rawStatus)
        {
            if (string.IsNullOrWhiteSpace(rawStatus))
            {
                return rawStatus;
            }

            string prefix = string.Empty;
            string statusCode = rawStatus.Trim();

            // 提取前缀
            if (statusCode.StartsWith(ACW_PREFIX))
            {
                prefix = ACW_PREFIX;
                statusCode = statusCode.Substring(ACW_PREFIX.Length);
            }
            else if (statusCode.StartsWith(DCW_PREFIX))
            {
                prefix = DCW_PREFIX;
                statusCode = statusCode.Substring(DCW_PREFIX.Length);
            }

            // 翻译状态码
            string translated = Translate(statusCode);

            // 拼接前缀和翻译结果
            return prefix + translated;
        }

        /// <summary>
        /// 为状态添加测试模式前缀
        /// </summary>
        /// <param name="status">原始状态</param>
        /// <param name="isACW">是否为ACW模式（false则为DCW）</param>
        /// <returns>带前缀的状态</returns>
        public static string AddTestModePrefix(string status, bool isACW)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return status;
            }

            // 如果已有前缀则不重复添加
            if (status.StartsWith(ACW_PREFIX) || status.StartsWith(DCW_PREFIX))
            {
                return status;
            }

            return (isACW ? ACW_PREFIX : DCW_PREFIX) + status;
        }

        /// <summary>
        /// 从状态中提取测试模式
        /// </summary>
        /// <param name="status">带前缀的状态</param>
        /// <returns>"ACW"、"DCW"或空字符串（无前缀）</returns>
        public static string ExtractTestMode(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return string.Empty;
            }

            if (status.StartsWith(ACW_PREFIX))
            {
                return "ACW";
            }
            else if (status.StartsWith(DCW_PREFIX))
            {
                return "DCW";
            }

            return string.Empty;
        }

        /// <summary>
        /// 从状态中移除测试模式前缀，获取纯状态码
        /// </summary>
        /// <param name="status">带前缀的状态</param>
        /// <returns>不带前缀的状态</returns>
        public static string RemoveTestModePrefix(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return status;
            }

            if (status.StartsWith(ACW_PREFIX))
            {
                return status.Substring(ACW_PREFIX.Length);
            }
            else if (status.StartsWith(DCW_PREFIX))
            {
                return status.Substring(DCW_PREFIX.Length);
            }

            return status;
        }

        /// <summary>
        /// 从独立XML配置文件加载状态映射
        /// 文件格式：
        /// &lt;StatusMappings&gt;
        ///   &lt;Map Code="PASS" Display="测试合格" /&gt;
        ///   &lt;Map Code="SHORT" Display="短路保护" /&gt;
        /// &lt;/StatusMappings&gt;
        /// </summary>
        /// <param name="xmlFilePath">XML配置文件路径</param>
        /// <param name="errorMessage">加载失败时的错误信息</param>
        /// <returns>是否成功加载</returns>
        public static bool LoadFromXml(string xmlFilePath, out string errorMessage)
        {
            errorMessage = string.Empty;

            try
            {
                if (!File.Exists(xmlFilePath))
                {
                    errorMessage = $"配置文件不存在: {xmlFilePath}";
                    return false;
                }

                var xdoc = XDocument.Load(xmlFilePath);
                var mappings = xdoc.Root
                    .Elements("Map")
                    .Where(e => e.Attribute("Code") != null && e.Attribute("Display") != null)
                    .Select(e => new TvStatusMapping
                    {
                        Code = e.Attribute("Code").Value,
                        Display = e.Attribute("Display").Value
                    });

                Reload(mappings);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"加载配置文件失败: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 保存默认映射到XML配置文件
        /// </summary>
        /// <param name="xmlFilePath">XML配置文件路径</param>
        /// <param name="errorMessage">保存失败时的错误信息</param>
        /// <returns>是否成功保存</returns>
        public static bool SaveDefaultXml(string xmlFilePath, out string errorMessage)
        {
            errorMessage = string.Empty;

            try
            {
                var dir = Path.GetDirectoryName(xmlFilePath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var xdoc = new XDocument(
                    new XDeclaration("1.0", "utf-8", null),
                    new XComment(" 耐压测试状态映射配置 "),
                    new XComment(" 格式: <Map Code=\"状态代码\" Display=\"中文描述\" /> "),
                    new XComment(" 修改后需重启软件生效，支持新增、修改、删除映射项 "),
                    new XElement("StatusMappings",
                        DefaultMappings.Select(kv =>
                            new XElement("Map",
                                new XAttribute("Code", kv.Key),
                                new XAttribute("Display", kv.Value)
                            )
                        )
                    )
                );

                using (var writer = new StreamWriter(xmlFilePath, false, Encoding.UTF8))
                {
                    xdoc.Save(writer);
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"保存配置文件失败: {ex.Message}";
                return false;
            }
        }
    }

    /// <summary>
    /// 耐压测试状态映射配置项
    /// </summary>
    public class TvStatusMapping
    {
        public string Code { get; set; }
        public string Display { get; set; }
    }
}

