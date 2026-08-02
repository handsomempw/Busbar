using HalconDotNet;

namespace PositionDetect
{
    /// <summary>
    /// 模板示教阶段的单个区域项，供特征列表展示与建模域计算使用。
    /// 包含区参与建模；排除区在生成形状模型前从包含区中减去，用于屏蔽反光、字符等干扰边缘。
    /// 该对象只存在于模型设置窗口会话中，不写入工程 XML。
    /// </summary>
    public class TemplateFeatureItem
    {
        /// <summary>
        /// 区域类别文案，例如“矩形”“圆形”，用于列表显示。
        /// </summary>
        public string ShapeName { get; set; } = "区域";

        /// <summary>
        /// true 表示排除区（不参与建模）；false 表示包含区。
        /// </summary>
        public bool IsExclude { get; set; }

        /// <summary>
        /// HALCON 区域对象，单位 px。
        /// </summary>
        public HObject Region { get; set; }

        public override string ToString()
        {
            return IsExclude ? $"排除-{ShapeName}" : $"包含-{ShapeName}";
        }
    }
}
