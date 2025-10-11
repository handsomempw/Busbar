using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
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
            sn = string.Empty;
            wocode = string.Empty;

            sn = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.DecodeSN(textBox1.Text);

            textBox5.Text = sn;

        }

        private void button3_Click(object sender, EventArgs e)
        {
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
        }

        private void button4_Click(object sender, EventArgs e)
        {
            var r = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.check_productNO(sn, textBox3.Text);
            label6.Text = $"工序校验结果：{r}";

        }

        private void button5_Click(object sender, EventArgs e)
        {

            string StandardCode = textBox8.Text.Trim();
            string StationCode = textBox2.Text.Trim();
            string ProductSN = string.Empty;
            string WO_CODE = textBox6.Text.Trim();
            string ComponentSN = textBox1.Text.Trim();


            var r = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.BindProductAndRawMaterial(StandardCode, StationCode, ProductSN, WO_CODE, ComponentSN);

            if (r.Success)
            {
                MessageBox.Show($"绑定成功:\r\n成品编号:{r.SN} ");
            }
            else
            {
                MessageBox.Show($"绑定失败:\r\n{r.ErrorInfo}");
            }

        }

        private void button2_Click(object sender, EventArgs e)
        {

            wocode = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.get_WO_CODE(sn);
            textBox6.Text = wocode;


            string partNO_ID = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.get_PartNO_ID(sn);
            textBox7.Text = partNO_ID;

        }



    }
}
