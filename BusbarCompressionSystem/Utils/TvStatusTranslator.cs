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

