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
using System.Diagnostics; // {{ AURA-X: Add - 添加Stopwatch用于性能计时. Approval: 寸止(ID:20250120). }}
using System.Threading; // {{ AURA-X: Add - 添加Thread用于获取线程ID. Approval: 寸止(ID:20250120). }}

namespace SQLITEDATABASE
{
    public partial class sqlite
    {
        private static object perfLogLocker = new object(); // {{ AURA-X: Add - 性能日志文件锁. Approval: 寸止(ID:20250120). }}

        /// <summary>
        /// {{ AURA-X: Add - 添加SQLite操作性能诊断日志方法. Approval: 寸止(ID:20250120). }}
        /// 写入性能诊断日志到独立文件
        /// </summary>
        private static void WritePerfLog(string tag, string message, long? elapsedMs = null, string extraInfo = null)
        {
            try
            {
                int threadId = Thread.CurrentThread.ManagedThreadId;
                string threadName = Thread.CurrentThread.Name ?? $"Thread-{threadId}";

                // 检测是否为异常耗时（超过1秒）
                bool isAbnormalTime = elapsedMs.HasValue && elapsedMs.Value > 1000;
                string perfLevel = isAbnormalTime ? "PERF-异常" : "PERF";

                StringBuilder logBuilder = new StringBuilder();
                logBuilder.Append($"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}]");
                logBuilder.Append($"[{threadName}]");
                logBuilder.Append($"[SQLITE_{tag}]");
                logBuilder.Append($"[{perfLevel}]");
                logBuilder.Append($"[LOCAL_DB] {message}");

                if (elapsedMs.HasValue)
                {
                    string timeDisplay = isAbnormalTime ?
                        $"耗时={elapsedMs.Value}ms[异常!!!]" :
                        $"耗时={elapsedMs.Value}ms";
                    logBuilder.Append($" | {timeDisplay}");
                }

                if (!string.IsNullOrEmpty(extraInfo))
                {
                    logBuilder.Append($" | {extraInfo}");
                }

                string logContent = logBuilder.ToString();

                string filename = $"{Environment.CurrentDirectory}\\日志\\性能诊断\\{DateTime.Now.ToString("yyyyMMdd")}_performance.log";
                string dir = Path.GetDirectoryName(filename);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                lock (perfLogLocker)
                {
                    using (StreamWriter sw = new StreamWriter(filename, true, Encoding.UTF8))
                    {
                        sw.WriteLine(logContent);
                        sw.Close();
                    }
                }
            }
            catch
            {
                // 性能日志失败不应影响业务
            }
        }

