using AT9620;
using BusbarCompressionSystem.Model;
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
    /// WOCODEINPUTFORM.xaml 的交互逻辑
    /// </summary>
    public partial class WOCODEINPUTFORM : WindowX
    {
        ViewModelLocator vml { get; set; }
        public WOCODEINPUTFORM()
        {
            InitializeComponent();
            vml = (ViewModelLocator)this.FindResource("Locator");
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {


            try
            {
                string partnoid = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.Get_PARTNOID_BY_WOCODE(vml.Main.DataModel.Processmodel.wocodeinputstr);
                if (string.IsNullOrEmpty(partnoid))
                {
                    MessageBoxX.Show("未获取到规格信息，请检查批号是否正确");
                    return;
                }

                var ps = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.Get_Parameters(
                    vml.Main.DataModel.Settingmodel.SETTING_DATA.MachineID,
                    vml.Main.DataModel.Processmodel.wocodeinputstr,
                    vml.Main.DataModel.Settingmodel.SETTING_DATA.StandardCode
                    );



                var r1 = ps.Where(p => p.ParameterName == "测试电压");
                var r2 = ps.Where(p => p.ParameterName == "电流类型");
                var r3 = ps.Where(p => p.ParameterName == "上升时间");
                var r4 = ps.Where(p => p.ParameterName == "测试时间");
                var r5 = ps.Where(p => p.ParameterName == "下降时间");
                var r6 = ps.Where(p => p.ParameterName == "测试电流");
                var r7 = ps.Where(p => p.ParameterName == "充电电流下限");
                var r8 = ps.Where(p => p.ParameterName == "测试频率");

                string error = "缺少以下工艺参数:\r\n";
                bool r = true;
                if (r1.Count() == 0)
                {
                    error += "测试电压\r\n";
                    r = false;
                }
                if (r2.Count() == 0)
                {
                    error += "电流类型\r\n";
                    r = false;
                }
                if (r3.Count() == 0)
                {
                    error += "上升时间\r\n";
                    r = false;
                }
                if (r4.Count() == 0)
                {
                    error += "测试时间\r\n";
                    r = false;
                }
                if (r5.Count() == 0)
                {
                    error += "下降时间\r\n";
                    r = false;
                }
                if (r6.Count() == 0)
                {
                    error += "测试电流\r\n";
                    r = false;
                }
                if (r7.Count() == 0)
                {
                    error += "充电电流下限\r\n";
                    r = false;
                }
                if (r8.Count() == 0)
                {
                    error += "测试频率\r\n";
                    r = false;
                }


                vml.Main.DataModel.Processmodel.TVParameter.Voltage = Convert.ToSingle(r1.First().TargetValue);
                vml.Main.DataModel.Processmodel.TVParameter.TestMode = (TestMode)Convert.ToInt16(r2.First().TargetValue);
                vml.Main.DataModel.Processmodel.TVParameter.RiseTime = Convert.ToSingle(r3.First().TargetValue);
                vml.Main.DataModel.Processmodel.TVParameter.TestTime = Convert.ToSingle(r4.First().TargetValue);
                vml.Main.DataModel.Processmodel.TVParameter.FallTime = Convert.ToSingle(r5.First().TargetValue);
                vml.Main.DataModel.Processmodel.TVParameter.High = Convert.ToSingle(r6.First().TargetValue);
                vml.Main.DataModel.Processmodel.TVParameter.Low = Convert.ToSingle(r7.First().TargetValue);
                vml.Main.DataModel.Processmodel.TVParameter.Freq = Convert.ToSingle(r8.First().TargetValue);



                if (r)
                {

                    var r11 = vml.Main.DataModel.Settingmodel.AT9620_1.Download();
                    var r12 = vml.Main.DataModel.Settingmodel.AT9620_2.Download();
                    var r13 = vml.Main.DataModel.Settingmodel.AT9620_3.Download();
                    if (!r11.Success)
                    {
                        NoticeBox.Show("耐压工位1参数下发失败", "错误", MessageBoxIcon.Error);
                    }
                    else if (!r12.Success)
                    {
                        NoticeBox.Show("耐压工位2参数下发失败", "错误", MessageBoxIcon.Error);
                    }
                    else if (!r13.Success)
                    {
                        NoticeBox.Show("耐压工位3参数下发失败", "错误", MessageBoxIcon.Error);
                    }

                    else
                    {
                        NoticeBox.Show("参数下发完成", "成功", MessageBoxIcon.Success, true, 5000);
                        vml.Main.DataModel.Processmodel.PartNOID = partnoid;
                    }
                }
                else
                {
                    MessageBoxX.Show(error, MessageBoxIcon.Error);


                }
            }
            catch (Exception ex)
            {

                MessageBoxX.Show(ex.ToString(), "错误", MessageBoxIcon.Error);
            }

        }
    }
}
