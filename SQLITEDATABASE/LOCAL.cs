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
using System.Diagnostics; // 添加Stopwatch用于性能计时
using System.Threading; // 添加Thread用于获取线程ID

namespace SQLITEDATABASE
{
    public partial class sqlite
    {
        private static object perfLogLocker = new object(); //性能日志文件锁
        private static object errorLogLocker = new object(); //数据库异常日志文件锁
        private static object dbFileLocker = new object(); //数据库文件操作锁

        /// <summary>
        /// SQLite模块专用错误日志
        /// 独立文件存储，便于问题定位和统计分析
        /// 输出路径：日志\数据库异常\{日期}.txt
        /// </summary>
        public static void WriteErrorLog(string tag, string message, string sn = "", string wocode = "")
        {
            try
            {
                DateTime now = DateTime.Now;
                // 独立的数据库异常日志目录
                string filename = $"{Environment.CurrentDirectory}\\日志\\数据库异常\\{now.ToString("yyyyMMdd")}.txt";
                string dir = Path.GetDirectoryName(filename);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                StringBuilder logBuilder = new StringBuilder();
                logBuilder.Append($"[{now.ToString("yyyy-MM-dd HH:mm:ss.fff")}]");
                logBuilder.Append($"[{tag}]");
                if (!string.IsNullOrEmpty(sn))
                {
                    logBuilder.Append($"[SN:{sn}]");
                }
                if (!string.IsNullOrEmpty(wocode))
                {
                    logBuilder.Append($"[WO:{wocode}]");
                }
                logBuilder.Append($" {message}");

                lock (errorLogLocker)
                {
                    using (StreamWriter sw = new StreamWriter(filename, true, Encoding.UTF8))
                    {
                        sw.WriteLine(logBuilder.ToString());
                        sw.Close();
                    }
                }
            }
            catch
            {
                // 日志写入失败不应影响业务
            }
        }

        public static bool CREATENEWLINE(string WOCODE, string PARTNOID, string SN, String STATIONCODE, string EQUIPMENTID, DateTime dt)
        {
            try
            {
                string _connstr = CheckDataBase(WOCODE, PARTNOID, SN);

                if (string.IsNullOrEmpty(_connstr))
                {
                    WriteErrorLog("[追踪]CREATENEWLINE-连接串为空",
                        "CheckDataBase返回空字符串，无法继续",
                        SN, WOCODE);
                    return false;
                }

                string sql1 = $"SELECT STATIONCODE FROM BusbarCompressionData WHERE  PARTNOID='{PARTNOID}' AND EQUIPMENTID='{EQUIPMENTID}' AND   WOCODE='{WOCODE}'  AND SN='{SN}' AND STATIONCODE={STATIONCODE} AND DATETIME>='{(dt.AddSeconds(-10))}'";

                DataTable dt1 = Read(sql1, _connstr);
                if (dt1 == null || dt1.Rows.Count <= 0)
                {
                    string sql = $"INSERT INTO BusbarCompressionData(PARTNOID,WOCODE,SN,EQUIPMENTID,STATIONCODE,DATETIME)VALUES('{PARTNOID}','{WOCODE}','{SN}','{EQUIPMENTID}','{STATIONCODE}','{dt}')";
                    int c = excute_sql(sql, _connstr);
                    return c > 0;
                }
                else
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                WriteErrorLog("[数据库异常]CREATENEWLINE-创建记录失败",
                    $"创建新记录时发生异常: {ex.Message}, 堆栈: {ex.StackTrace}",
                    SN, WOCODE);
            }
            return false;
        }
        public static bool UpdateTakePhoto1(string WOCODE, string PARTNOID, string SN, bool TakePhoto1)
        {
            try
            {
                string _connstr = CheckDataBase(WOCODE, PARTNOID, SN);

                if (string.IsNullOrEmpty(_connstr))
                {
                    WriteErrorLog("[追踪]UpdateTakePhoto1-连接串为空", "CheckDataBase返回空", SN, WOCODE);
                    return false;
                }

                string sql = $"UPDATE BusbarCompressionData SET TAKEPHOTO1 ={(TakePhoto1 ? 1 : 0)} WHERE id=(SELECT max(id) from BusbarCompressionData WHERE sn='{SN}')";
                int c = excute_sql(sql, _connstr);
                return c > 0;
            }
            catch (Exception ex)
            {
                WriteErrorLog("[数据库异常]UpdateTakePhoto1失败", $"异常: {ex.Message}", SN, WOCODE);
            }
            return false;
        }


