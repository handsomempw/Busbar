using PositionDetect.ViewModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.TextFormatting;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace PositionDetect
{
    /// <summary>
    /// UserControl1.xaml 的交互逻辑
    /// </summary>
    public partial class UserControl1 : UserControl
    {
        //public ViewModelLocator vml = null;
        //DATA data = null;
        PositionDetectViewModel vm = null;
        public UserControl1()
        {
            InitializeComponent();
            //vml = (ViewModelLocator)this.FindResource("Locator");
            //vml.Main.DATA.HWindow = HWindow;
            // data=(DATA)DataContext;
            vm = (PositionDetectViewModel)DataContext;
        }





        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (!vm.DATA.ROImode)
            {
                ClearTemplateFeatures();
            }
            vm.DATA.ROImode = !vm.DATA.ROImode;


        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            vm.showtest();
            vm.showregion();
            vm.drawrectangle();
            vm.showregion();

        }

        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            vm.showtest();
            vm.showregion();
            vm.drawcircle();
            vm.showregion();

        }

        private void DrawExcludeRectangle_Click(object sender, RoutedEventArgs e)
        {
            vm.showtest();
            vm.showregion();
            vm.drawExcludeRectangle();
            vm.showregion();
        }

        /// <summary>
        /// 清空当前示教会话中的包含区与排除区，并释放 HALCON 区域句柄。
        /// </summary>
        private void ClearTemplateFeatures()
        {
            foreach (var item in vm.DATA.Objects.ToList())
            {
                try { item?.Region?.Dispose(); } catch { }
            }
            vm.DATA.Objects.Clear();
        }

        private void Button_Click_3(object sender, RoutedEventArgs e)
        {
            vm.OutputModel();
        }

        private void Button_Click_4(object sender, RoutedEventArgs e)
        {
            vm.showreduce();

        }

        private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            vm.showtest();
            vm.showregion();
        }

        private void Button_Click_5(object sender, RoutedEventArgs e)
        {
            vm.showdetect();
        }

        private void ListBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                vm.deletelistitem();
            }
        }

        private void ListBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
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

        private void DeleteSelectedFeature_Click(object sender, RoutedEventArgs e)
        {
            vm.deletelistitem();
        }

        private void UserControl_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            vm = (PositionDetectViewModel)DataContext;
            //vm.DATA.HWindow = HWindow;
            vm.DATA.HWindow = HWindow;
        }
    }
}
