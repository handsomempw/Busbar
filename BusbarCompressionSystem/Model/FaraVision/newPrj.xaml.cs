using Panuon.WPF.UI;
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
using System.Windows.Shapes;

namespace BusbarCompressionSystem.Model
{
    /// <summary>
    /// newPrj.xaml 的交互逻辑
    /// </summary>
    public partial class newPrj : WindowX
    {
        public string Prj_Name = string.Empty;
        public newPrj()
        {
            InitializeComponent();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string s = Prj_TextBox.Text.Trim().ToUpper();
                if (!string.IsNullOrEmpty(s))
                {
                    Prj_Name = s;
                    this.DialogResult = true;
                    return;
                }
            }
            catch (Exception ex) {; }

            NoticeBox.Show("请输入新建工程名称", "提示", MessageBoxIcon.Warning, true, 5000);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }
    }
}
