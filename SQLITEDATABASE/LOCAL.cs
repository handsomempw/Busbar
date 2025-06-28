using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data.SQLite;
using System.Security.Policy;
using System.IO;
using System.Reflection.Emit;

namespace SQLITEDATABASE
{
    public partial class sqlite
    {
        public static bool CREATENEWLINE(string WOCODE, string PARTNOID, string SN)
        {
            try
            {
                string _connstr = CheckDataBase(WOCODE, PARTNOID, SN);

                if (!string.IsNullOrEmpty(_connstr))
                {

                    string sql = $"INSERT INTO BusbarCompressionData(PARTNOID,WOCODE,SN)VALUES('{PARTNOID}','{WOCODE}','{SN}')";

                    int c = excute_sql(sql, _connstr);
                    return c > 0;
                }
            }
            catch
            {; }
            return false;
        }
        public static bool UpdateTakePhoto1(string WOCODE, string PARTNOID, string SN, bool TakePhoto1)
        {
            try
            {
                string _connstr = CheckDataBase(WOCODE, PARTNOID, SN);

                if (!string.IsNullOrEmpty(_connstr))
                {
                    string sql = $"UPDATE BusbarCompressionData SET TAKEPHOTO1 ={(TakePhoto1 ? 1 : 0)} WHERE id=(SELECT max(id) from BusbarCompressionData WHERE sn='{SN}')";
                    int c = excute_sql(sql, _connstr);
                    return c > 0;
                }
            }
            catch
            {; }
            return false;
        }
        public static bool UpdateTV(string WOCODE, string PARTNOID, string SN, float RES, float MaxVoltage, bool TVResult)
        {

            try
            {
                string _connstr = CheckDataBase(WOCODE, PARTNOID, SN);

                if (!string.IsNullOrEmpty(_connstr))
                {
                    string sql = $"UPDATE BusbarCompressionData SET RES={RES},TVMAXVOLTAGE={MaxVoltage}, TVRESULT={(TVResult ? 1 : 0)} WHERE id=(SELECT max(id) from BusbarCompressionData WHERE sn='{SN}')";
                    int c = excute_sql(sql, _connstr);
                    return c > 0;
                }
            }
            catch
            {; }
            return false;
        }
        public static bool UpdateTakePhoto2(string WOCODE, string PARTNOID, string SN, bool TakePhoto2)
        {
            try
            {
                string _connstr = CheckDataBase(WOCODE, PARTNOID, SN);

                if (!string.IsNullOrEmpty(_connstr))
                {
                    string sql = $"UPDATE BusbarCompressionData SET TAKEPHOTO2 ={(TakePhoto2 ? 1 : 0)} WHERE id=(SELECT max(id) from BusbarCompressionData WHERE sn='{SN}')";
                    int c = excute_sql(sql, _connstr);
                    return c > 0;
                }
            }
            catch
            {; }
            return false;
        }
        public static string CheckDataBase(string WOCODE, string PARTNOID, string SN)
        {
            try
            {
                string defaultdb = $"{Environment.CurrentDirectory}\\数据库\\default.db";
                //string db = $"{Environment.CurrentDirectory}\\数据库\\{PARTNOID}\\{WOCODE}\\{SN}.db";
                string db = Path.Combine(Environment.CurrentDirectory, "数据库", $"{WOCODE}.db");

                string dir = Path.GetDirectoryName(db);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }



                if (!File.Exists(db))
                {
                    if (File.Exists(defaultdb))
                    {
                        File.Copy(defaultdb, db);
                        return $"Data Source={db};Pooling=true;FailIfMissing=false";
                    }
                }
                else
                {
                    return $"Data Source={db};Pooling=true;FailIfMissing=false";
                }
            }
            catch (Exception e)
            {
                ;
            }

            return string.Empty;
        }

