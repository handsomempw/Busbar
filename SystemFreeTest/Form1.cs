using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using SystemFree;

namespace SystemFreeTest
{
    public partial class Form1 : Form
    {
        SystemFree.FreeTest FreeTest=new SystemFree.FreeTest();
        public Form1()
        {
            InitializeComponent();
            FreeTest.SystemFreeReceived += FreeTest_SystemFreeReceived;
            FreeTest.Start(20);
        }

        private void FreeTest_SystemFreeReceived(object sender, EventArgs e)
        {
            MessageBox.Show($"空闲超过设定时间{(e as SystemFreeEventArgs).Seconds}");
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            //var r = SystemFree.FreeTest.GetLastInputTime();
            //this.BeginInvoke(new Action(() =>
            //{
            //    label1.Text = r.ToString();
            //}));
        }
    }
}
