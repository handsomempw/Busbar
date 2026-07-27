using System.Windows.Controls;

namespace BusbarCompressionSystem.View
{
    /// <summary>
    /// 双Y小屏生产视图。该视图提供两工位状态、过程记录和运行日志交互；
    /// 共享 MainViewModel 负责测试执行、PLC 反馈、SQLite 持久化和 MES 报工。
    /// </summary>
    public partial class DualYProductionView : UserControl
    {
        public DualYProductionView()
        {
            InitializeComponent();
        }
    }
}