        public static int Check1(string WOCODE, string PARTNOID, string SN)
        {
            try
            {
                string _connstr = CheckDataBase(WOCODE, PARTNOID, SN);

                if (!string.IsNullOrEmpty(_connstr))
                {
                    string sql = $"SELECT SN, TAKEPHOTO1, RES, TVMAXVOLTAGE, TVRESULT FROM BusbarCompressionData  where ID=(SELECT max(ID)  FROM BusbarCompressionData WHERE sn='{SN}')";
                    DataTable dt = Read(sql, _connstr);
                    if (dt != null && dt.Rows.Count > 0)
                    {
                        bool _takephoto1 = false;
                        if (!bool.TryParse(dt.Rows[0]["TAKEPHOTO1"].ToString(), out _takephoto1))
                        { return 1; }

                        float _tvmaxvoltage = -1;
                        if (!float.TryParse(dt.Rows[0]["TVMAXVOLTAGE"].ToString(), out _tvmaxvoltage))
                        {
                            return 2;
                        }

                        float _res = -1;
                        if (!float.TryParse(dt.Rows[0]["RES"].ToString(), out _res))
                        {
                            return 3;
                        }
                        bool _tvresult = false;
                        if (!bool.TryParse(dt.Rows[0]["TVRESULT"].ToString(), out _tvresult))
                        { return 2; }



                        if (!_takephoto1)
                        {
                            return 1;
                        }
                        if (_tvmaxvoltage == 0 || _tvmaxvoltage == -1)
                        {
                            return 2;
                        }
                        if (!_tvresult)
                        {
                            return 2;
                        }
                        if (_res == 0)
                        {
                            return 3;
                        }


                        return 0;
                    }
                }


            }
            catch (Exception e)
            {
            }

            return 1;
        }
        public static int Check2(string WOCODE, string PARTNOID, string SN)
        {
            try
            {
                string _connstr = CheckDataBase(WOCODE, PARTNOID, SN);

                if (!string.IsNullOrEmpty(_connstr))
                {
                    string sql = $"SELECT SN, TAKEPHOTO1, RES, TVMAXVOLTAGE, TVRESULT,TAKEPHOTO2 FROM BusbarCompressionData  where ID=(SELECT max(ID)  FROM BusbarCompressionData WHERE sn='{SN}')";
                    DataTable dt = Read(sql, _connstr);
                    if (dt != null && dt.Rows.Count > 0)
                    {
                        bool _takephoto1 = false;
                        if (!bool.TryParse(dt.Rows[0]["TAKEPHOTO1"].ToString(), out _takephoto1))
                        { return 1; }

                        float _tvmaxvoltage = -1;
                        if (!float.TryParse(dt.Rows[0]["TVMAXVOLTAGE"].ToString(), out _tvmaxvoltage))
                        {
                            return 2;
                        }
                        bool _tvresult = false;
                        if (!bool.TryParse(dt.Rows[0]["TVRESULT"].ToString(), out _tvresult))
                        { return 2; }

                        float _res = -1;
                        if (!float.TryParse(dt.Rows[0]["RES"].ToString(), out _res))
                        {
                            return 3;
                        }

                        bool _takephoto2 = false;
                        if (!bool.TryParse(dt.Rows[0]["TAKEPHOTO2"].ToString(), out _takephoto2))
                        { return 4; }

                        if (!_takephoto1)
                        {
                            return 1;
                        }
                        if (_tvmaxvoltage == 0 || _tvmaxvoltage == -1)
                        {
                            return 2;
                        }
                        if (!_tvresult)
                        {
                            return 2;
                        }
                        if (_res == 0)
                        {
                            return 3;
                        }

                        if (!_takephoto2)
                        {
                            return 4;
                        }

                        return 0;
                    }
                }


            }
            catch (Exception e)
            {
            }

            return 1;
        }
    }


    public partial class sqlite
    {
        //string connStr = @"Data Source=" + @"D:\sqlliteDb\document.db;Initial Catalog=sqlite;Integrated Security=True;Max Pool Size=10";        
        public static string connstr = @"Data Source=database\record.db;Pooling=true;FailIfMissing=false";

        #region 读取数据按内部链接来
        public static DataTable Read(string sql)
        {
            return Read(sql, connstr);
        }
        public static string ReadString(String sql)
        {
            return ReadString(sql, connstr);
        }
        public static int excute_sql(string sql)
        {
            return excute_sql(sql, connstr);
        }
        #endregion


        #region 读取数据按外部链接来

        public static DataTable Read(string sql, string connstring)
        {
            try
            {
                SQLiteConnection conn = new SQLiteConnection(connstring);
                conn.Open();
                SQLiteDataAdapter oda = new SQLiteDataAdapter(sql, conn);
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
        public static string ReadString(String sql, string connstring)
        {
            try
            {
                SQLiteConnection conn = new SQLiteConnection(connstring);
                conn.Open();
                SQLiteCommand odc = new SQLiteCommand(sql, conn);
                SQLiteDataReader reader = odc.ExecuteReader();
                reader.Read();
                string s = reader.GetValue(0).ToString();
                conn.Close();
                conn.Dispose();
                odc.Dispose();
                return (s);
            }
            catch
            {
                return null;
            }
        }
        public static int excute_sql(string sql, string connstring)
        {
            try
            {
                SQLiteConnection conn = new SQLiteConnection(connstring);
                conn.Open();
                SQLiteCommand odc = new SQLiteCommand(sql, conn);
                int x = odc.ExecuteNonQuery();
                conn.Close();
                conn.Dispose();
                odc.Dispose();
                return x;
            }
            catch { return 0; }
        }

        #endregion
    }
}
