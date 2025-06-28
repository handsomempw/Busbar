using BusbarCompressionSystem.Model;
using BusbarCompressionSystem.ViewModel;
using HalconDotNet;
using Panuon.WPF.UI;
using SQLITEDATABASE;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace BusbarCompressionSystem
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : WindowX
    {
        ViewModelLocator vml = null;
        public MainWindow()
        {
            InitializeComponent();
            vml = this.FindResource("Locator") as ViewModelLocator;
        }

        private void WindowX_Loaded(object sender, RoutedEventArgs e)
        {
            vml.Main.LoadSettingModel();
            vml.Main.LoadRecordModel();
            vml.Main.LoadProcessmodel();
            inittvparameter();
            vml.Main.InitAt9620();
            vml.Main.PLC_Start();
            vml.Main.InitCamera();
            InitHwindow();
            vml.Main.InitRobotServer();
        }


        public void InitHwindow()
        {
            vml.Main.DataModel.Settingmodel.HWindow1 = Hwindow1.HalconWindow;
            vml.Main.DataModel.Settingmodel.HWindow2 = Hwindow2.HalconWindow;
            vml.Main.DataModel.Settingmodel.HWindow3 = Hwindow3.HalconWindow;
            vml.Main.DataModel.Settingmodel.HWindow4 = Hwindow4.HalconWindow;
        }

        private void WindowX_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (MessageBoxX.Show("是否确定关闭运行软件?", "提示", MessageBoxButton.YesNo, MessageBoxIcon.Question) == MessageBoxResult.Yes)
            {
                vml.Main.SaveSettingModel();
                vml.Main.SaveRecordModel();
                vml.Main.SaveProcessmodel();
                vml.Main.CloseCamera();
                Environment.Exit(0);
            }
            else
            {
                e.Cancel = true;
            }
        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                vml.Main.ScanSN();
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            vml.Main.ScanSN();
        }



        private void inittvparameter()
        {

            //vml.Main.DataModel.Processmodel.TVParameter.RiseTime = 5;
            //vml.Main.DataModel.Processmodel.TVParameter.FallTime = 5;
            //vml.Main.DataModel.Processmodel.TVParameter.TestTime = 20;
            //vml.Main.DataModel.Processmodel.TVParameter.Voltage = 50;
            //vml.Main.DataModel.Processmodel.TVParameter.High = 10;
            //vml.Main.DataModel.Processmodel.TVParameter.Low = 0;
            vml.Main.DataModel.Settingmodel.AT9620_1.TVParameter = vml.Main.DataModel.Processmodel.TVParameter;
            vml.Main.DataModel.Settingmodel.AT9620_2.TVParameter = vml.Main.DataModel.Processmodel.TVParameter;
            vml.Main.DataModel.Settingmodel.AT9620_3.TVParameter = vml.Main.DataModel.Processmodel.TVParameter;
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {

            new Thread(() =>
            {
                vml.Main.DataModel.Settingmodel.AT9620_1.Start();
                vml.Main.DataModel.Settingmodel.AT9620_2.Start();
                vml.Main.DataModel.Settingmodel.AT9620_3.Start();

            }).Start();



        }

        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            new Thread(() =>
            {
                var r1 = vml.Main.DataModel.Settingmodel.AT9620_1.Download();
                var r2 = vml.Main.DataModel.Settingmodel.AT9620_2.Download();
                var r3 = vml.Main.DataModel.Settingmodel.AT9620_3.Download();
                if (!r1.Success)
                {
                    NoticeBox.Show("耐压工位1参数下发失败", "错误", MessageBoxIcon.Error);
                }
                else if (!r2.Success)
                {
                    NoticeBox.Show("耐压工位2参数下发失败", "错误", MessageBoxIcon.Error);
                }
                else if (!r3.Success)
                {
                    NoticeBox.Show("耐压工位3参数下发失败","错误",MessageBoxIcon.Error);
                }

                else
                {
                    NoticeBox.Show("参数下发完成","成功",MessageBoxIcon.Success,true,5000);
                }
            }).Start();
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBoxX.Show("是否确定清空数据", "提示", MessageBoxButton.YesNo, MessageBoxIcon.Question, DefaultButton.NoCancel) != MessageBoxResult.Yes)
            {
                return;
            }
            vml.Main.DataModel.Recordmodel.ProductInfoRecords.Clear();

        }

        private void Button_Click_3(object sender, RoutedEventArgs e)
        {
            WOCODEINPUTFORM Wf = new WOCODEINPUTFORM();
            Wf.ShowDialog();
        }

        private void MenuItem_Click_1(object sender, RoutedEventArgs e)
        {
            if (MessageBoxX.Show("是否确定清空数据", "提示", MessageBoxButton.YesNo, MessageBoxIcon.Question, DefaultButton.NoCancel) != MessageBoxResult.Yes)
            {
                return;
            }


            for (int i = vml.Main.DataModel.Recordmodel.ProductInfoRecords.Count - 1; i >= 10; i--)
            {
                try
                {
                    vml.Main.DataModel.Recordmodel.ProductInfoRecords.RemoveAt(i);
                }
                catch (Exception ex) {; }
            }
        }

        private void check1_Click(object sender, RoutedEventArgs e)
        {
            sqlite.Check1("N2T050A510-2", "BTBBC3607861D001", "MT01736413");
        }
    }
}
