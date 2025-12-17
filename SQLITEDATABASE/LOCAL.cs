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

        /// <summary>
        /// 添加SQLite操作性能诊断日志方法
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

        /// <summary>
        /// 创建新的产品记录到本地SQLite数据库
        /// 业务逻辑：当产品扫码进站时，初始化该产品的测试记录
        /// 防重逻辑：10秒内相同产品编号不重复插入
        /// </summary>
        /// <param name="WOCODE">工单号</param>
        /// <param name="PARTNOID">产品料号</param>
        /// <param name="SN">产品序列号</param>
        /// <param name="STATIONCODE">工站代码</param>
        /// <param name="EQUIPMENTID">设备ID</param>
        /// <param name="dt">操作时间</param>
        /// <returns>创建成功或已存在返回true，失败返回false</returns>
        public static bool CREATENEWLINE(string WOCODE, string PARTNOID, string SN, String STATIONCODE, string EQUIPMENTID, DateTime dt)
        {
            Stopwatch swTotal = Stopwatch.StartNew(); // 总执行时间计时器
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
        /// <summary>
        /// 更新产品的第一次拍照留底结果
        /// 业务逻辑：在拍照留底工位完成后，记录三个相机的拍照是否全部成功
        /// 数据定位：通过SN查找最新的记录进行更新
        /// </summary>
        /// <param name="TakePhoto1">拍照是否成功（true=成功，false=超时或失败）</param>
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
            catch (Exception ex)
            {
               WriteErrorLog("UPDATE_TAKEPHOTO1_ERROR", $"更新拍照1结果失败: {ex.Message}", SN, WOCODE);
            }
            return false;
        }


        /// <summary>
        /// 更新产品的压力测试数据
        /// 业务逻辑：记录铜排电测过程中的压力监控数据（平均值、最大值、最小值）
        /// 判定标准：最大值≤设定上限 且 最小值≥设定下限
        /// </summary>
        /// <param name="PressureResult">压力测试是否合格</param>
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
            catch (Exception ex)
            {
                WriteErrorLog("UPDATE_PRESSURE_ERROR", $"更新压力结果失败: {ex.Message}", SN, WOCODE);
            }
            return false;
        }
        /// <summary>
        /// 更新产品的耐压测试数据（TV = Test Voltage）
        /// 业务逻辑：记录AT9620耐压测试仪的测试结果，包括电阻值、最大电压、最大电流等
        /// 应用场景：有3个耐压测试工位（TV1/TV2/TV3），每个产品需要通过其中一个工位的测试
        /// </summary>
        /// <param name="RES">电阻值（单位：欧姆）</param>
        /// <param name="MaxVoltage">测试过程中的最大电压值</param>
        /// <param name="TVResult">耐压测试是否合格</param>
        /// <param name="MaxCurrent">测试过程中的最大电流值</param>
        /// <param name="TVInfo">测试状态信息（如"PASS"/"FAIL"）</param>
        /// <param name="TVMeterID">测试仪器编号（标识是哪台耐压仪）</param>
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
            catch (Exception ex)
            {
                WriteErrorLog("UPDATE_TV_ERROR", $"更新耐压结果失败: {ex.Message}", SN, WOCODE);
            }
            return false;
        }
        /// <summary>
        /// 更新产品的第二次拍照（外观检测/AOI）结果
        /// 业务逻辑：在外观检测工位完成后，记录AOI视觉检测是否合格
        /// 区别于TakePhoto1：这个是对产品缺陷进行AOI识别判断，而不仅仅是留底
        /// </summary>
        /// <param name="TakePhoto2">AOI外观检测是否合格</param>
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
            catch (Exception ex)
            {
                WriteErrorLog("UPDATE_TAKEPHOTO2_ERROR", $"更新拍照2结果失败: {ex.Message}", SN, WOCODE);
            }
            return false;
        }
        /// <summary>
        /// 检查并准备数据库连接字符串
        /// 业务逻辑：按工单号（WOCODE）创建独立的SQLite数据库文件，实现数据隔离
        /// 设计思路：每个工单一个独立db文件，便于归档、迁移和追溯
        /// 自动初始化：如果数据库文件不存在，从default.db模板复制创建
        /// 线程安全：使用静态锁保护文件复制操作，防止并发复制导致的异常
        /// </summary>
        /// <returns>返回数据库连接字符串，失败返回空字符串</returns>
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
                    // 使用锁保护文件复制操作，防止多线程并发复制导致IOException
                    lock (dbFileLocker)
                    {
                        // 双重检查：锁内再次判断文件是否存在，避免重复复制
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
                            // 在等待锁期间，其他线程已经完成了复制
                            return $"Data Source={db};Pooling=true;FailIfMissing=false";
                        }
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

        /// <summary>
        /// 第一次综合校验：检查产品是否通过了前置工序的所有测试项目
        /// 业务场景：在CHECK1工位（外观检测前）进行的数据完整性和合格性校验
        /// 校验项目：拍照留底(TakePhoto1) + 耐压测试(TVResult) + 电阻测试(RES)
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
                        "数据库连接字符串获取失败，数据库文件不存在或创建失败，返回值:1(伪装为拍照不良)",
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
                            $"产品数据不存在，可能未扫码进站或数据库记录丢失，SQL=[{sql}]，返回值:1",
                            SN, WOCODE);
                        return 1;
                    }
                    
                    if (dt != null && dt.Rows.Count > 0)
                    {
                        // 1. 先解析拍照留底（必须字段）
                        bool _takephoto1 = false;
                        string s_takephoto1 = dt.Rows[0]["TAKEPHOTO1"]?.ToString();
                        
                        // 增加空值检查，区分“数据缺失”和“解析失败”
                        if (string.IsNullOrWhiteSpace(s_takephoto1))
                        {
                             WriteErrorLog("[数据缺失]CHECK1-字段为空-TAKEPHOTO1",
                                $"TAKEPHOTO1字段为空，可能是UpdateTakePhoto1执行失败，返回值:1",
                                SN, WOCODE);
                            return 1;
                        }

                        if (!bool.TryParse(s_takephoto1, out _takephoto1))
                        {
                            WriteErrorLog("[数据异常]CHECK1-字段解析错误-TAKEPHOTO1",
                                $"TAKEPHOTO1字段解析失败，原始值=[{s_takephoto1}]，返回值:1",
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
                                "RES字段为空，可能是UpdateTV执行失败，返回值:3",
                                SN, WOCODE);
                            return 3;
                        }

                        if (!float.TryParse(s_res, out _res))
                        {
                            WriteErrorLog("[数据异常]CHECK1-字段解析错误-RES",
                                $"RES字段解析失败，原始值=[{s_res}]，返回值:3",
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
                                "TVMAXVOLTAGE字段为空，可能是UpdateTV执行失败，返回值:2",
                                SN, WOCODE);
                            return 2;
                        }

                        if (!float.TryParse(s_tvmaxvoltage, out _tvmaxvoltage))
                        {
                            WriteErrorLog("[数据异常]CHECK1-字段解析错误-TVMAXVOLTAGE",
                                $"TVMAXVOLTAGE字段解析失败，原始值=[{s_tvmaxvoltage}]，返回值:2",
                                SN, WOCODE);
                            return 2;
                        }
                        bool _tvresult = false;
                        string s_tvresult = dt.Rows[0]["TVRESULT"]?.ToString();
                        
                        if (string.IsNullOrWhiteSpace(s_tvresult))
                        {
                            // TVRESULT为空也视为耐压数据缺失
                             WriteErrorLog("[数据缺失]CHECK1-字段为空-TVRESULT",
                                "TVRESULT字段为空，可能是UpdateTV执行失败，返回值:2",
                                SN, WOCODE);
                            return 2;
                        }

                        if (!bool.TryParse(s_tvresult, out _tvresult))
                        {
                            WriteErrorLog("[数据异常]CHECK1-字段解析错误-TVRESULT",
                                $"TVRESULT字段解析失败，原始值=[{s_tvresult}]，返回值:2",
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
                    $"未捕获异常，类型=[{ex.GetType().Name}]，消息=[{ex.Message}]，堆栈=[{ex.StackTrace}]，返回值:1",
                    SN, WOCODE);
            }

            return 1;
        }
        /// <summary>
        /// 第二次综合校验：检查产品是否通过了所有工序的测试项目（包括外观检测）
        /// 业务场景：在CHECK2工位（最终下料前）进行的全流程数据校验
        /// 校验项目：Check1的所有项 + 外观检测(TakePhoto2/AOI)
        /// 返回值说明：
        ///   0 = 全部合格
        ///   1 = 拍照留底不良（或数据库异常）
        ///   2 = 耐压测试不合格
        ///   3 = 阻值测试不合格
        ///   4 = AOI外观检测不合格（TakePhoto2=false）
        /// 设计思路：Check2是最终出站校验，确保所有工序数据完整且合格
        /// 注意：所有系统异常都会被映射为业务不良返回，通过独立日志详细记录实际原因
        /// </summary>
        /// <returns>错误代码：0=合格，1~3同Check1，4=外观不良</returns>
        public static int Check2(string WOCODE, string PARTNOID, string SN)
        {
            try
            {
                string _connstr = CheckDataBase(WOCODE, PARTNOID, SN);

                if (string.IsNullOrEmpty(_connstr))
                {
                    // 【系统异常】数据库连接失败
                    WriteErrorLog("[数据库异常]CHECK2-连接失败",
                        "数据库连接字符串获取失败，数据库文件不存在或创建失败，返回值:1(伪装为拍照不良)",
                        SN, WOCODE);
                    return 1;
                }

                if (!string.IsNullOrEmpty(_connstr))
                {
                    string sql = $"SELECT SN, TAKEPHOTO1, RES, TVMAXVOLTAGE, TVRESULT,TAKEPHOTO2 FROM BusbarCompressionData  where ID=(SELECT max(ID)  FROM BusbarCompressionData WHERE sn='{SN}')";
                    DataTable dt = Read(sql, _connstr);
                    
                    if (dt == null || dt.Rows.Count <= 0)
                    {
                        // 【系统异常】数据不存在
                        WriteErrorLog("[数据缺失]CHECK2-记录不存在",
                            $"产品数据不存在，可能未扫码进站或数据库记录丢失，SQL=[{sql}]，返回值:1",
                            SN, WOCODE);
                        return 1;
                    }
                    
                    if (dt != null && dt.Rows.Count > 0)
                    {
                        // 1. 先解析拍照留底（必须字段）
                        bool _takephoto1 = false;
                        string s_takephoto1 = dt.Rows[0]["TAKEPHOTO1"]?.ToString();
                        
                        // 增加空值检查
                        if (string.IsNullOrWhiteSpace(s_takephoto1))
                        {
                            WriteErrorLog("[数据缺失]CHECK2-字段为空-TAKEPHOTO1",
                                $"TAKEPHOTO1字段为空，可能是UpdateTakePhoto1执行失败，返回值:1",
                                SN, WOCODE);
                            return 1;
                        }

                        if (!bool.TryParse(s_takephoto1, out _takephoto1))
                        {
                            WriteErrorLog("[数据异常]CHECK2-字段解析错误-TAKEPHOTO1",
                                $"TAKEPHOTO1字段解析失败，原始值=[{s_takephoto1}]，返回值:1",
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
                            WriteErrorLog("[数据缺失]CHECK2-字段为空-RES",
                                "RES字段为空，可能是UpdateTV执行失败，返回值:3",
                                SN, WOCODE);
                            return 3;
                        }

                        if (!float.TryParse(s_res, out _res))
                        {
                            WriteErrorLog("[数据异常]CHECK2-字段解析错误-RES",
                                $"RES字段解析失败，原始值=[{s_res}]，返回值:3",
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
                            WriteErrorLog("[数据缺失]CHECK2-字段为空-TVMAXVOLTAGE",
                                "TVMAXVOLTAGE字段为空，可能是UpdateTV执行失败，返回值:2",
                                SN, WOCODE);
                            return 2;
                        }

                        if (!float.TryParse(s_tvmaxvoltage, out _tvmaxvoltage))
                        {
                            WriteErrorLog("[数据异常]CHECK2-字段解析错误-TVMAXVOLTAGE",
                                $"TVMAXVOLTAGE字段解析失败，原始值=[{s_tvmaxvoltage}]，返回值:2",
                                SN, WOCODE);
                            return 2;
                        }
                        bool _tvresult = false;
                        string s_tvresult = dt.Rows[0]["TVRESULT"]?.ToString();
                        
                        if (string.IsNullOrWhiteSpace(s_tvresult))
                        {
                            WriteErrorLog("[数据缺失]CHECK2-字段为空-TVRESULT",
                                "TVRESULT字段为空，可能是UpdateTV执行失败，返回值:2",
                                SN, WOCODE);
                            return 2;
                        }

                        if (!bool.TryParse(s_tvresult, out _tvresult))
                        {
                            WriteErrorLog("[数据异常]CHECK2-字段解析错误-TVRESULT",
                                $"TVRESULT字段解析失败，原始值=[{s_tvresult}]，返回值:2",
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

                        // 6. 解析AOI外观检测
                        bool _takephoto2 = false;
                        string s_takephoto2 = dt.Rows[0]["TAKEPHOTO2"]?.ToString();
                        
                        if (string.IsNullOrWhiteSpace(s_takephoto2))
                        {
                            WriteErrorLog("[数据缺失]CHECK2-字段为空-TAKEPHOTO2",
                                "TAKEPHOTO2字段为空，可能是UpdateTakePhoto2执行失败，返回值:4",
                                SN, WOCODE);
                            return 4;
                        }

                        if (!bool.TryParse(s_takephoto2, out _takephoto2))
                        {
                            WriteErrorLog("[数据异常]CHECK2-字段解析错误-TAKEPHOTO2",
                                $"TAKEPHOTO2字段解析失败，原始值=[{s_takephoto2}]，返回值:4",
                                SN, WOCODE);
                            return 4;
                        }

                        if (!_takephoto2)
                        {
                            return 4;
                        }

                        return 0;
                    }
                }


            }
            catch (Exception ex)
            {
                // 【系统异常】未捕获的异常
                WriteErrorLog("[系统异常]CHECK2-未捕获异常",
                    $"未捕获异常，类型=[{ex.GetType().Name}]，消息=[{ex.Message}]，堆栈=[{ex.StackTrace}]，返回值:1",
                    SN, WOCODE);
            }

            return 1;
        }
    }


    public partial class sqlite
    {
        //string connStr = @"Data Source=" + @"D:\sqlliteDb\document.db;Initial Catalog=sqlite;Integrated Security=True;Max Pool Size=10";        
        public static string connstr = @"Data Source=database\record.db;Pooling=true;FailIfMissing=false";

        #region 读取数据按内部链接来
        /// <summary>
        /// 使用默认本地SQLite连接读取数据
        /// 典型场景：业务层只需执行查询，不关心具体数据库路径
        /// </summary>
        /// <param name="sql">要执行的查询SQL</param>
        /// <returns>查询结果DataTable；异常时返回null</returns>
        public static DataTable Read(string sql)
        {
            return Read(sql, connstr);
        }
        /// <summary>
        /// 使用默认本地SQLite连接读取单个字符串结果
        /// 适用于只需返回首行首列值的轻量查询
        /// </summary>
        /// <param name="sql">要执行的查询SQL</param>
        /// <returns>首行首列字符串；异常时返回null</returns>
        public static string ReadString(String sql)
        {
            return ReadString(sql, connstr);
        }
        /// <summary>
        /// 使用默认本地SQLite连接执行非查询SQL
        /// 典型用途：INSERT/UPDATE/DELETE 操作
        /// </summary>
        /// <param name="sql">要执行的SQL语句</param>
        /// <returns>受影响的行数；失败返回0</returns>
        public static int excute_sql(string sql)
        {
            return excute_sql(sql, connstr);
        }
        #endregion


        #region 读取数据按外部链接来
        /// <summary>
        /// 指定连接字符串读取数据
        /// 业务场景：跨工单/多库查询时由上层传入连接
        /// </summary>
        /// <param name="sql">查询SQL</param>
        /// <param name="connstring">SQLite连接字符串</param>
        /// <returns>查询结果DataTable；异常时返回null</returns>
        public static DataTable Read(string sql, string connstring)
        {
            try
            {
                using (SQLiteConnection conn = new SQLiteConnection(connstring))
                {
                    conn.Open();
                    using (SQLiteDataAdapter oda = new SQLiteDataAdapter(sql, conn))
                    {
                        DataTable dt = new DataTable();
                        oda.Fill(dt);
                        return dt;
                    }
                }
            }
            catch
            {
                return null;
            }
        }
        /// <summary>
        /// 指定连接字符串读取首行首列的字符串结果
        /// 适用于快速读取单值配置或状态位
        /// </summary>
        /// <param name="sql">查询SQL</param>
        /// <param name="connstring">SQLite连接字符串</param>
        /// <returns>首行首列字符串；异常时返回null</returns>
        public static string ReadString(String sql, string connstring)
        {
            try
            {
                using (SQLiteConnection conn = new SQLiteConnection(connstring))
                {
                    conn.Open();
                    using (SQLiteCommand odc = new SQLiteCommand(sql, conn))
                    using (SQLiteDataReader reader = odc.ExecuteReader())
                    {
                        reader.Read();
                        string s = reader.GetValue(0).ToString();
                        return s;
                    }
                }
            }
            catch
            {
                return null;
            }
        }
        /// <summary>
        /// 指定连接字符串执行非查询SQL，并带重试机制
        /// 重试策略：异常时最多重试3次，每次间隔100ms，主要应对SQLite锁冲突
        /// </summary>
        /// <param name="sql">要执行的SQL语句</param>
        /// <param name="connstring">SQLite连接字符串</param>
        /// <returns>受影响的行数；超出重试或异常返回0</returns>
        public static int excute_sql(string sql, string connstring)
        {
            int retryCount = 0;
            int maxRetries = 3;
            
            while (retryCount <= maxRetries)
            {
                try
                {
                    using (SQLiteConnection conn = new SQLiteConnection(connstring))
                    {
                        conn.Open();
                        using (SQLiteCommand odc = new SQLiteCommand(sql, conn))
                        {
                            int x = odc.ExecuteNonQuery();
                            return x;
                        }
                    }
                }
                catch (Exception ex)
                {
                    retryCount++;
                    // 如果是最后一次尝试，或者异常不是数据库锁定（通常锁定也是Exception，但为了保险起见对所有异常重试），记录日志
                    // 实际生产中SQLite Busy/Locked是主要重试目标
                    
                    if (retryCount > maxRetries)
                    {
                        WriteErrorLog("SQL_EXECUTE_ERROR", 
                            $"SQL执行失败，已重试{maxRetries}次。SQL=[{sql}] Error=[{ex.Message}]", 
                            "", "");
                        return 0;
                    }
                    
                    // 等待一段时间后重试
                    Thread.Sleep(100);
                }
            }
            return 0;
        }

        #endregion
    }
}
