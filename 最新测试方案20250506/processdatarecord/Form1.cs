using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace processdatarecord
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }


        string partid = "PC366W084J00275000";
        string wocode = "67S0609212";
        string SN = "7Y14725654";
        string group_Name = DateTime.Now.ToString("yyyyMMddHHmmssFFF");
        //string group_Name = DateTime.Now.ToString("123456");
        string procedurename = "测试数据";

        string[] titles =
        {
                "C01_P1_计算值",
                "C01_P2_计算值"                ,
                "C01_P3_计算值"                ,
                "C01_P4_计算值"                ,
                "C01_P5_计算值"                ,
                "C01_P6_计算值"                ,
                "C01_P7_计算值"                ,
                "C01_P8_计算值"                ,
                "C01_P9_计算值"                ,
                "C01_P10_计算值"               ,
                "C01_P11_计算值"               ,
                "C01_P12_计算值"               ,
                "C01_计算值"                   ,
                "F01_P1_计算值"                ,
                "F01_P2_计算值"                ,
                "F01_P3_计算值"                ,
                "F01_P4_计算值"                ,
                "F01_P5_计算值"                ,
                "F01_P6_计算值"                ,
                "F01_P7_计算值"                ,
                "F01_P8_计算值"                ,
                "F01_P9_计算值"                ,
                "F01_P10_计算值"               ,
                "F01_计算值"

            };

        double[] Datas = new double[]
        {
                0.233,0.23,0.171,0.094,0.088,0.044,0.068,0.032,-0.026,0.06,0.025,-0.037,0.466,-0.054,0.02,0.025,0.015,0.001,0.006,0.068,0.011,-0.029,-0.064,0.137
        };

        double[] Nominal_values1 = new double[]
        {
                0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0
        };


        double[] Upper_Tol = new double[]
        {
                0.2,0.2,0.2,0.2,0.2,0.2,0.2,0.2,0.2,0.2,0.2,0.2,0.4,0.125,0.125,0.125,0.125,0.125,0.125,0.125,0.125,0.125,0.125,0.25
         };

        double[] Lower_Tol =
        {
              -0.2,-0.2,  -0.2,-0.2,-0.2,-0.2,-0.2,-0.2,-0.2,-0.2,-0.2,-0.2,-0.2,-0.20,-0.125,-0.125,-0.125,-0.125,-0.125,-0.125,-0.125,-0.125,-0.125,-0.1250
            };


        private void button1_Click(object sender, EventArgs e)
        {
            var r1 = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.SaveProcedureTitle(procedurename,wocode, group_Name, titles);
            var r2 = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.SaveProcedureParameter(procedurename, wocode, group_Name, titles, Nominal_values1, Upper_Tol, Lower_Tol);

        }

        private void button2_Click(object sender, EventArgs e)
        {
            var r3 = MES_ORACLE_DATABASE.MES_ORACLE_DATABASE.SaveProcedureData(procedurename, partid, wocode, SN, group_Name, titles, Datas);
        }
    }
}
