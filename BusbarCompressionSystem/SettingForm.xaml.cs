/*
 * 系统设置窗口
 *
 * 提供系统配置的图形化界面：
 * 1. 硬件参数设置：相机、PLC、测试仪器等设备的配置
 * 2. 生产参数调整：测试标准、流程参数等设置
 * 3. 界面参数配置：显示选项、语言等个性化设置
 * 4. 参数验证和保存：确保设置的合理性和持久化保存
 *
 * 作用：让操作员能够方便地调整系统运行参数，无需修改代码
 */

using BusbarCompressionSystem.ViewModel;
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

namespace BusbarCompressionSystem
{
    /// <summary>
    /// SettingForm.xaml 的交互逻辑
    /// </summary>
    public partial class SettingForm : WindowX

    {

        ViewModelLocator vml;
        public SettingForm()
        {
            InitializeComponent();

            vml = this.FindResource("Locator") as ViewModelLocator;

        }


        private void TV1Tb_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (vml.Main.DataModel.Processmodel.TVAvailable.TV2Available)
                {
                    TV2Tb.Focus();
                    TV2Tb.SelectAll();

                }
                else if (vml.Main.DataModel.Processmodel.TVAvailable.TV3Available)
                {
                    TV3Tb.Focus();
                    TV3Tb.SelectAll();
                }
                else
                {
                    config1.Focus();
                }
            }
        }

        private void TV2Tb_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (vml.Main.DataModel.Processmodel.TVAvailable.TV3Available)
                {
                    TV3Tb.Focus();
                    TV3Tb.SelectAll();
                }
                else
                {
                    config1.Focus();
                }
            }
        }

        private void TV3Tb_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {

                config1.Focus();

            }
        }

        private void config1_Click(object sender, RoutedEventArgs e)
        {
            if (vml.Main.DataModel.Processmodel.TVAvailable.TV1Available && string.IsNullOrEmpty(vml.Main.DataModel.Settingmodel.SETTING_DATA.TVMeterID1))
            {
                NoticeBox.Show("耐压仪器1编号不能为空");
                TV1Tb.Focus();
                return;
            }
            else if (vml.Main.DataModel.Processmodel.TVAvailable.TV2Available && string.IsNullOrEmpty(vml.Main.DataModel.Settingmodel.SETTING_DATA.TVMeterID2))
            {
                NoticeBox.Show("耐压仪器2编号不能为空");
                TV2Tb.Focus();
                return;
            }
            else if (vml.Main.DataModel.Processmodel.TVAvailable.TV3Available && string.IsNullOrEmpty(vml.Main.DataModel.Settingmodel.SETTING_DATA.TVMeterID3))
            {
                NoticeBox.Show("耐压仪器3编号不能为空");
                TV3Tb.Focus();
                return;
            }
            vml.Main.DataModel.Processmodel.CheckData.TVMeterCheckTime = DateTime.Now;
        }

        private void WindowX_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyDualYTv3UiPolicy();

            double H = (DateTime.Now - vml.Main.DataModel.Processmodel.CheckData.TVMeterCheckTime).TotalHours;

            if (H > 12)
            {
                NoticeBox.Show("请重新扫描仪器编号", "提示", MessageBoxIcon.Warning, true, 6000);
                vml.Main.DataModel.Settingmodel.SETTING_DATA.TVMeterID1 = string.Empty;
                vml.Main.DataModel.Settingmodel.SETTING_DATA.TVMeterID2 = string.Empty;
                vml.Main.DataModel.Settingmodel.SETTING_DATA.TVMeterID3 = string.Empty;

                TV1Tb.Focus();
            }
            else if (H > 11)
            {
                if (MessageBoxX.Show("是否确定重新扫描仪器编号", "提示", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                {
                    Environment.Exit(0);
                }
                else
                {
                    TV1Tb.Focus();
                    TV1Tb.SelectAll();
                }
            }

        }

        /// <summary>
        /// 双Y专用部署机台无第三台耐压仪：固定取消仪器3勾选并禁用，避免操作员以为可手改；标准产线保持 M3032 镜像可编辑外观。
        /// </summary>
        private void ApplyDualYTv3UiPolicy()
        {
            if (vml?.Main == null || !vml.Main.IsDualYElectricalTestDeployment())
            {
                return;
            }

            vml.Main.DataModel.Processmodel.TVAvailable.TV3Available = false;
            TV3AvailableCb.IsEnabled = false;
            TV3AvailableCb.ToolTip = "双Y专用部署无耐压工位3仪器，软件强制忽略；参数下发与触发均不走 AT9620_3";
            TV3Tb.IsEnabled = false;
        }

        private void WindowX_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (vml.Main.DataModel.Processmodel.TVAvailable.TV1Available && string.IsNullOrEmpty(vml.Main.DataModel.Settingmodel.SETTING_DATA.TVMeterID1))
            {
                NoticeBox.Show("耐压仪器1编号不能为空");
                TV1Tb.Focus();
                e.Cancel = true;
               
            }
            else if (vml.Main.DataModel.Processmodel.TVAvailable.TV2Available && string.IsNullOrEmpty(vml.Main.DataModel.Settingmodel.SETTING_DATA.TVMeterID2))
            {
                NoticeBox.Show("耐压仪器2编号不能为空");
                TV2Tb.Focus();
                e.Cancel = true;
              
            }
            else if (vml.Main.DataModel.Processmodel.TVAvailable.TV3Available && string.IsNullOrEmpty(vml.Main.DataModel.Settingmodel.SETTING_DATA.TVMeterID3))
            {
                NoticeBox.Show("耐压仪器3编号不能为空");
                TV3Tb.Focus();
                e.Cancel = true;
          
            }

           // double H = (DateTime.Now - vml.Main.DataModel.Processmodel.CheckData.TVMeterCheckTime).TotalHours;

        }
    }
}
