using PositionDetect.ViewModel;
using Panuon.WPF.UI;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PositionDetect
{
    /// <summary>
    /// 模板示教图和特征侧栏的交互入口。
    /// 该控件维护当前窗口会话的绘制、选择与删除操作；配方持久化和 .shm 发布由外层模型设置窗口统一完成。
    /// </summary>
    public partial class UserControl1 : UserControl
    {
        //public ViewModelLocator vml = null;
        //DATA data = null;
        PositionDetectViewModel vm = null;
        /// <summary>
        /// 初始化示教控件并接入当前 PositionDetect 会话；HALCON 窗口在 DataContext 就绪后交给 ViewModel 使用。
        /// </summary>
        public UserControl1()
        {
            InitializeComponent();
            //vml = (ViewModelLocator)this.FindResource("Locator");
            //vml.Main.DATA.HWindow = HWindow;
            // data=(DATA)DataContext;
            vm = (PositionDetectViewModel)DataContext;
        }





        /// <summary>
        /// 切换特征圈选状态。恢复的区域始终保留在列表中，暂停圈选后仍可执行预览和发布。
        /// </summary>
        /// <param name="sender">继续编辑或暂停圈选按钮。</param>
        /// <param name="e">WPF 点击事件参数。</param>
        private void ToggleFeatureEditing_Click(object sender, RoutedEventArgs e)
        {
            vm.DATA.ROImode = !vm.DATA.ROImode;
            if (vm.DATA.ROImode)
            {
                vm.showtest();
                vm.showregion();
            }
        }

        /// <summary>
        /// 在原始示教图坐标中绘制包含矩形，并把区域加入当前可编辑配方。
        /// </summary>
        /// <param name="sender">画包含矩形按钮。</param>
        /// <param name="e">WPF 点击事件参数。</param>
        private void DrawIncludeRectangle_Click(object sender, RoutedEventArgs e)
        {
            vm.showtest();
            vm.showregion();
            vm.drawrectangle();
            vm.showregion();

        }

        /// <summary>
        /// 在原始示教图坐标中绘制包含圆形，并把区域加入当前可编辑配方。
        /// </summary>
        /// <param name="sender">画包含圆形按钮。</param>
        /// <param name="e">WPF 点击事件参数。</param>
        private void DrawIncludeCircle_Click(object sender, RoutedEventArgs e)
        {
            vm.showtest();
            vm.showregion();
            vm.drawcircle();
            vm.showregion();

        }

        /// <summary>
        /// 绘制排除矩形，用于从包含区中扣除反光、字符和阴影等干扰边缘。
        /// </summary>
        /// <param name="sender">画排除矩形按钮。</param>
        /// <param name="e">WPF 点击事件参数。</param>
        private void DrawExcludeRectangle_Click(object sender, RoutedEventArgs e)
        {
            vm.showtest();
            vm.showregion();
            vm.drawExcludeRectangle();
            vm.showregion();
        }

        /// <summary>
        /// 经操作员确认后清空本次会话区域并进入重新示教。
        /// 已发布 .shm 与 Tool XML 配方继续保留，模型窗口完成预览和发布后再替换工程内容。
        /// </summary>
        /// <param name="sender">模型设置侧栏的“重新示教”按钮。</param>
        /// <param name="e">WPF 点击事件参数。</param>
        private void ResetTemplateFeatures_Click(object sender, RoutedEventArgs e)
        {
            if (!vm.DATA.HasAnyTemplateFeatures)
            {
                return;
            }

            MessageBoxResult result = MessageBoxX.Show(
                "重新示教会清空当前窗口中的全部包含区和排除区。已发布模型继续生效，确认开始重新示教？",
                "重新示教",
                MessageBoxButton.YesNo,
                MessageBoxIcon.Question,
                DefaultButton.NoCancel);
            if (result == MessageBoxResult.Yes)
            {
                vm.StartNewTemplateTeaching();
            }
        }

        /// <summary>
        /// 刷新选中特征的红色强调显示，几何配方和临时模型状态保持由 ViewModel 管理。
        /// </summary>
        /// <param name="sender">模板特征列表。</param>
        /// <param name="e">列表选择变化参数。</param>
        private void FeatureList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            vm.showtest();
            vm.showregion();
        }

        /// <summary>
        /// 处理特征列表的 Delete 快捷删除入口，并沿用与可见删除按钮一致的确认流程。
        /// </summary>
        /// <param name="sender">模板特征列表。</param>
        /// <param name="e">键盘事件参数。</param>
        private void FeatureList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                vm.deletelistitem();
            }
        }

        /// <summary>
        /// 右键菜单打开前选中鼠标所在项，使删除命令作用于操作员当前指向的特征。
        /// </summary>
        /// <param name="sender">模板特征列表。</param>
        /// <param name="e">鼠标右键事件参数。</param>
        private void FeatureList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject source = e.OriginalSource as DependencyObject;
            while (source != null && !(source is ListBoxItem))
            {
                source = VisualTreeHelper.GetParent(source);
            }

            if (source is ListBoxItem item)
            {
                item.IsSelected = true;
                item.Focus();
            }
        }

        /// <summary>
        /// 删除当前选中的包含区或排除区。删除会使临时模型失效，发布文件继续保持当前版本。
        /// </summary>
        /// <param name="sender">删除按钮或右键菜单项。</param>
        /// <param name="e">WPF 点击事件参数。</param>
        private void DeleteSelectedFeature_Click(object sender, RoutedEventArgs e)
        {
            vm.deletelistitem();
        }

        /// <summary>
        /// 将当前 HALCON 窗口交给新的 PositionDetect 会话，供恢复区域、绘制特征和显示模型轮廓使用。
        /// </summary>
        /// <param name="sender">模板示教控件。</param>
        /// <param name="e">DataContext 切换事件参数。</param>
        private void UserControl_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            vm = (PositionDetectViewModel)DataContext;
            //vm.DATA.HWindow = HWindow;
            vm.DATA.HWindow = HWindow;
        }
    }
}
