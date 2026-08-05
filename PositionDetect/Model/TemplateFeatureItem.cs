using HalconDotNet;
using System.Xml.Serialization;

namespace PositionDetect
{
    /// <summary>
    /// 模板示教区域的几何类型。
    /// 当前操作界面支持包含矩形、包含圆形和排除矩形；枚举值写入 Tool XML，供重新进入模型设置时恢复可编辑区域。
    /// </summary>
    public enum TemplateFeatureShapeType
    {
        [XmlEnum("矩形")]
        Rectangle,

        [XmlEnum("圆形")]
        Circle
    }

    /// <summary>
    /// 单个模板示教区域的可持久化定义。
    /// 坐标均使用原始示教图像坐标，单位为 px；Tool XML 保存该对象，工具复制、重排和工程快照沿用现有 XML 文件边界。
    /// </summary>
    public class TemplateFeatureDefinition
    {
        /// <summary>
        /// 区域几何类型。矩形读取 Row1/Col1/Row2/Col2，圆形读取 CenterRow/CenterCol/Radius。
        /// </summary>
        [XmlElement("区域类型")]
        public TemplateFeatureShapeType ShapeType { get; set; } = TemplateFeatureShapeType.Rectangle;

        /// <summary>
        /// 区域用途。false 表示参与建模的包含区，true 表示从包含区中扣除的干扰屏蔽区。
        /// 当前界面只创建矩形排除区，旧工程缺少该节点时按包含区兼容。
        /// </summary>
        [XmlElement("是否排除")]
        public bool IsExclude { get; set; }

        /// <summary>矩形起始行坐标，单位 px。</summary>
        [XmlElement("矩形起始行")]
        public double Row1 { get; set; }

        /// <summary>矩形起始列坐标，单位 px。</summary>
        [XmlElement("矩形起始列")]
        public double Col1 { get; set; }

        /// <summary>矩形结束行坐标，单位 px。</summary>
        [XmlElement("矩形结束行")]
        public double Row2 { get; set; }

        /// <summary>矩形结束列坐标，单位 px。</summary>
        [XmlElement("矩形结束列")]
        public double Col2 { get; set; }

        /// <summary>圆心行坐标，单位 px。</summary>
        [XmlElement("圆心行")]
        public double CenterRow { get; set; }

        /// <summary>圆心列坐标，单位 px。</summary>
        [XmlElement("圆心列")]
        public double CenterCol { get; set; }

        /// <summary>圆形区域半径，单位 px。</summary>
        [XmlElement("圆半径")]
        public double Radius { get; set; }

        /// <summary>
        /// 复制当前几何定义，隔离模型窗口编辑会话与 ToolModel 已保存配方。
        /// 会话内增删区域只有在模型发布成功时才替换工具配方。
        /// </summary>
        /// <returns>包含相同几何参数的独立定义对象。</returns>
        public TemplateFeatureDefinition Clone()
        {
            return new TemplateFeatureDefinition
            {
                ShapeType = ShapeType,
                IsExclude = IsExclude,
                Row1 = Row1,
                Col1 = Col1,
                Row2 = Row2,
                Col2 = Col2,
                CenterRow = CenterRow,
                CenterCol = CenterCol,
                Radius = Radius
            };
        }
    }

    /// <summary>
    /// 模板示教阶段的单个区域项，供特征列表展示与建模域计算使用。
    /// 包含区参与建模；排除区在生成形状模型前从包含区中减去，用于屏蔽反光、字符等干扰边缘。
    /// HALCON Region 只存在于模型设置窗口会话中；Definition 由 ToolModel 写入工程 XML。
    /// </summary>
    public class TemplateFeatureItem
    {
        /// <summary>
        /// 当前区域的可持久化几何定义。模型窗口关闭后由该定义重新创建 HALCON Region。
        /// </summary>
        public TemplateFeatureDefinition Definition { get; set; } = new TemplateFeatureDefinition();

        /// <summary>
        /// 区域类别文案，用于特征列表显示。
        /// </summary>
        public string ShapeName
        {
            get { return Definition?.ShapeType == TemplateFeatureShapeType.Circle ? "圆形" : "矩形"; }
        }

        /// <summary>
        /// true 表示排除区，false 表示包含区。
        /// </summary>
        public bool IsExclude
        {
            get { return Definition?.IsExclude == true; }
        }

        /// <summary>
        /// HALCON 区域对象，坐标单位为 px；窗口关闭时释放，不参与 XML 序列化。
        /// </summary>
        public HObject Region { get; set; }

        public override string ToString()
        {
            return IsExclude ? $"排除-{ShapeName}" : $"包含-{ShapeName}";
        }
    }
}
