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
        /// <summary>
        /// 用户输入的动态密码（仅在 DialogResult=true 时有效）
        /// </summary>
        public string InputValue { get; private set; } = string.Empty;

        public InputPassword()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            check();

        }


        public void check()
        {
            try
            {
                // 原先这里是“固定口令比对”，现改为动态密码输入框，仅做格式校验并返回输入值
                string value = (passwordbox.Password ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    MessageBox.Show("请输入6位动态密码");
                    return;
                }

                if (value.Length != 6 || value.Any(c => c < '0' || c > '9'))
                {
                    MessageBox.Show("动态密码格式不正确（应为6位数字）");
                    return;
                }

                InputValue = value;
                this.DialogResult = true;
                return;
            }
            catch {; }

            MessageBox.Show("动态密码输入异常，请重试");

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
