using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.IO;

namespace F7DataBase
{
    public class Sqlserver
    {


        #region 数据库基本操作
        private static string connenstrf7 = "Provider=SQLOLEDB.1;Password=f7data;Persist Security Info=True;User ID=f7;Initial Catalog=F7Data;Data Source=192.168.95.107";

        private string SQL_CONNSTR = connenstrf7;
        public Boolean excutesql_sql(String sql)
        {
            try
            {
                using (OleDbConnection conn = new OleDbConnection(SQL_CONNSTR))
                {
                    conn.Open();
                    var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.CommandType = CommandType.Text;

                    cmd.ExecuteNonQuery();
                    conn.Close();
                    conn.Dispose();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                return false;
            }
        }
        public DataTable Read_sql(string sql)
        {
            try
            {
                OleDbConnection conn = new OleDbConnection(SQL_CONNSTR);
                conn.Open();
                OleDbDataAdapter oda = new OleDbDataAdapter(sql, conn);
                DataTable dt = new DataTable();
                oda.Fill(dt);
                conn.Close();
                conn.Dispose();
                oda.Dispose();
                return dt;
            }
            catch
            {
                return null;
            }
        }
        public string ReadString_sql(string sql)
        {
            try
            {
                using (OleDbConnection conn = new OleDbConnection(SQL_CONNSTR))
                {
                    conn.Open();
                    OleDbCommand odc = new OleDbCommand(sql, conn);
                    var reader = odc.ExecuteReader();
                    reader.Read();
                    string s = reader.GetValue(0).ToString();
                    conn.Close();
                    conn.Dispose();
                    odc.Dispose();
                    return (s);
                }
            }
            catch (Exception ex)
            {
                return null;
            }
        }
        #endregion


        #region 耐压过程数据存储
        public bool Save_TVProcessData(string WO_CODE, string SN, string PROCERURENAME, string RESULT, string WORKERID, DateTime DT, string TVMETERID, string PROCESSDATA)
        {
            try
            {
                string sql = $"INSERT INTO [F7Data].[dbo].[dr_TVProcess] ( [FBatch], [FStep], [FSerial], [FTVResult], [FOpdate], [FOpman], [FMachine], [FProcess]) VALUES " +
                    $"( '{WO_CODE}', '{PROCERURENAME}', '{SN}', '{RESULT}', '{DT.ToString("yyyy-MM-dd HH:mm:ss.FFF")}', '{WORKERID}', '{TVMETERID}', '{PROCESSDATA}');";

                bool R = true;
                int I = 0;
                while (I++ < 3)
                {
                    R &= excutesql_sql(sql);
                    if (R) { break; }
                    Thread.Sleep(100);
                }

                if (!R)
                {
                    #region 保存耐压过程数据到本地
                    Save_TVProcessData_Local(WO_CODE, SN, PROCERURENAME, RESULT, WORKERID, DT, TVMETERID, PROCESSDATA);

                    #endregion
                }
                return R;

            }
            catch (Exception ex) { return false; }
        }

        private bool Save_TVProcessData_Local(string WO_CODE, string SN, string PROCERURENAME, string RESULT, string WORKERID, DateTime DT, string TVMETERID, string PROCESSDATA)
        {

            try
            {
                string filename = $"{Environment.CurrentDirectory}\\保存错误数据\\耐压过程数据\\{DateTime.Now.ToString("yyyy-MM-dd")}\\{WO_CODE}.txt";
                string dir = Path.GetDirectoryName(filename);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                using (StreamWriter sw = new StreamWriter(filename))
                {
                    sw.WriteLine($"{WO_CODE}${SN}${PROCERURENAME}${RESULT}${WORKERID}${DT}${TVMETERID}${PROCESSDATA}");
                    sw.Close();
                }

                return true;
            }
            catch (Exception ex) { return false; }


            #endregion

        }
    }
}
