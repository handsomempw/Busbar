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

namespace BusbarCompressionSystem.Model.FaraVision
{
    /// <summary>
    /// InputPassword.xaml 的交互逻辑
    /// </summary>
    public partial class InputPassword : WindowX
    {
        string p = string.Empty;
        public InputPassword()
        {
            InitializeComponent();
        }
        public InputPassword(string p)
        {
            InitializeComponent();
            this.p = p;
        }
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            check();

        }


        public void check()
        {
            try
            {
                if (p.ToUpper() == passwordbox.Password.ToUpper())
                {
                    this.DialogResult = true;
                    return;
                }
            }
            catch {; }

            MessageBox.Show("密码错误");

        }
        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }

        private void passwordbox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                check();
            }
        }
    }
}