        public static bool UpdatePressure(string WOCODE, string PARTNOID, string SN, float AveragePressure, float MaxPressure, float MinPressure, bool PressureResult)
        {
            try
            {
                string _connstr = CheckDataBase(WOCODE, PARTNOID, SN);

                if (string.IsNullOrEmpty(_connstr))
                {
                    WriteErrorLog("[追踪]UpdatePressure-连接串为空", "CheckDataBase返回空", SN, WOCODE);
                    return false;
                }

                string sql = $"UPDATE BusbarCompressionData SET PRESSURE_RESULT ={(PressureResult ? 1 : 0)},PRESSURE_MAX={MaxPressure},PRESSURE_AVERAGE={AveragePressure},PRESSURE_MIN={MinPressure} WHERE id=(SELECT max(id) from BusbarCompressionData WHERE sn='{SN}')";
                int c = excute_sql(sql, _connstr);
                return c > 0;
            }
            catch (Exception ex)
            {
                WriteErrorLog("[数据库异常]UpdatePressure失败", $"异常: {ex.Message}", SN, WOCODE);
            }
            return false;
        }
        public static bool UpdateTV(string WOCODE, string PARTNOID, string SN, float RES, float MaxVoltage, bool TVResult, float MaxCurrent, string TVInfo, string TVMeterID)
        {
            try
            {
                string _connstr = CheckDataBase(WOCODE, PARTNOID, SN);

                if (string.IsNullOrEmpty(_connstr))
                {
                    WriteErrorLog("[追踪]UpdateTV-连接串为空", "CheckDataBase返回空", SN, WOCODE);
                    return false;
                }

                string sql = $"UPDATE BusbarCompressionData SET RES={RES},TVMAXVOLTAGE={MaxVoltage},TVMAXCURRENT={MaxCurrent},TVRESULT={(TVResult ? 1 : 0)},TVMeterID='{TVMeterID}',TVInfo='{TVInfo}' WHERE id=(SELECT max(id) from BusbarCompressionData WHERE sn='{SN}')";
                int c = excute_sql(sql, _connstr);
                return c > 0;
            }
            catch (Exception ex)
            {
                WriteErrorLog("[数据库异常]UpdateTV失败", $"异常: {ex.Message}", SN, WOCODE);
            }
            return false;
        }
        public static bool UpdateTakePhoto2(string WOCODE, string PARTNOID, string SN, bool TakePhoto2)
        {
            try
            {
                string _connstr = CheckDataBase(WOCODE, PARTNOID, SN);

                if (string.IsNullOrEmpty(_connstr))
                {
                    WriteErrorLog("[追踪]UpdateTakePhoto2-连接串为空", "CheckDataBase返回空", SN, WOCODE);
                    return false;
                }

                string sql = $"UPDATE BusbarCompressionData SET TAKEPHOTO2 ={(TakePhoto2 ? 1 : 0)} WHERE id=(SELECT max(id) from BusbarCompressionData WHERE sn='{SN}')";
                int c = excute_sql(sql, _connstr);
                return c > 0;
            }
            catch (Exception ex)
            {
                WriteErrorLog("[数据库异常]UpdateTakePhoto2失败", $"异常: {ex.Message}", SN, WOCODE);
            }
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
                        WriteErrorLog("[数据库信息]CheckDataBase-新建数据库",
                            $"从模板创建新数据库: {db}", SN, WOCODE);
                        return $"Data Source={db};Pooling=true;FailIfMissing=false";
                    }
                    else
                    {
                        // 关键问题：模板数据库不存在
                        WriteErrorLog("[数据库异常]CheckDataBase-模板缺失",
                            $"模板数据库不存在: {defaultdb}，无法创建工单数据库", SN, WOCODE);
                        return string.Empty;
                    }
                }
                else
                {
                    return $"Data Source={db};Pooling=true;FailIfMissing=false";
                }
            }
            catch (Exception ex)
            {
                WriteErrorLog("[数据库异常]CheckDataBase失败",
                    $"数据库检查异常: {ex.Message}", SN, WOCODE);
            }

            return string.Empty;
        }

        /// <summary>
        /// 第一次综合校验：检查产品是否通过了拍照留底、耐压测试、阻值测试
        /// 返回值说明：
        ///   0 = 全部合格
        ///   1 = 拍照留底不良（TakePhoto1=false 或数据解析失败 或数据库异常）
        ///   2 = 耐压测试不合格（TVMaxVoltage异常 或 TVResult=false）
        ///   3 = 阻值测试不合格（RES=0 或数据解析失败）
        /// 注意：所有系统异常都会被映射为业务不良返回，通过独立日志详细记录实际原因
        /// </summary>
        /// <returns>错误代码：0=合格，1=拍照不良，2=耐压不良，3=阻值不良</returns>
        public static int Check1(string WOCODE, string PARTNOID, string SN)
        {
            try
            {
                string _connstr = CheckDataBase(WOCODE, PARTNOID, SN);

                if (string.IsNullOrEmpty(_connstr))
                {
                    // 【系统异常】数据库连接失败
                    WriteErrorLog("[数据库异常]CHECK1-连接失败",
                        "数据库连接字符串获取失败，数据库文件不存在或创建失败，返回值=1(伪装为拍照不良)",
                        SN, WOCODE);
                    return 1;
                }

                if (!string.IsNullOrEmpty(_connstr))
                {
                    string sql = $"SELECT SN, TAKEPHOTO1, RES, TVMAXVOLTAGE, TVRESULT FROM BusbarCompressionData  where ID=(SELECT max(ID)  FROM BusbarCompressionData WHERE sn='{SN}')";
                    DataTable dt = Read(sql, _connstr);
                    
                    if (dt == null || dt.Rows.Count <= 0)
                    {
                        // 【系统异常】数据不存在
                        WriteErrorLog("[数据缺失]CHECK1-记录不存在",
                            $"产品数据不存在，可能未扫码进站或数据库记录丢失，SQL=[{sql}]，返回值=1",
                            SN, WOCODE);
                        return 1;
                    }
                    
                    if (dt != null && dt.Rows.Count > 0)
                    {
                        // 1. 先解析拍照留底（必须字段）
                        bool _takephoto1 = false;
                        string s_takephoto1 = dt.Rows[0]["TAKEPHOTO1"]?.ToString();
                        
                        // 增加空值检查，区分"数据缺失"和"解析失败"
                        if (string.IsNullOrWhiteSpace(s_takephoto1))
                        {
                             WriteErrorLog("[数据缺失]CHECK1-字段为空-TAKEPHOTO1",
                                $"TAKEPHOTO1字段为空，可能是UpdateTakePhoto1执行失败，返回值=1",
                                SN, WOCODE);
                            return 1;
                        }

                        if (!bool.TryParse(s_takephoto1, out _takephoto1))
                        {
                            WriteErrorLog("[数据异常]CHECK1-字段解析错误-TAKEPHOTO1",
                                $"TAKEPHOTO1字段解析失败，原始值=[{s_takephoto1}]，返回值=1",
                                SN, WOCODE);
                            return 1;
                        }

                        if (!_takephoto1)
                        {
                            return 1;
                        }

                        // 2. 解析阻值（必须字段）
                        float _res = -1;
                        string s_res = dt.Rows[0]["RES"]?.ToString();
                        
                        if (string.IsNullOrWhiteSpace(s_res))
                        {
                             WriteErrorLog("[数据缺失]CHECK1-字段为空-RES",
                                "RES字段为空，可能是UpdateTV执行失败，返回值=3",
                                SN, WOCODE);
                            return 3;
                        }

                        if (!float.TryParse(s_res, out _res))
                        {
                            WriteErrorLog("[数据异常]CHECK1-字段解析错误-RES",
                                $"RES字段解析失败，原始值=[{s_res}]，返回值=3",
                                SN, WOCODE);
                            return 3;
                        }

                        // 3. 优先判断阻值，阻值不良时无需检查耐压（PLC可能未执行耐压测试）
                        if (_res > 14)
                        {
                            return 3;
                        }

                        // 4. 阻值合格，继续解析耐压字段
                        float _tvmaxvoltage = -1;
                        string s_tvmaxvoltage = dt.Rows[0]["TVMAXVOLTAGE"]?.ToString();
                        
                        if (string.IsNullOrWhiteSpace(s_tvmaxvoltage))
                        {
                             WriteErrorLog("[数据缺失]CHECK1-字段为空-TVMAXVOLTAGE",
                                "TVMAXVOLTAGE字段为空，可能是UpdateTV执行失败，返回值=2",
                                SN, WOCODE);
                            return 2;
                        }

                        if (!float.TryParse(s_tvmaxvoltage, out _tvmaxvoltage))
                        {
                            WriteErrorLog("[数据异常]CHECK1-字段解析错误-TVMAXVOLTAGE",
                                $"TVMAXVOLTAGE字段解析失败，原始值=[{s_tvmaxvoltage}]，返回值=2",
                                SN, WOCODE);
                            return 2;
                        }
                        bool _tvresult = false;
                        string s_tvresult = dt.Rows[0]["TVRESULT"]?.ToString();
                        
                        if (string.IsNullOrWhiteSpace(s_tvresult))
                        {
                            // TVRESULT为空也认为耐压数据缺失
                             WriteErrorLog("[数据缺失]CHECK1-字段为空-TVRESULT",
                                "TVRESULT字段为空，可能是UpdateTV执行失败，返回值=2",
                                SN, WOCODE);
                            return 2;
                        }

                        if (!bool.TryParse(s_tvresult, out _tvresult))
                        {
                            WriteErrorLog("[数据异常]CHECK1-字段解析错误-TVRESULT",
                                $"TVRESULT字段解析失败，原始值=[{s_tvresult}]，返回值=2",
                                SN, WOCODE);
                            return 2;
                        }

                        // 5. 判断耐压测试结果
                        if (_tvmaxvoltage == 0 || _tvmaxvoltage == -1)
                        {
                            return 2;
                        }
                        if (!_tvresult)
                        {
                            return 2;
                        }

                        return 0;
                    }
                }


            }
            catch (Exception ex)
            {
                // 【系统异常】未捕获的异常
                WriteErrorLog("[系统异常]CHECK1-未捕获异常",
                    $"未捕获异常，类型=[{ex.GetType().Name}]，消息=[{ex.Message}]，堆栈=[{ex.StackTrace}]，返回值=1",
                    SN, WOCODE);
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
            catch (Exception ex)
            {
                WriteErrorLog("[数据库异常]excute_sql执行失败",
                    $"SQL执行异常: {ex.Message}, SQL=[{sql}]");
                return 0;
            }
        }

        #endregion
    }
}
