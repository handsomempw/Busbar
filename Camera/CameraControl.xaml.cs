using HalconDotNet;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Camera
{
    /// <summary>
    /// CameraControl.xaml 的交互逻辑
    /// </summary>
    public partial class CameraControl : UserControl
    {
        Camera.DATA DATA = null;
        public CameraControl()
        {
            InitializeComponent();
            try
            {
                DATA.CameraModel.HWindow = HWindow;
                DATA.CameraModel.camera.ShowWindow = HWindow;
            }
            catch (Exception ex) {; }
        }

        //public HWindowControlWPF GetHwindow()
        //{
        //    return HWindow;
        //}
        public HSmartWindowControlWPF GetHwindow()
        {
            return HWindow;
        }
        private void GetData()
        {
            try
            {
                if (DATA == null)
                {
                    DATA = (DATA)this.DataContext;
                }
            }
            catch (Exception ex) {; }
        }

        private void bnContinuesMode_Checked(object sender, RoutedEventArgs e)
        {
            GetData();
            DATA.CameraModel.camera.bnContinuesMode_CheckedChanged();
        }

        private void bnTriggerMode_Checked(object sender, RoutedEventArgs e)
        {
            GetData();
            DATA.CameraModel.camera.bnTriggerMode_CheckedChanged();
        }

        private void cbSoftTrigger_Checked(object sender, RoutedEventArgs e)
        {
            GetData();
            DATA.CameraModel.camera.cbSoftTrigger_CheckedChanged();
        }

        private void UserControl_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            GetData();


        }
        public void refreshHwindow()
        {
            try
            {
                DATA.CameraModel.HWindow = HWindow;
                DATA.CameraModel.camera.ShowWindow = HWindow;
            }
            catch (Exception ex) {; }
        }

        private void bnSetParam_Click(object sender, RoutedEventArgs e)
        {

        }

        
    }
}
