using GalaSoft.MvvmLight;
using BusbarCompressionSystem.Model.Record;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace BusbarCompressionSystem.Model.Record1
{
    /// <summary>
    /// IR绝缘电阻测试实时数据模型
    /// </summary>
    public class IRTestModel : ObservableObject
    {
        public Productinfo Productinfo { get; set; } = new Productinfo();

        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// 绝缘电阻（单位由仪器返回，通常为 Ohm）
        /// </summary>
        public double Resistance { get; set; }

        /// <summary>
        /// 漏电流（单位由仪器返回）
        /// </summary>
        public double LeakCurrent { get; set; }

        /// <summary>
        /// 分选结果（如 GD / NG）
        /// </summary>
        public string Judgment { get; set; } = string.Empty;

        /// <summary>
        /// IR仪器编号
        /// </summary>
        public string IRMeterID { get; set; } = string.Empty;

        /// <summary>
        /// IR原始返回字符串（包含电阻、漏电流、判定）
        /// </summary>
        public string IRInfo { get; set; } = string.Empty;
    }
}