        public static bool CREATENEWLINE(string WOCODE, string PARTNOID, string SN, String STATIONCODE, string EQUIPMENTID, DateTime dt)
        {
            // {{ AURA-X: Modify - 添加性能诊断日志. Approval: 寸止(ID:20250120). }}
            Stopwatch swTotal = Stopwatch.StartNew();
            WritePerfLog("CREATENEWLINE_START", "SQLite写入开始", extraInfo: $"SN={SN}, WOCODE={WOCODE}");

            try
            {
                Stopwatch sw1 = Stopwatch.StartNew();
                string _connstr = CheckDataBase(WOCODE, PARTNOID, SN);
                sw1.Stop();
                WritePerfLog("CHECKDATABASE", "CheckDataBase完成", sw1.ElapsedMilliseconds);

                if (!string.IsNullOrEmpty(_connstr))
                {

                    string sql1 = $"SELECT STATIONCODE FROM BusbarCompressionData WHERE  PARTNOID='{PARTNOID}' AND EQUIPMENTID='{EQUIPMENTID}' AND   WOCODE='{WOCODE}'  AND SN='{SN}' AND STATIONCODE={STATIONCODE} AND DATETIME>='{(dt.AddSeconds(-10))}'";

                    Stopwatch sw2 = Stopwatch.StartNew();
                    DataTable dt1 = Read(sql1);
                    sw2.Stop();
                    WritePerfLog("SELECT_CHECK", "重复检查查询完成", sw2.ElapsedMilliseconds, $"RowCount={dt1?.Rows.Count ?? 0}");

                    if (dt1 == null || dt1.Rows.Count <= 0)
                    {
                        string sql = $"INSERT INTO BusbarCompressionData(PARTNOID,WOCODE,SN,EQUIPMENTID,STATIONCODE,DATETIME)VALUES('{PARTNOID}','{WOCODE}','{SN}','{EQUIPMENTID}','{STATIONCODE}','{dt}')";

                        Stopwatch sw3 = Stopwatch.StartNew();
                        int c = excute_sql(sql, _connstr);
                        sw3.Stop();
                        WritePerfLog("INSERT_EXECUTE", "INSERT执行完成", sw3.ElapsedMilliseconds, $"AffectedRows={c}");

                        swTotal.Stop();
                        bool success = c > 0;
                        WritePerfLog(success ? "CREATENEWLINE_SUCCESS" : "CREATENEWLINE_FAILED",
                            "SQLite写入完成", swTotal.ElapsedMilliseconds, $"Success={success}");
                        return success;
                    }

                    else
                    {
                        swTotal.Stop();
                        WritePerfLog("CREATENEWLINE_SKIPPED", "SQLite写入跳过(记录已存在)", swTotal.ElapsedMilliseconds);
                        return true;

                    }

                }
                else
                {
                    swTotal.Stop();
                    WritePerfLog("CREATENEWLINE_FAILED", "SQLite写入失败(连接字符串为空)", swTotal.ElapsedMilliseconds);
                }
            }
            catch (Exception ex)
            {
                swTotal.Stop();
                WritePerfLog("CREATENEWLINE_EXCEPTION", "SQLite写入异常", swTotal.ElapsedMilliseconds, $"Error={ex.Message}");
            }
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


        public static bool UpdatePressure(string WOCODE, string PARTNOID, string SN, float AveragePressure, float MaxPressure, float MinPressure, bool PressureResult)
        {
            try
            {
                string _connstr = CheckDataBase(WOCODE, PARTNOID, SN);

                if (!string.IsNullOrEmpty(_connstr))
                {
                    string sql = $"UPDATE BusbarCompressionData SET PRESSURE_RESULT ={(PressureResult ? 1 : 0)},PRESSURE_MAX={MaxPressure},PRESSURE_AVERAGE={AveragePressure},PRESSURE_MIN={MinPressure} WHERE id=(SELECT max(id) from BusbarCompressionData WHERE sn='{SN}')";
                    int c = excute_sql(sql, _connstr);
                    return c > 0;
                }
            }
            catch {; }
            return false;
        }
        public static bool UpdateTV(string WOCODE, string PARTNOID, string SN, float RES, float MaxVoltage, bool TVResult, float MaxCurrent, string TVInfo, string TVMeterID)
        {

            try
            {
                string _connstr = CheckDataBase(WOCODE, PARTNOID, SN);

                if (!string.IsNullOrEmpty(_connstr))
                {
                    string sql = $"UPDATE BusbarCompressionData SET RES={RES},TVMAXVOLTAGE={MaxVoltage},TVMAXCURRENT={MaxCurrent},TVRESULT={(TVResult ? 1 : 0)},TVMeterID='{TVMeterID}',TVInfo='{TVInfo}' WHERE id=(SELECT max(id) from BusbarCompressionData WHERE sn='{SN}')";
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
                        bool _tvresult = false;
                        if (!bool.TryParse(dt.Rows[0]["TVRESULT"].ToString(), out _tvresult))
                        { return 2; }


                        float _res = -1;
                        if (!float.TryParse(dt.Rows[0]["RES"].ToString(), out _res))
                        {
                            return 3;
                        }




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
