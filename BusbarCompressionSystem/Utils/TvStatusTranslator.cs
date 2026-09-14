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
    /// 耐压测试状态翻译器。
    /// 将 AT9620 返回的英文结束态 Code 转为中文 Display；运行时映射表的 Key 同时作为上位机/仪器侧
    /// 「可信结束态」码表，与过程态（Ramp Up 等）无关。
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
            { "HI-LIMIT", "超上限" },
            { "LO-LIMIT", "超下限" },
            { "LOWER", "低于下限" },
            { "RISELOW", "缓升上限报警" }
        };

        private static Dictionary<string, string> _runtimeMappings = new Dictionary<string, string>(DefaultMappings, StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyDictionary<string, string> Defaults => DefaultMappings;

        /// <summary>
        /// 用配置项重建运行时映射。默认码表始终作为底线保留；配置中的同名 Code 只覆盖 Display，
        /// 新增 Code 同时进入翻译表与可信结束态码表（供 AT9620 监控收工与落库判定共用）。
        /// </summary>
        /// <param name="mappings">
        /// 来自 <c>耐压状态映射.xml</c> 的 Code/Display 对；允许为 null，此时仅恢复默认映射。
        /// </param>
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
        /// 当前运行时映射表中的全部状态原文 Code。
        /// 主程序加载映射后交给 <see cref="AT9620.AT9620.ConfigureKnownTerminalStatuses"/>，
        /// 使仪器 Fetch 收工白名单与界面翻译同源。
        /// </summary>
        /// <returns>可信结束态 Code 集合（含默认码与配置新增码）；顺序不保证。</returns>
        public static IReadOnlyCollection<string> GetTrustedTerminalCodes()
        {
            return _runtimeMappings.Keys.ToArray();
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
        /// 判断耐压状态是否属于当前映射表中的可信结束码。
        /// 与 AT9620 监控收工白名单同源：命中则按仪器结论落库/翻译；未命中则上层可记为通信异常。
        /// </summary>
        /// <param name="status">SQLite 或内存中的耐压状态文本，可带 ACW/DCW 模式前缀。</param>
        /// <returns>去除模式前缀后的状态属于运行时映射 Code 时返回 true。</returns>
        public static bool IsTrustedTerminalStatus(string status)
        {
            string text = RemoveTestModePrefix(status)?.Trim() ?? string.Empty;
            return !string.IsNullOrEmpty(text) && _runtimeMappings.ContainsKey(text);
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
        /// 从独立 XML 加载状态映射。
        /// 文件中每个 Map 的 Code 既是显示翻译键，也是可信结束态码；现场增加仪器新结束码时须同时写 Code 与 Display。
        /// 文件格式：
        /// &lt;StatusMappings&gt;
        ///   &lt;Map Code="PASS" Display="测试合格" /&gt;
        ///   &lt;Map Code="SHORT" Display="短路保护" /&gt;
        /// &lt;/StatusMappings&gt;
        /// </summary>
        /// <param name="xmlFilePath">配置目录下「耐压状态映射.xml」的完整路径。</param>
        /// <param name="errorMessage">加载失败时的错误说明，成功时为空。</param>
        /// <returns>成功解析并 Reload 时返回 true；文件缺失或 XML 异常时返回 false。</returns>
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
                    new XComment(" Code 同时作为仪器可信结束态白名单；新增现场结束码（如 HI-LIMIT、LO-LIMIT）须写入 Code "),
                    new XComment(" 修改后需重启软件生效；同名 Code 覆盖中文，默认码表不会因删行而消失 "),
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

