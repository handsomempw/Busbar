using BusbarCompressionSystem.ViewModel;
using Panuon.WPF.UI;
using System;
using System.Collections.Generic;
using System.IO;
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
    /// Inputbox.xaml 的交互逻辑
    /// </summary>
    public partial class Inputbox : WindowX
    {
        ViewModelLocator vml = null;
        public Inputbox()
        {
            InitializeComponent();
            vml = (ViewModelLocator)this.FindResource("Locator");
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {

            this.DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }

        private void WindowX_Loaded(object sender, RoutedEventArgs e)
        {

            try
            {
                string dir = $"{vml.Main.DataModel.FaraVisionDataModel.Settingmodel.Prjdir}\\{vml.Main.DataModel.FaraVisionDataModel.Settingmodel.Name}";
                for (int i = 0; i < 100; i++)
                {
                    string filename = $"{dir}\\main{i}.bmp";

                    Tool_Picnumber.Items.Add(i.ToString());

                    if (!File.Exists(filename))
                    {
                        break;
                    }
                }

                if (Tool_Picnumber.Items.Count > 0)
                {
                    Tool_Picnumber.SelectedIndex = Tool_Picnumber.Items.Count - 1;
                }
            }
            catch {; }

        }
    }
}
