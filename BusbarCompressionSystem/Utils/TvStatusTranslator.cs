using System;
using System.Collections.Generic;
using System.Linq;
using BusbarCompressionSystem.Model;

namespace BusbarCompressionSystem.Utils
{
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
    }
}

