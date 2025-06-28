using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using MES_ORACLE_DATABASE;

namespace oracletest
{
    public partial class Form1 : Form
    {
        string sn, wocode;
        public Form1()
        {
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            try
            {
                textBox5.Text = string.Empty;
                textBox6.Text = string.Empty;
                textBox9.Text = string.Empty;
                textBox10.Text = string.Empty;

                sn = string.Empty;
                wocode = string.Empty;

                Stopwatch sw = Stopwatch.StartNew();
                sn = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.DecodeSN(textBox1.Text);
                sw.Stop();
                Debug.WriteLine(sw.ElapsedMilliseconds.ToString());
                label10.Text = sw.ElapsedMilliseconds.ToString() + "ms";
                textBox5.Text = sn;
               // textBox10.Text = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.__NEWSN;
            }
            catch (Exception ex) {; }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            Stopwatch sw = Stopwatch.StartNew();
            var r = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.Save_EquipmentRecord_mes(
                textBox2.Text,
                wocode,
                sn,
                textBox3.Text,
                textBox4.Text,
                "不合格",
                textBox2.Text
                   );
            MessageBox.Show(r ? "报工完成" : "报工失败");
            sw.Stop();
            Debug.WriteLine(sw.ElapsedMilliseconds.ToString());
            label13.Text = sw.ElapsedMilliseconds.ToString() + "ms";
        }

        private void button4_Click(object sender, EventArgs e)
        {
            Stopwatch sw = Stopwatch.StartNew();
            var r = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.check_productNO(sn, textBox3.Text);
            sw.Stop();
            Debug.WriteLine(sw.ElapsedMilliseconds.ToString());
            label12.Text = sw.ElapsedMilliseconds.ToString() + "ms";
            label6.Text = $"工序校验结果：{r}";
        }

        private void button5_Click(object sender, EventArgs e)
        {
            string StandardCode = textBox8.Text.Trim();
            string StationCode = textBox2.Text.Trim();
            string ProductSN = string.Empty;
            string WO_CODE = textBox6.Text.Trim();
            string ComponentSN = textBox1.Text.Trim();

            var r = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.
                BindProductAndRawMaterial(StandardCode, StationCode, ProductSN, WO_CODE, ComponentSN);

            if (r.Success)
            {
                MessageBox.Show($"绑定成功:\r\n成品编号:{r.SN} ");
            }
            else
            {
                MessageBox.Show($"绑定失败:\r\n{r.ErrorInfo}");
            }
        }

        private void label13_Click(object sender, EventArgs e)
        {

        }

        private void button6_Click(object sender, EventArgs e)
        {
            MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.Get_Parameters("E11FXJ028600", "N2T050A511", "00035");
         //   MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.Get_Parameters("E11FXJ028600", "N2T050A511", "0002081949");
        }

        private void button2_Click(object sender, EventArgs e)
        {
            try
            {
                Stopwatch sw = Stopwatch.StartNew();

                //      var r = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.GetProductInfo(sn);

                wocode = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.get_WO_CODE(sn);
                textBox6.Text = wocode;

                string partNO_ID = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.get_PartNO_ID(sn);
                textBox7.Text = partNO_ID;
                sw.Stop();
                Debug.WriteLine(sw.ElapsedMilliseconds);
                label11.Text = sw.ElapsedMilliseconds.ToString() + "ms";

               // textBox9.Text = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.__NEW_WOCODE;
            }
            catch (Exception ex) {; }
        }


    }
}
