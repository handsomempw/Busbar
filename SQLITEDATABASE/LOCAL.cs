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
using System.Globalization; // 使用InvariantCulture格式化浮点数，避免小数点逗号导致SQL解析失败

namespace SQLITEDATABASE
{
    public partial class sqlite
    {
        private static object perfLogLocker = new object(); //性能日志文件锁
        private static object errorLogLocker = new object(); //数据库异常日志文件锁
        private static object dbFileLocker = new object(); //数据库文件操作锁

        /// <summary>
        /// UI层注入的日志回调：用于将SQLite相关提示输出到界面（例如调用主程序的 writeLog）。
        /// 说明：SQLITEDATABASE 项目不应直接依赖 UI 项目，通过委托回调保持分层与依赖方向正确。
        /// </summary>
        public static Action<string> UiLog { get; set; }

        /// <summary>
        /// 电测过程数据快照。
        /// 双Y流程结束归档从本地 SQLite 读取 ACW/DCW 行后上传 MES，避免界面异步刷新影响最终过程数据。
        /// </summary>
        public sealed class ElectricalTestProcessRow
        {
            public long Id { get; set; }
            public string TestMode { get; set; }
            public bool TakePhoto1 { get; set; }
            public float Res { get; set; }
            public float TVMaxVoltage { get; set; }
            public float TVMaxCurrent { get; set; }
            public string TVMeterID { get; set; }
            public string TVInfo { get; set; }
            public bool TVResult { get; set; }
            public UInt32 PressureMax { get; set; }
            public UInt32 PressureAverage { get; set; }
            public UInt32 PressureMin { get; set; }
            public bool PressureResult { get; set; }
            public bool TakePhoto2 { get; set; }
        }

        /// <summary>
        /// SQLite模块专用错误日志
        /// 独立文件存储，便于问题定位和统计分析
        /// 输出路径：
        /// - 异常：日志\数据库异常\{日期}.txt
        /// - 追踪/信息：日志\数据库追踪\{日期}.txt
        /// </summary>
        public static void WriteErrorLog(string tag, string message, string sn = "", string wocode = "")
        {
            try
            {
                DateTime now = DateTime.Now;

                // 规范化 tag：调用方历史上会传入 “[追踪]xxx”/“[数据库异常]xxx”/“UPDATETV_EXCEPTION”等多种风格，统一成单一格式，便于检索与统计
                string normalizedTag = NormalizeDbLogTag(tag, out string category);
                bool isTrace = IsDbTraceCategory(category);

                // 目录分流：把追踪/信息/数据缺失等从“数据库异常”中剥离，避免异常日志被噪声污染
                string subDir = isTrace ? "数据库追踪" : "数据库异常";
                string filename = $"{Environment.CurrentDirectory}\\日志\\{subDir}\\{now.ToString("yyyyMMdd")}.txt";
                string dir = Path.GetDirectoryName(filename);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                StringBuilder logBuilder = new StringBuilder();
                logBuilder.Append($"[{now.ToString("yyyy-MM-dd HH:mm:ss.fff")}]");
                logBuilder.Append($"[{normalizedTag}]");
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
        /// 规范化数据库日志 tag，并提取分类（如“追踪/数据库异常/数据缺失”等）。
        /// 目标：不改变日志整体格式，只把 tag 统一成可检索的稳定形态。
        /// </summary>
        private static string NormalizeDbLogTag(string tag, out string category)
        {
            category = string.Empty;
            if (string.IsNullOrWhiteSpace(tag))
            {
                return "UNSPECIFIED";
            }

            string trimmed = tag.Trim();

            // 提取开头的 “[分类]” 前缀
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                int closeIndex = trimmed.IndexOf(']');
                if (closeIndex > 1)
                {
                    category = trimmed.Substring(1, closeIndex - 1).Trim();
                    string rest = trimmed.Substring(closeIndex + 1).Trim();
                    rest = rest.Replace("[", string.Empty).Replace("]", string.Empty).Trim();

                    if (string.IsNullOrEmpty(rest))
                    {
                        return category;
                    }

                    return $"{category}-{rest}";
                }
            }

            // 没有分类前缀的，清理掉意外的括号后直接返回
            return trimmed.Replace("[", string.Empty).Replace("]", string.Empty);
        }

        /// <summary>
        /// 判断是否属于“数据库追踪”类别（非异常）。
        /// </summary>
        private static bool IsDbTraceCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                return false;
            }

            // 这些类型在现有代码里用于诊断/业务判定/信息记录，不应混入“数据库异常”目录
            switch (category.Trim())
            {
                case "追踪":
                case "调试信息":
                case "数据库信息":
                case "数据缺失":
                case "数据异常":
                case "业务判定":
                case "双测模式":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 记录压力 NG3 追踪日志，落盘路径与阻值 NG3 相同（日志\数据库追踪\{yyyyMMdd}.txt）。
        /// 判定口径与 CHECK 前 UpdatePressure 一致：最大压力不超过上限，最小压力不低于下限。
        /// </summary>
        /// <param name="stageTag">流程阶段标识，如 CHECK1、CHECK2、点检CHECK1。</param>
        /// <param name="pressureMax">最大压力，单位与 PLC/数据库 PRESSURE_MAX 一致。</param>
        /// <param name="pressureMin">最小压力，单位与 PLC/数据库 PRESSURE_MIN 一致。</param>
        /// <param name="pressureAverage">平均压力，单位与 PLC/数据库 PRESSURE_AVERAGE 一致。</param>
        /// <param name="maxLimit">压力上限阈值，来自过程参数 PressureParamter.Max_Pressure。</param>
        /// <param name="minLimit">压力下限阈值，来自过程参数 PressureParamter.Min_Pressure。</param>
        /// <param name="sn">产品 SN，便于按条码检索。</param>
        /// <param name="wocode">批次号，便于按工单检索。</param>
        public static void WritePressureThresholdNg3Trace(string stageTag, float pressureMax, float pressureMin, float pressureAverage, float maxLimit, float minLimit, string sn = "", string wocode = "")
        {
            var reasons = new List<string>();
            if (pressureMax > maxLimit)
            {
                reasons.Add($"Max={FormatSqlNumber(pressureMax)} > maxLimit={FormatSqlNumber(maxLimit)}");
            }
            if (pressureMin < minLimit)
            {
                reasons.Add($"Min={FormatSqlNumber(pressureMin)} < minLimit={FormatSqlNumber(minLimit)}");
            }
            if (reasons.Count == 0)
            {
                reasons.Add($"PRESSURE_RESULT=0，Max={FormatSqlNumber(pressureMax)}, Min={FormatSqlNumber(pressureMin)}, Avg={FormatSqlNumber(pressureAverage)}，阈值=[{FormatSqlNumber(minLimit)},{FormatSqlNumber(maxLimit)}]");
            }

            WriteErrorLog($"[追踪]{stageTag}-压力阈值判定NG3",
                $"{string.Join("; ", reasons)}，直接返回=3(压力不合格)",
                sn, wocode);
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

        /// <summary>
        /// 普通耐压记录的数据库筛选边界。
        /// IR 结果复用 TVMAXVOLTAGE/TVMAXCURRENT 物理列保存绝缘电阻和漏电流；所有 ACW/DCW 压力、CHECK 基础判定
        /// 和耐压更新都必须排除 [IR] 行，避免把绝缘电阻当作耐压最大电压或最大电流处理。
        /// </summary>
        private const string NonIrTvInfoCondition = "(TVInfo IS NULL OR TVInfo NOT LIKE '[IR]%')";

        /// <summary>
        /// 耐压首测占位行筛选边界。
        /// 拍照留底后创建的占位记录通常尚未写入 TVInfo，ACW/DCW 第一次测试只能占用这类空白非 IR 行；
        /// 如果同 SN 已存在另一种耐压模式行，应由双测插入逻辑新增独立行，而不是覆盖已有模式。
        /// </summary>
        private const string BlankNonIrTvInfoCondition = "((TVInfo IS NULL OR trim(TVInfo) = '') AND (TVInfo IS NULL OR TVInfo NOT LIKE '[IR]%'))";

        /// <summary>
        /// 从 TVInfo 前缀识别电测业务类型。
        /// 返回值只用于数据库行定位；不改变 TVInfo 的原始保存内容，也不参与 UI 文案显示。
        /// </summary>
        /// <param name="tvInfo">调用方准备写入本地库的 TVInfo，通常带 [ACW]、[DCW] 或 [IR] 前缀。</param>
        /// <returns>识别到的前缀；无法识别时返回空字符串，保持无前缀耐压记录按非 IR 最新行兼容。</returns>
        private static string GetTvModePrefix(string tvInfo)
        {
            string text = tvInfo ?? string.Empty;
            if (text.StartsWith("[ACW]", StringComparison.OrdinalIgnoreCase)) return "[ACW]";
            if (text.StartsWith("[DCW]", StringComparison.OrdinalIgnoreCase)) return "[DCW]";
            if (text.StartsWith("[IR]", StringComparison.OrdinalIgnoreCase)) return "[IR]";
            return string.Empty;
        }

        /// <summary>
        /// 构造耐压字段更新片段，统一处理小数点格式和字符串单引号。
        /// 该片段只用于本地 SQLite 电测结果写入，避免中文系统小数逗号或仪表文本中的单引号破坏 SQL。
        /// </summary>
        private static string BuildTvUpdateSet(float RES, float MaxVoltage, bool TVResult, float MaxCurrent, string TVInfo, string TVMeterID)
        {
            return $"RES={FormatSqlNumber(RES)},TVMAXVOLTAGE={FormatSqlNumber(MaxVoltage)},TVMAXCURRENT={FormatSqlNumber(MaxCurrent)},TVRESULT={(TVResult ? 1 : 0)},TVMeterID='{EscapeSqlLiteral(TVMeterID)}',TVInfo='{EscapeSqlLiteral(TVInfo)}'";
        }

        /// <summary>
        /// 按 SQLite 可解析的小数点格式输出测试数值。
        /// </summary>
        private static string FormatSqlNumber(float value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 转义写入 SQLite 文本字段的单引号。
        /// </summary>
        private static string EscapeSqlLiteral(string value)
        {
            return (value ?? string.Empty).Replace("'", "''");
        }

        private static float ParseDbFloat(object value)
        {
            float result;
            string raw = value?.ToString();
            if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ||
                float.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out result))
            {
                return result;
            }

            return 0;
        }

        private static UInt32 ParseDbUInt32(object value)
        {
            UInt32 integerValue;
            string raw = value?.ToString();
            if (UInt32.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out integerValue)
                || UInt32.TryParse(raw, NumberStyles.Integer, CultureInfo.CurrentCulture, out integerValue))
            {
                return integerValue;
            }

            decimal number;
            if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                && !decimal.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out number))
            {
                return 0;
            }

            if (number <= 0) return 0;
            if (number >= UInt32.MaxValue) return UInt32.MaxValue;
            return Convert.ToUInt32(number);
        }

        /// <summary>
        /// 查询本地过程表中符合条件的最新记录 ID。
        /// 用于电测写库和 CHECK 诊断链路确认“本轮写入行”和“本轮判定行”是否一致；只返回 ID，不改变任何业务数据。
        /// </summary>
        /// <param name="connstring">当前工单 SQLite 数据库连接串。</param>
        /// <param name="whereClause">已由调用方限定好 SN、测试类型和 IR 隔离边界的查询条件。</param>
        /// <returns>找到记录时返回数据库 ID；未找到或无法解析时返回 -1。</returns>
        private static long GetLatestBusbarRecordId(string connstring, string whereClause)
        {
            DataTable dt = Read($"SELECT ID FROM BusbarCompressionData WHERE {whereClause} ORDER BY ID DESC LIMIT 1", connstring);
            if (dt == null || dt.Rows.Count == 0)
            {
                return -1;
            }

            long rowId;
            return long.TryParse(dt.Rows[0]["ID"]?.ToString(), out rowId) ? rowId : -1;
        }


        /// <summary>
        /// 更新产品的非 IR 电测压力数据。
        /// 压力在当前产线流程中属于普通 ACW/DCW 电测判定；IR 记录虽然复用同一张本地表，
        /// 但不参与 D1600 压力回写，避免最新 IR 行覆盖耐压工位的压力追溯结果。
        /// </summary>
        /// <param name="PressureResult">压力测试是否合格</param>
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

                string sql = $"UPDATE BusbarCompressionData SET PRESSURE_RESULT ={(PressureResult ? 1 : 0)},PRESSURE_MAX={FormatSqlNumber(MaxPressure)},PRESSURE_AVERAGE={FormatSqlNumber(AveragePressure)},PRESSURE_MIN={FormatSqlNumber(MinPressure)} WHERE id=(SELECT max(id) from BusbarCompressionData WHERE sn='{SN}' AND {NonIrTvInfoCondition})";
                int c = excute_sql(sql, _connstr);
                return c > 0;
            }
            catch (Exception ex)
            {
                WriteErrorLog("[数据库异常]UpdatePressure失败", $"异常: {ex.Message}", SN, WOCODE);
            }
            return false;
        }

        /// <summary>
        /// 双Y流程结束压力快照写入。
        /// D1020/D1021 触发后，PLC 提供本工位最终压力快照；该快照同步到同 SN 的 ACW/DCW 电测行，保证两条 MES 过程数据使用同一轮流程结束压力。
        /// </summary>
        /// <param name="WOCODE">当前产品工单号，用于定位本地工单数据库。</param>
        /// <param name="PARTNOID">当前产品规格编码，用于保持数据库接口一致。</param>
        /// <param name="SN">当前工位从 D825/D850 读取到的产品 SN。</param>
        /// <param name="AveragePressure">PLC 压力平均值，单位沿用现场 PLC 标定。</param>
        /// <param name="MaxPressure">PLC 压力最大值，单位沿用现场 PLC 标定。</param>
        /// <param name="MinPressure">PLC 压力最小值，单位沿用现场 PLC 标定。</param>
        /// <param name="PressureResult">按当前参数上下限得到的压力判定。</param>
        /// <returns>至少一条电测行或兼容占位行写入成功时返回 true。</returns>
        public static bool UpdatePressureForElectricalRows(string WOCODE, string PARTNOID, string SN, float AveragePressure, float MaxPressure, float MinPressure, bool PressureResult)
        {
            try
            {
                string _connstr = CheckDataBase(WOCODE, PARTNOID, SN);
                if (string.IsNullOrEmpty(_connstr))
                {
                    WriteErrorLog("[追踪]UpdatePressureForElectricalRows-连接串为空", "CheckDataBase返回空", SN, WOCODE);
                    return false;
                }

                string escapedSn = EscapeSqlLiteral(SN);
                string updateSet = $"PRESSURE_RESULT={(PressureResult ? 1 : 0)},PRESSURE_MAX={FormatSqlNumber(MaxPressure)},PRESSURE_AVERAGE={FormatSqlNumber(AveragePressure)},PRESSURE_MIN={FormatSqlNumber(MinPressure)}";
                string sql = $"UPDATE BusbarCompressionData SET {updateSet} WHERE sn='{escapedSn}' AND (TVInfo LIKE '[ACW]%' OR TVInfo LIKE '[DCW]%')";
                int c = excute_sql(sql, _connstr);
                if (c > 0)
                {
                    WriteErrorLog("[追踪]UpdatePressureForElectricalRows-写入电测行", $"affectedRows={c}, PressureResult={(PressureResult ? 1 : 0)}", SN, WOCODE);
                    return true;
                }

                return UpdatePressure(WOCODE, PARTNOID, SN, AveragePressure, MaxPressure, MinPressure, PressureResult);
            }
            catch (Exception ex)
            {
                WriteErrorLog("[数据库异常]UpdatePressureForElectricalRows失败", $"异常: {ex.Message}", SN, WOCODE);
            }
            return false;
        }

        /// <summary>
        /// 按指定 SQLite 行 ID 写入 32 位压力快照。
        /// 双Y流程结束只应传入各模式最新测试行；同模式更早复测行不在此范围，以保持“压力属于最终一次测试”的追溯口径。
        /// 精确 ID 范围同时保证 Y1/Y2 并行与历史记录互不覆盖。
        /// </summary>
        /// <param name="WOCODE">当前工位产品工单号，用于定位本地工单数据库。</param>
        /// <param name="PARTNOID">当前工位产品规格编码。</param>
        /// <param name="SN">当前工位产品 SN，用于校验目标行归属。</param>
        /// <param name="recordIds">应写入压力的 SQLite 行 ID，通常为各 ACW/DCW 模式最新一条。</param>
        /// <param name="AveragePressure">PLC uint32 压力平均值，单位沿用现场标定。</param>
        /// <param name="MaxPressure">PLC uint32 压力最大值，单位沿用现场标定。</param>
        /// <param name="MinPressure">PLC uint32 压力最小值，单位沿用现场标定。</param>
        /// <param name="PressureResult">按当前工艺上下限计算的压力判定。</param>
        /// <returns>全部目标测试行均写入成功时返回 true。</returns>
        public static bool UpdatePressureForElectricalRowsByIds(string WOCODE, string PARTNOID, string SN, IEnumerable<long> recordIds,
            UInt32 AveragePressure, UInt32 MaxPressure, UInt32 MinPressure, bool PressureResult)
        {
            try
            {
                List<long> ids = (recordIds ?? Enumerable.Empty<long>()).Where(id => id > 0).Distinct().ToList();
                if (ids.Count == 0)
                {
                    WriteErrorLog("[数据缺失]双Y压力写入-测试行为空", "当前工位会话没有可写入的电测行ID", SN, WOCODE);
                    return false;
                }

                string connectionString = CheckDataBase(WOCODE, PARTNOID, SN);
                if (string.IsNullOrEmpty(connectionString))
                {
                    WriteErrorLog("[追踪]双Y压力写入-连接串为空", "CheckDataBase返回空", SN, WOCODE);
                    return false;
                }

                string idList = string.Join(",", ids.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray());
                string updateSet = $"PRESSURE_RESULT={(PressureResult ? 1 : 0)},PRESSURE_MAX={MaxPressure.ToString(CultureInfo.InvariantCulture)},PRESSURE_AVERAGE={AveragePressure.ToString(CultureInfo.InvariantCulture)},PRESSURE_MIN={MinPressure.ToString(CultureInfo.InvariantCulture)}";
                string sql = $"UPDATE BusbarCompressionData SET {updateSet} WHERE sn='{EscapeSqlLiteral(SN)}' AND ID IN ({idList}) AND (TVInfo LIKE '[ACW]%' OR TVInfo LIKE '[DCW]%')";
                int affectedRows = excute_sql(sql, connectionString);
                bool allUpdated = affectedRows == ids.Count;
                WriteErrorLog("[追踪]双Y压力写入完成",
                    $"expectedRows={ids.Count}, affectedRows={affectedRows}, IDs={idList}, PressureResult={(PressureResult ? 1 : 0)}",
                    SN, WOCODE);
                return allUpdated;
            }
            catch (Exception ex)
            {
                WriteErrorLog("[数据库异常]双Y压力写入失败", $"异常: {ex.Message}", SN, WOCODE);
                return false;
            }
        }

        /// <summary>
        /// 更新产品的 ACW/DCW 耐压测试数据。
        /// 本地表历史上使用 TVMAXVOLTAGE/TVMAXCURRENT 保存耐压值，IR 后续也复用这些物理列保存绝缘电阻和漏电流；
        /// 因此耐压更新必须按 TVInfo 前缀或空白占位行定位，不能再简单写入同 SN 最新记录。
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

                if (string.IsNullOrEmpty(_connstr))
                {
                    WriteErrorLog("[追踪]UpdateTV-连接串为空", "CheckDataBase返回空", SN, WOCODE);
                    return false;
                }

                string updateSet = BuildTvUpdateSet(RES, MaxVoltage, TVResult, MaxCurrent, TVInfo, TVMeterID);
                string tvModePrefix = GetTvModePrefix(TVInfo);

                if (tvModePrefix == "[ACW]" || tvModePrefix == "[DCW]")
                {
                    string escapedSn = EscapeSqlLiteral(SN);
                    long placeholderId = GetLatestBusbarRecordId(_connstr, $"sn='{escapedSn}' AND {BlankNonIrTvInfoCondition}");
                    if (placeholderId > 0)
                    {
                        string updatePlaceholder = $"UPDATE BusbarCompressionData SET {updateSet} WHERE ID={placeholderId}";
                        int placeholderCount = excute_sql(updatePlaceholder, _connstr);
                        if (placeholderCount > 0)
                        {
                            WriteErrorLog("[追踪]UpdateTV-写入最新占位行",
                                $"hitId={placeholderId}, mode={tvModePrefix}, affectedRows={placeholderCount}, RES={FormatSqlNumber(RES)}",
                                SN, WOCODE);
                            return true;
                        }
                    }

                    long sameModeId = GetLatestBusbarRecordId(_connstr, $"sn='{escapedSn}' AND TVInfo LIKE '{tvModePrefix}%'");
                    if (sameModeId <= 0)
                    {
                        WriteErrorLog("[追踪]UpdateTV-未找到耐压可更新记录",
                            $"mode={tvModePrefix}, 未找到最新空白非IR行，也未找到同模式兼容行",
                            SN, WOCODE);
                        return false;
                    }

                    string updateSameMode = $"UPDATE BusbarCompressionData SET {updateSet} WHERE ID={sameModeId}";
                    int sameModeCount = excute_sql(updateSameMode, _connstr);
                    if (sameModeCount > 0)
                    {
                        WriteErrorLog("[追踪]UpdateTV-回退写入同模式行",
                            $"hitId={sameModeId}, mode={tvModePrefix}, affectedRows={sameModeCount}, RES={FormatSqlNumber(RES)}",
                            SN, WOCODE);
                        return true;
                    }

                    WriteErrorLog("[追踪]UpdateTV-同模式行写入未命中",
                        $"hitId={sameModeId}, mode={tvModePrefix}, affectedRows={sameModeCount}",
                        SN, WOCODE);
                    return false;
                }

                string sql = $"UPDATE BusbarCompressionData SET {updateSet} WHERE id=(SELECT max(id) from BusbarCompressionData WHERE sn='{SN}' AND {NonIrTvInfoCondition})";
                int c = excute_sql(sql, _connstr);
                return c > 0;
            }
            catch (Exception ex)
            {
                WriteErrorLog("[数据库异常]UpdateTV失败", $"异常: {ex.Message}", SN, WOCODE);
            }
            return false;
        }

        /// <summary>
        /// 仅更新阻值(RES)字段
        /// 业务背景：现场存在“阻值NG导致PLC跳过耐压测试”的流程分支，此时不会调用UpdateTV，
        /// 若不单独落库阻值，数据库将只保留拍照留底等前置数据，导致追溯缺失。
        /// </summary>
        /// <remarks>
        /// 设计取舍：遵循KISS/YAGNI，只补齐缺失的RES写库，不伪造TVMAXVOLTAGE/TVRESULT等字段。
        /// </remarks>
        public static bool UpdateResOnly(string WOCODE, string PARTNOID, string SN, float RES)
        {
            try
            {
                string _connstr = CheckDataBase(WOCODE, PARTNOID, SN);
                if (string.IsNullOrEmpty(_connstr))
                {
                    WriteErrorLog("[追踪]UpdateResOnly-连接串为空", "CheckDataBase返回空", SN, WOCODE);
                    return false;
                }

                // 使用InvariantCulture，避免中文系统下浮点数格式化为“1,23”导致SQLite SQL解析失败
                string resValue = RES.ToString(CultureInfo.InvariantCulture);
                string sql = $"UPDATE BusbarCompressionData SET RES={resValue} WHERE id=(SELECT max(id) from BusbarCompressionData WHERE sn='{SN}')";
                int c = excute_sql(sql, _connstr);
                return c > 0;
            }
            catch (Exception ex)
            {
                WriteErrorLog("[数据库异常]UpdateResOnly失败", $"异常: {ex.Message}", SN, WOCODE);
            }
            return false;
        }

        /// <summary>
        /// 按 SN 从数据库中获取最近一条记录的 RES（接触电阻/阻值）。
        /// 兜底场景：PLC 读取阻值失败/未写入时，IR 测试仍希望复用同一产品 ACW/DCW 等记录中的 RES。
        /// </summary>
        public static bool TryGetLatestRes(string WOCODE, string PARTNOID, string SN, out float res)
        {
            res = -1;
            try
            {
                string _connstr = CheckDataBase(WOCODE, PARTNOID, SN);
                if (string.IsNullOrEmpty(_connstr))
                {
                    WriteErrorLog("[追踪]TryGetLatestRes-连接串为空", "CheckDataBase返回空", SN, WOCODE);
                    return false;
                }

                string sql = $"SELECT RES FROM BusbarCompressionData WHERE sn='{SN}' ORDER BY id DESC LIMIT 1";
                DataTable dt = Read(sql, _connstr);
                if (dt == null || dt.Rows.Count == 0)
                {
                    WriteErrorLog("[追踪]TryGetLatestRes-无记录", "未查到任何记录用于兜底RES", SN, WOCODE);
                    return false;
                }

                string raw = dt.Rows[0]["RES"]?.ToString();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    WriteErrorLog("[追踪]TryGetLatestRes-RES为空", "最近记录RES字段为空", SN, WOCODE);
                    return false;
                }

                // 兼容本地数据库中可能出现的不同小数点格式
                if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out res) &&
                    !float.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out res))
                {
                    WriteErrorLog("[追踪]TryGetLatestRes-RES解析失败", $"原始值=[{raw}]", SN, WOCODE);
                    return false;
                }

                return res > 0;
            }
            catch (Exception ex)
            {
                WriteErrorLog("[数据库异常]TryGetLatestRes失败", $"异常: {ex.Message}", SN, WOCODE);
            }

            return false;
        }

        /// <summary>
        /// 为双测模式的第二次测试插入新记录
        /// 业务逻辑：当产品需要进行两次电测（先ACW后DCW或先DCW后ACW）时，
        /// 第二次测试需要插入新记录而不是更新现有记录，以保留两次测试的完整数据
        /// </summary>
        /// <param name="WOCODE">工单号</param>
        /// <param name="PARTNOID">产品料号</param>
        /// <param name="SN">产品序列号</param>
        /// <param name="STATIONCODE">工站代码</param>
        /// <param name="EQUIPMENTID">设备ID</param>
        /// <param name="RES">电阻值</param>
        /// <param name="MaxVoltage">最大电压</param>
        /// <param name="TVResult">测试结果</param>
        /// <param name="MaxCurrent">最大电流</param>
        /// <param name="TVInfo">测试状态信息（应带[DCW]前缀）</param>
        /// <param name="TVMeterID">测试仪器编号</param>
        /// <returns>插入成功返回true</returns>
        public static bool InsertTV_SecondTest(string WOCODE, string PARTNOID, string SN, string STATIONCODE, string EQUIPMENTID,
            float RES, float MaxVoltage, bool TVResult, float MaxCurrent, string TVInfo, string TVMeterID)
        {
            return InsertElectricalTestRecord(WOCODE, PARTNOID, SN, STATIONCODE, EQUIPMENTID,
                RES, MaxVoltage, TVResult, MaxCurrent, TVInfo, TVMeterID, "InsertTV_SecondTest");
        }

        /// <summary>
        /// 为双Y一次实际耐压测试新增独立过程行，并返回 SQLite 主键。
        /// 每次 ACW/DCW 触发均调用本入口，连续复测继续追加记录；
        /// 压力字段由流程结束按“各模式最新一条”补写，同模式更早复测行不写压力。
        /// </summary>
        /// <param name="WOCODE">本次测试产品的工单号，用于定位工单数据库。</param>
        /// <param name="PARTNOID">本次测试产品的规格编码。</param>
        /// <param name="SN">从对应工位 PLC 产品码区读取的产品 SN。</param>
        /// <param name="STATIONCODE">MES 工站代码，沿用设备基础配置。</param>
        /// <param name="EQUIPMENTID">设备编号，沿用设备基础配置。</param>
        /// <param name="RES">本次测试前读取的接触电阻值，单位沿用 PLC 标定。</param>
        /// <param name="MaxVoltage">本次耐压过程最大电压，单位 V。</param>
        /// <param name="TVResult">本次耐压测试判定。</param>
        /// <param name="MaxCurrent">本次耐压过程最大电流，单位 mA。</param>
        /// <param name="TVInfo">带 ACW/DCW 前缀的仪器状态文本。</param>
        /// <param name="TVMeterID">执行本次测试的耐压仪编号。</param>
        /// <param name="testedAt">仪器完成本次测试的时间，用于界面与数据库统一排序。</param>
        /// <returns>插入成功时返回大于 0 的 SQLite 行 ID；失败时返回 0 并写入数据库异常日志。</returns>
        public static long InsertDualYTestAttempt(string WOCODE, string PARTNOID, string SN, string STATIONCODE, string EQUIPMENTID,
            float RES, float MaxVoltage, bool TVResult, float MaxCurrent, string TVInfo, string TVMeterID, DateTime testedAt)
        {
            string connectionString = CheckDataBase(WOCODE, PARTNOID, SN);
            if (string.IsNullOrEmpty(connectionString))
            {
                WriteErrorLog("[追踪]InsertDualYTestAttempt-连接串为空", "CheckDataBase返回空", SN, WOCODE);
                return 0;
            }

            const string insertSql = "INSERT INTO BusbarCompressionData(PARTNOID,WOCODE,SN,EQUIPMENTID,STATIONCODE,DATETIME,TAKEPHOTO1,RES,TVMAXVOLTAGE,TVMAXCURRENT,TVRESULT,TVMeterID,TVInfo) " +
                "VALUES(@partNoId,@woCode,@sn,@equipmentId,@stationCode,@testedAt,0,@res,@maxVoltage,@maxCurrent,@tvResult,@tvMeterId,@tvInfo)";

            for (int retry = 0; retry <= 3; retry++)
            {
                try
                {
                    using (var connection = new SQLiteConnection(connectionString))
                    {
                        connection.Open();
                        using (var command = new SQLiteCommand(insertSql, connection))
                        {
                            command.Parameters.AddWithValue("@partNoId", PARTNOID ?? string.Empty);
                            command.Parameters.AddWithValue("@woCode", WOCODE ?? string.Empty);
                            command.Parameters.AddWithValue("@sn", SN ?? string.Empty);
                            command.Parameters.AddWithValue("@equipmentId", EQUIPMENTID ?? string.Empty);
                            command.Parameters.AddWithValue("@stationCode", STATIONCODE ?? string.Empty);
                            command.Parameters.AddWithValue("@testedAt", testedAt);
                            command.Parameters.AddWithValue("@res", RES);
                            command.Parameters.AddWithValue("@maxVoltage", MaxVoltage);
                            command.Parameters.AddWithValue("@maxCurrent", MaxCurrent);
                            command.Parameters.AddWithValue("@tvResult", TVResult ? 1 : 0);
                            command.Parameters.AddWithValue("@tvMeterId", TVMeterID ?? string.Empty);
                            command.Parameters.AddWithValue("@tvInfo", TVInfo ?? string.Empty);

                            if (command.ExecuteNonQuery() <= 0)
                            {
                                return 0;
                            }
                        }

                        using (var idCommand = new SQLiteCommand("SELECT last_insert_rowid()", connection))
                        {
                            long recordId = Convert.ToInt64(idCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
                            WriteErrorLog("[电测记录]双Y测试行新增成功", $"ID={recordId}, TVInfo={TVInfo}", SN, WOCODE);
                            return recordId;
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (retry >= 3)
                    {
                        WriteErrorLog("[数据库异常]InsertDualYTestAttempt失败", $"已重试3次，异常: {ex.Message}", SN, WOCODE);
                        return 0;
                    }

                    Thread.Sleep(100);
                }
            }

            return 0;
        }

        /// <summary>
        /// 新增一条独立电测记录（ACW/DCW第二次测试、IR测试共用）。
        ///
        /// 业务策略：
        /// - 本地表沿用既有 TV* 物理列，避免扩表影响历史数据与下游上传；
        /// - 通过 TVInfo 前缀、TVMeterID 和 UI 的 TestMode 区分 ACW/DCW/IR 语义；
        /// - 插入独立电测行时优先复制同 SN 最近一条非 IR 记录的拍照/压力结果，保证 IR 复用物理列时不反向污染普通耐压上下文。
        /// </summary>
        private static bool InsertElectricalTestRecord(string WOCODE, string PARTNOID, string SN, string STATIONCODE, string EQUIPMENTID,
            float RES, float MaxVoltage, bool TVResult, float MaxCurrent, string TVInfo, string TVMeterID, string source)
        {
            try
            {
                string _connstr = CheckDataBase(WOCODE, PARTNOID, SN);

                if (string.IsNullOrEmpty(_connstr))
                {
                    WriteErrorLog($"[追踪]{source}-连接串为空", "CheckDataBase返回空", SN, WOCODE);
                    return false;
                }

                // 基础拍照/压力结果属于普通耐压流程，新增 IR 行时也只从非 IR 行复制，避免最新 IR 行回流到 ACW/DCW 语义。
                string sqlSelect = $"SELECT TAKEPHOTO1, PRESSURE_MAX, PRESSURE_AVERAGE, PRESSURE_MIN, PRESSURE_RESULT FROM BusbarCompressionData WHERE id=(SELECT max(id) from BusbarCompressionData WHERE sn='{SN}' AND {NonIrTvInfoCondition})";
                DataTable dt = Read(sqlSelect, _connstr);

                bool takePhoto1 = false;
                float pressureMax = 0, pressureAvg = 0, pressureMin = 0;
                bool pressureResult = false;

                if (dt != null && dt.Rows.Count > 0)
                {
                    takePhoto1 = TryParseDbBool(dt.Rows[0]["TAKEPHOTO1"]);
                    float.TryParse(dt.Rows[0]["PRESSURE_MAX"]?.ToString(), out pressureMax);
                    float.TryParse(dt.Rows[0]["PRESSURE_AVERAGE"]?.ToString(), out pressureAvg);
                    float.TryParse(dt.Rows[0]["PRESSURE_MIN"]?.ToString(), out pressureMin);
                    pressureResult = TryParseDbBool(dt.Rows[0]["PRESSURE_RESULT"]);
                }

                // 插入新记录
                string sqlInsert = $"INSERT INTO BusbarCompressionData(PARTNOID,WOCODE,SN,EQUIPMENTID,STATIONCODE,DATETIME,TAKEPHOTO1,RES,TVMAXVOLTAGE,TVMAXCURRENT,TVRESULT,TVMeterID,TVInfo,PRESSURE_MAX,PRESSURE_AVERAGE,PRESSURE_MIN,PRESSURE_RESULT) " +
                    $"VALUES('{PARTNOID}','{WOCODE}','{SN}','{EQUIPMENTID}','{STATIONCODE}','{DateTime.Now}',{(takePhoto1 ? 1 : 0)},{RES},{MaxVoltage},{MaxCurrent},{(TVResult ? 1 : 0)},'{TVMeterID}','{TVInfo}',{pressureMax},{pressureAvg},{pressureMin},{(pressureResult ? 1 : 0)})";

                int c = excute_sql(sqlInsert, _connstr);
                if (c > 0)
                {
                    WriteErrorLog("[电测记录]新增独立测试行成功", $"Source={source}, TVInfo={TVInfo}", SN, WOCODE);
                }
                return c > 0;
            }
            catch (Exception ex)
            {
                WriteErrorLog($"[数据库异常]{source}失败", $"异常: {ex.Message}", SN, WOCODE);
            }
            return false;
        }

        /// <summary>
        /// 插入 IR 绝缘电阻测试结果行（复用独立电测记录插入逻辑）
        /// 列映射：
        /// - TVMAXVOLTAGE ← 绝缘电阻（Ohm）
        /// - TVMAXCURRENT ← 漏电流（A）
        /// - TVRESULT ← 合格/不合格
        /// - TVInfo ← 以 "[IR]" 前缀区分（如 "[IR] GD" / "[IR] NG"）
        /// </summary>
        public static bool InsertIR_Test(string WOCODE, string PARTNOID, string SN, string STATIONCODE, string EQUIPMENTID,
            float RES, float MaxVoltage, bool TVResult, float MaxCurrent, string TVInfo, string TVMeterID)
        {
            return InsertElectricalTestRecord(WOCODE, PARTNOID, SN, STATIONCODE, EQUIPMENTID,
                RES, MaxVoltage, TVResult, MaxCurrent, TVInfo, TVMeterID, "InsertIR_Test");
        }

        /// <summary>
        /// 读取双Y归档需要上传到 MES 的 ACW/DCW 电测行。
        /// 同一 SN 因重测产生多条同模式记录时，取最新一条；ACW 与 DCW 分别保留，供流程结束阶段各上传一条过程数据。
        /// </summary>
        /// <param name="WOCODE">当前产品工单号，用于定位本地工单数据库。</param>
        /// <param name="PARTNOID">当前产品规格编码，用于保持数据库接口一致。</param>
        /// <param name="SN">当前产品序列号。</param>
        /// <returns>按数据库 ID 升序排列的 ACW/DCW 过程数据快照。</returns>
        public static List<ElectricalTestProcessRow> GetElectricalTestProcessRows(string WOCODE, string PARTNOID, string SN)
        {
            var result = new List<ElectricalTestProcessRow>();
            try
            {
                string _connstr = CheckDataBase(WOCODE, PARTNOID, SN);
                if (string.IsNullOrEmpty(_connstr))
                {
                    WriteErrorLog("[追踪]GetElectricalTestProcessRows-连接串为空", "CheckDataBase返回空", SN, WOCODE);
                    return result;
                }

                string escapedSn = EscapeSqlLiteral(SN);
                string sql = $"SELECT ID, TAKEPHOTO1, RES, TVMAXVOLTAGE, TVMAXCURRENT, TVMeterID, TVInfo, TVRESULT, PRESSURE_MAX, PRESSURE_AVERAGE, PRESSURE_MIN, PRESSURE_RESULT, TAKEPHOTO2 FROM BusbarCompressionData WHERE sn='{escapedSn}' AND (TVInfo LIKE '[ACW]%' OR TVInfo LIKE '[DCW]%') ORDER BY ID DESC";
                DataTable dt = Read(sql, _connstr);
                if (dt == null || dt.Rows.Count == 0)
                {
                    WriteErrorLog("[追踪]GetElectricalTestProcessRows-无电测行", "未查询到ACW/DCW过程数据", SN, WOCODE);
                    return result;
                }

                var latestByMode = new Dictionary<string, ElectricalTestProcessRow>(StringComparer.OrdinalIgnoreCase);
                foreach (DataRow row in dt.Rows)
                {
                    string tvInfo = row["TVInfo"]?.ToString() ?? string.Empty;
                    string mode = tvInfo.StartsWith("[DCW]", StringComparison.OrdinalIgnoreCase) ? "DCW" : "ACW";
                    if (latestByMode.ContainsKey(mode))
                    {
                        continue;
                    }

                    long id;
                    long.TryParse(row["ID"]?.ToString(), out id);
                    latestByMode[mode] = new ElectricalTestProcessRow
                    {
                        Id = id,
                        TestMode = mode,
                        TakePhoto1 = TryParseDbBool(row["TAKEPHOTO1"]),
                        Res = ParseDbFloat(row["RES"]),
                        TVMaxVoltage = ParseDbFloat(row["TVMAXVOLTAGE"]),
                        TVMaxCurrent = ParseDbFloat(row["TVMAXCURRENT"]),
                        TVMeterID = row["TVMeterID"]?.ToString() ?? string.Empty,
                        TVInfo = tvInfo,
                        TVResult = TryParseDbBool(row["TVRESULT"]),
                        PressureMax = ParseDbUInt32(row["PRESSURE_MAX"]),
                        PressureAverage = ParseDbUInt32(row["PRESSURE_AVERAGE"]),
                        PressureMin = ParseDbUInt32(row["PRESSURE_MIN"]),
                        PressureResult = TryParseDbBool(row["PRESSURE_RESULT"]),
                        TakePhoto2 = TryParseDbBool(row["TAKEPHOTO2"])
                    };
                }

                result = latestByMode.Values.OrderBy(r => r.Id).ToList();
                WriteErrorLog("[追踪]GetElectricalTestProcessRows-读取完成",
                    $"rows={result.Count}, modes={string.Join(",", result.Select(r => r.TestMode).ToArray())}",
                    SN, WOCODE);
            }
            catch (Exception ex)
            {
                WriteErrorLog("[数据库异常]GetElectricalTestProcessRows失败", $"异常: {ex.Message}", SN, WOCODE);
            }

            return result;
        }

        /// <summary>
        /// 按双Y当前工位会话记录的 SQLite 行 ID 读取全部 ACW/DCW 测试尝试。
        /// 返回集合保留同模式复测行，供界面追溯、最终结果选取和 MES 逐次过程数据上传使用。
        /// </summary>
        /// <param name="WOCODE">当前工位产品工单号，用于定位工单数据库。</param>
        /// <param name="PARTNOID">当前工位产品规格编码。</param>
        /// <param name="SN">当前工位产品 SN，用于校验行归属。</param>
        /// <param name="recordIds">本次扫码会话内实际测试行的 SQLite 主键集合。</param>
        /// <returns>按 SQLite ID 升序排列的全部测试尝试；目标为空或读取失败时返回空集合。</returns>
        public static List<ElectricalTestProcessRow> GetElectricalTestProcessRowsByIds(string WOCODE, string PARTNOID, string SN, IEnumerable<long> recordIds)
        {
            var result = new List<ElectricalTestProcessRow>();
            try
            {
                List<long> ids = (recordIds ?? Enumerable.Empty<long>()).Where(id => id > 0).Distinct().ToList();
                if (ids.Count == 0)
                {
                    WriteErrorLog("[数据缺失]双Y测试行读取-ID为空", "当前工位会话没有测试行ID", SN, WOCODE);
                    return result;
                }

                string connectionString = CheckDataBase(WOCODE, PARTNOID, SN);
                if (string.IsNullOrEmpty(connectionString))
                {
                    WriteErrorLog("[追踪]双Y测试行读取-连接串为空", "CheckDataBase返回空", SN, WOCODE);
                    return result;
                }

                string idList = string.Join(",", ids.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray());
                string sql = $"SELECT ID, TAKEPHOTO1, RES, TVMAXVOLTAGE, TVMAXCURRENT, TVMeterID, TVInfo, TVRESULT, PRESSURE_MAX, PRESSURE_AVERAGE, PRESSURE_MIN, PRESSURE_RESULT, TAKEPHOTO2 FROM BusbarCompressionData WHERE sn='{EscapeSqlLiteral(SN)}' AND ID IN ({idList}) AND (TVInfo LIKE '[ACW]%' OR TVInfo LIKE '[DCW]%') ORDER BY ID ASC";
                DataTable table = Read(sql, connectionString);
                if (table == null || table.Rows.Count == 0)
                {
                    WriteErrorLog("[数据缺失]双Y测试行读取-无匹配行", $"IDs={idList}", SN, WOCODE);
                    return result;
                }

                foreach (DataRow row in table.Rows)
                {
                    string tvInfo = row["TVInfo"]?.ToString() ?? string.Empty;
                    long id;
                    long.TryParse(row["ID"]?.ToString(), out id);
                    result.Add(new ElectricalTestProcessRow
                    {
                        Id = id,
                        TestMode = tvInfo.StartsWith("[DCW]", StringComparison.OrdinalIgnoreCase) ? "DCW" : "ACW",
                        TakePhoto1 = TryParseDbBool(row["TAKEPHOTO1"]),
                        Res = ParseDbFloat(row["RES"]),
                        TVMaxVoltage = ParseDbFloat(row["TVMAXVOLTAGE"]),
                        TVMaxCurrent = ParseDbFloat(row["TVMAXCURRENT"]),
                        TVMeterID = row["TVMeterID"]?.ToString() ?? string.Empty,
                        TVInfo = tvInfo,
                        TVResult = TryParseDbBool(row["TVRESULT"]),
                        PressureMax = ParseDbUInt32(row["PRESSURE_MAX"]),
                        PressureAverage = ParseDbUInt32(row["PRESSURE_AVERAGE"]),
                        PressureMin = ParseDbUInt32(row["PRESSURE_MIN"]),
                        PressureResult = TryParseDbBool(row["PRESSURE_RESULT"]),
                        TakePhoto2 = TryParseDbBool(row["TAKEPHOTO2"])
                    });
                }

                WriteErrorLog("[追踪]双Y测试行读取完成", $"expectedRows={ids.Count}, actualRows={result.Count}, IDs={idList}", SN, WOCODE);
            }
            catch (Exception ex)
            {
                WriteErrorLog("[数据库异常]双Y测试行读取失败", $"异常: {ex.Message}", SN, WOCODE);
            }

            return result;
        }

        /// <summary>
        /// 读取标准产线 CHECK 阶段需要上传到 MES 的电测过程行。
        /// ACW、DCW 和 IR 在 SQLite 中以 TVInfo 前缀区分；CHECK1 依据该快照上传每一种已完成的电测模式，
        /// CHECK2 可从同一快照选择一条最终/AOI 汇总行，避免 UI 列表刷新顺序影响 MES 过程数据。
        /// </summary>
        /// <param name="WOCODE">当前产品工单号，用于定位本地工单数据库。</param>
        /// <param name="PARTNOID">当前产品规格编码，用于保持数据库接口一致。</param>
        /// <param name="SN">当前产品序列号，用于读取同一产品最新的各模式电测行。</param>
        /// <returns>按数据库 ID 升序排列的 ACW/DCW/IR 最新过程数据快照；同一模式多次测试时只保留最新一条。</returns>
        public static List<ElectricalTestProcessRow> GetStandardElectricalTestProcessRows(string WOCODE, string PARTNOID, string SN)
        {
            var result = new List<ElectricalTestProcessRow>();
            try
            {
                string _connstr = CheckDataBase(WOCODE, PARTNOID, SN);
                if (string.IsNullOrEmpty(_connstr))
                {
                    WriteErrorLog("[追踪]GetStandardElectricalTestProcessRows-连接串为空", "CheckDataBase返回空", SN, WOCODE);
                    return result;
                }

                string escapedSn = EscapeSqlLiteral(SN);
                string sql = $"SELECT ID, TAKEPHOTO1, RES, TVMAXVOLTAGE, TVMAXCURRENT, TVMeterID, TVInfo, TVRESULT, PRESSURE_MAX, PRESSURE_AVERAGE, PRESSURE_MIN, PRESSURE_RESULT, TAKEPHOTO2 FROM BusbarCompressionData WHERE sn='{escapedSn}' AND (TVInfo LIKE '[ACW]%' OR TVInfo LIKE '[DCW]%' OR TVInfo LIKE '[IR]%') ORDER BY ID DESC";
                DataTable dt = Read(sql, _connstr);
                if (dt == null || dt.Rows.Count == 0)
                {
                    WriteErrorLog("[追踪]GetStandardElectricalTestProcessRows-无电测行", "未查询到ACW/DCW/IR过程数据", SN, WOCODE);
                    return result;
                }

                var latestByMode = new Dictionary<string, ElectricalTestProcessRow>(StringComparer.OrdinalIgnoreCase);
                foreach (DataRow row in dt.Rows)
                {
                    string tvInfo = row["TVInfo"]?.ToString() ?? string.Empty;
                    string mode;
                    if (tvInfo.StartsWith("[IR]", StringComparison.OrdinalIgnoreCase))
                    {
                        mode = "IR";
                    }
                    else if (tvInfo.StartsWith("[DCW]", StringComparison.OrdinalIgnoreCase))
                    {
                        mode = "DCW";
                    }
                    else
                    {
                        mode = "ACW";
                    }

                    if (latestByMode.ContainsKey(mode))
                    {
                        continue;
                    }

                    long id;
                    long.TryParse(row["ID"]?.ToString(), out id);
                    latestByMode[mode] = new ElectricalTestProcessRow
                    {
                        Id = id,
                        TestMode = mode,
                        TakePhoto1 = TryParseDbBool(row["TAKEPHOTO1"]),
                        Res = ParseDbFloat(row["RES"]),
                        TVMaxVoltage = ParseDbFloat(row["TVMAXVOLTAGE"]),
                        TVMaxCurrent = ParseDbFloat(row["TVMAXCURRENT"]),
                        TVMeterID = row["TVMeterID"]?.ToString() ?? string.Empty,
                        TVInfo = tvInfo,
                        TVResult = TryParseDbBool(row["TVRESULT"]),
                        PressureMax = ParseDbUInt32(row["PRESSURE_MAX"]),
                        PressureAverage = ParseDbUInt32(row["PRESSURE_AVERAGE"]),
                        PressureMin = ParseDbUInt32(row["PRESSURE_MIN"]),
                        PressureResult = TryParseDbBool(row["PRESSURE_RESULT"]),
                        TakePhoto2 = TryParseDbBool(row["TAKEPHOTO2"])
                    };
                }

                result = latestByMode.Values.OrderBy(r => r.Id).ToList();
                WriteErrorLog("[追踪]GetStandardElectricalTestProcessRows-读取完成",
                    $"rows={result.Count}, modes={string.Join(",", result.Select(r => r.TestMode).ToArray())}",
                    SN, WOCODE);
            }
            catch (Exception ex)
            {
                WriteErrorLog("[数据库异常]GetStandardElectricalTestProcessRows失败", $"异常: {ex.Message}", SN, WOCODE);
            }

            return result;
        }

        /// <summary>
        /// 检查产品是否为双测模式（同一SN有两条记录，分别带[ACW]和[DCW]前缀）
        /// </summary>
        /// <param name="WOCODE">工单号</param>
        /// <param name="PARTNOID">产品料号</param>
        /// <param name="SN">产品序列号</param>
        /// <returns>是否为双测模式</returns>
        public static bool IsDualTestMode(string WOCODE, string PARTNOID, string SN)
        {
            try
            {
                string _connstr = CheckDataBase(WOCODE, PARTNOID, SN);
                if (string.IsNullOrEmpty(_connstr)) return false;

                // 查询最近两条记录的TVInfo
                string sql = $"SELECT TVInfo FROM BusbarCompressionData WHERE sn='{SN}' ORDER BY id DESC LIMIT 2";
                DataTable dt = Read(sql, _connstr);

                if (dt != null && dt.Rows.Count >= 2)
                {
                    string tvInfo1 = dt.Rows[0]["TVInfo"]?.ToString() ?? "";
                    string tvInfo2 = dt.Rows[1]["TVInfo"]?.ToString() ?? "";

                    // 检查是否一条是ACW一条是DCW
                    bool hasACW = tvInfo1.StartsWith("[ACW]") || tvInfo2.StartsWith("[ACW]");
                    bool hasDCW = tvInfo1.StartsWith("[DCW]") || tvInfo2.StartsWith("[DCW]");

                    return hasACW && hasDCW;
                }
            }
            catch (Exception ex)
            {
                WriteErrorLog("[数据库异常]IsDualTestMode检查失败", $"异常: {ex.Message}", SN, WOCODE);
            }
            return false;
        }

        /// <summary>
        /// 获取双测模式下的综合测试结果
        /// 只有ACW和DCW都合格时才返回合格
        /// </summary>
        /// <param name="WOCODE">工单号</param>
        /// <param name="PARTNOID">产品料号</param>
        /// <param name="SN">产品序列号</param>
        /// <param name="acwResult">输出：ACW测试结果</param>
        /// <param name="dcwResult">输出：DCW测试结果</param>
        /// <returns>综合结果：0=全部合格，2=耐压不合格</returns>
        public static int GetDualTestResult(string WOCODE, string PARTNOID, string SN, out bool acwResult, out bool dcwResult)
        {
            acwResult = false;
            dcwResult = false;

            try
            {
                string _connstr = CheckDataBase(WOCODE, PARTNOID, SN);
                if (string.IsNullOrEmpty(_connstr)) return 2;

                // 查询最近两条记录
                string sql = $"SELECT TVInfo, TVRESULT, TVMAXVOLTAGE FROM BusbarCompressionData WHERE sn='{SN}' ORDER BY id DESC LIMIT 2";
                DataTable dt = Read(sql, _connstr);

                if (dt != null && dt.Rows.Count >= 2)
                {
                    for (int i = 0; i < dt.Rows.Count; i++)
                    {
                        string tvInfo = dt.Rows[i]["TVInfo"]?.ToString() ?? "";
                        bool tvResult = TryParseDbBool(dt.Rows[i]["TVRESULT"]);
                        float tvMaxVoltage = 0;
                        float.TryParse(dt.Rows[i]["TVMAXVOLTAGE"]?.ToString(), out tvMaxVoltage);

                        bool isPass = tvResult && tvMaxVoltage > 0 && tvMaxVoltage != -1;

                        if (tvInfo.StartsWith("[ACW]"))
                        {
                            acwResult = isPass;
                        }
                        else if (tvInfo.StartsWith("[DCW]"))
                        {
                            dcwResult = isPass;
                        }
                    }
                }

                // 双测模式下，两个都合格才算合格
                return (acwResult && dcwResult) ? 0 : 2;
            }
            catch (Exception ex)
            {
                WriteErrorLog("[数据库异常]GetDualTestResult失败", $"异常: {ex.Message}", SN, WOCODE);
                return 2;
            }
        }

        /// <summary>
        /// 检查产品是否为多测模式（同一SN有两条或以上记录，可能包含[ACW]/[DCW]/[IR]）
        /// </summary>
        public static bool IsMultiTestMode(string WOCODE, string PARTNOID, string SN)
        {
            try
            {
                string _connstr = CheckDataBase(WOCODE, PARTNOID, SN);
                if (string.IsNullOrEmpty(_connstr)) return false;

                // LIMIT 3：覆盖 ACW + DCW + IR 最多三行
                string sql = $"SELECT TVInfo FROM BusbarCompressionData WHERE sn='{SN}' ORDER BY id DESC LIMIT 3";
                DataTable dt = Read(sql, _connstr);

                if (dt == null || dt.Rows.Count < 2) return false;

                bool hasACW = false, hasDCW = false, hasIR = false;
                for (int i = 0; i < dt.Rows.Count; i++)
                {
                    string tvInfo = dt.Rows[i]["TVInfo"]?.ToString() ?? "";
                    if (tvInfo.StartsWith("[ACW]")) hasACW = true;
                    else if (tvInfo.StartsWith("[DCW]")) hasDCW = true;
                    else if (tvInfo.StartsWith("[IR]")) hasIR = true;
                }

                int typeCount = (hasACW ? 1 : 0) + (hasDCW ? 1 : 0) + (hasIR ? 1 : 0);
                return typeCount >= 2;
            }
            catch (Exception ex)
            {
                WriteErrorLog("[数据库异常]IsMultiTestMode检查失败", $"异常: {ex.Message}", SN, WOCODE);
                return false;
            }
        }

        /// <summary>
        /// 获取多测模式下的综合测试结果（ACW/DCW/IR）
        /// 只有找到的所有测试类型均合格时才返回合格，否则返回 NG2（耐压不合格）
        /// </summary>
        public static int GetMultiTestResult(string WOCODE, string PARTNOID, string SN,
            out bool acwResult, out bool dcwResult, out bool irResult)
        {
            acwResult = false;
            dcwResult = false;
            irResult = false;

            try
            {
                string _connstr = CheckDataBase(WOCODE, PARTNOID, SN);
                if (string.IsNullOrEmpty(_connstr)) return 2;

                string sql = $"SELECT TVInfo, TVRESULT, TVMAXVOLTAGE FROM BusbarCompressionData WHERE sn='{SN}' ORDER BY id DESC LIMIT 3";
                DataTable dt = Read(sql, _connstr);
                if (dt == null) return 2;

                bool acwFound = false, dcwFound = false, irFound = false;

                for (int i = 0; i < dt.Rows.Count; i++)
                {
                    string tvInfo = dt.Rows[i]["TVInfo"]?.ToString() ?? "";
                    bool tvResult = TryParseDbBool(dt.Rows[i]["TVRESULT"]);

                    float tvMaxVoltage = 0;
                    float.TryParse(dt.Rows[i]["TVMAXVOLTAGE"]?.ToString(), out tvMaxVoltage);

                    bool isPass = tvResult && tvMaxVoltage > 0;

                    if (tvInfo.StartsWith("[ACW]"))
                    {
                        acwFound = true;
                        // ACW/DCW：tvMaxVoltage=-1 或 0 视为无效
                        acwResult = isPass && tvMaxVoltage != -1;
                    }
                    else if (tvInfo.StartsWith("[DCW]"))
                    {
                        dcwFound = true;
                        dcwResult = isPass && tvMaxVoltage != -1;
                    }
                    else if (tvInfo.StartsWith("[IR]"))
                    {
                        irFound = true;
                        // IR：tvMaxVoltage 存储绝缘电阻（>0 即有效）
                        irResult = tvResult && tvMaxVoltage > 0;
                    }
                }

                // 所有找到的测试类型都必须通过
                bool allPass = (!acwFound || acwResult) && (!dcwFound || dcwResult) && (!irFound || irResult);
                return allPass ? 0 : 2;
            }
            catch (Exception ex)
            {
                WriteErrorLog("[数据库异常]GetMultiTestResult失败", $"异常: {ex.Message}", SN, WOCODE);
                return 2;
            }
        }

        /// <summary>
        /// 解析数据库中的“布尔”字段：兼容 1/0、true/false、True/False 等历史写法。
        /// </summary>
        private static bool TryParseDbBool(object value)
        {
            try
            {
                if (value == null || value == DBNull.Value) return false;

                // 先按整数解析（SQLite里常见 1/0）
                if (value is long l) return l != 0;
                if (value is int i) return i != 0;
                if (value is short s) return s != 0;
                if (value is byte b) return b != 0;

                var text = value.ToString()?.Trim();
                if (string.IsNullOrEmpty(text)) return false;

                if (int.TryParse(text, out var n)) return n != 0;
                if (bool.TryParse(text, out var bb)) return bb;

                // 一些奇怪的写法兜底
                if (string.Equals(text, "Y", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(text, "N", StringComparison.OrdinalIgnoreCase)) return false;
            }
            catch
            {
                // ignore
            }
            return false;
        }

        /// <summary>
        /// 更新产品的第二次拍照（外观检测/AOI）结果。
        /// CHECK2 的最终出站判定以普通非 IR 电测记录作为基础行；AOI 结果也写回该行，
        /// 避免 IR 独立电测行成为同 SN 最新记录时截走外观结果。
        /// </summary>
        /// <param name="TakePhoto2">AOI外观检测是否合格</param>
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

                string targetSql = $"SELECT ID, TAKEPHOTO2 FROM BusbarCompressionData WHERE id=(SELECT max(id) from BusbarCompressionData WHERE sn='{SN}' AND {NonIrTvInfoCondition})";
                DataTable beforeUpdate = Read(targetSql, _connstr);
                string targetId = beforeUpdate != null && beforeUpdate.Rows.Count > 0
                    ? beforeUpdate.Rows[0]["ID"]?.ToString()
                    : string.Empty;
                string beforeTakePhoto2 = beforeUpdate != null && beforeUpdate.Rows.Count > 0
                    ? beforeUpdate.Rows[0]["TAKEPHOTO2"]?.ToString()
                    : string.Empty;

                if (string.IsNullOrWhiteSpace(targetId))
                {
                    WriteErrorLog("[追踪]UpdateTakePhoto2-目标行不存在",
                        $"待写入TAKEPHOTO2={(TakePhoto2 ? 1 : 0)}({(TakePhoto2 ? "OK" : "NG")})，条件=SN最新非IR行，写入将返回失败",
                        SN, WOCODE);
                }

                string sql = $"UPDATE BusbarCompressionData SET TAKEPHOTO2 ={(TakePhoto2 ? 1 : 0)} WHERE id=(SELECT max(id) from BusbarCompressionData WHERE sn='{SN}' AND {NonIrTvInfoCondition})";
                int c = excute_sql(sql, _connstr);
                DataTable afterUpdate = Read(targetSql, _connstr);
                string afterTakePhoto2 = afterUpdate != null && afterUpdate.Rows.Count > 0
                    ? afterUpdate.Rows[0]["TAKEPHOTO2"]?.ToString()
                    : string.Empty;

                WriteErrorLog("[追踪]UpdateTakePhoto2-写入依据",
                    $"targetId={targetId}, 待写入TAKEPHOTO2={(TakePhoto2 ? 1 : 0)}({(TakePhoto2 ? "OK" : "NG")}), 写入前=[{beforeTakePhoto2}], 写入后=[{afterTakePhoto2}], affectedRows={c}, 条件=SN最新非IR行",
                    SN, WOCODE);
                return c > 0;
            }
            catch (Exception ex)
            {
                WriteErrorLog("[数据库异常]UpdateTakePhoto2失败", $"异常: {ex.Message}", SN, WOCODE);
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
                    if (File.Exists(defaultdb))
                    {
                        File.Copy(defaultdb, db);
                        WriteErrorLog("[数据库信息]CheckDataBase-新建数据库",
                            $"从模板创建新数据库: {db}", SN, WOCODE);
                        return $"Data Source={db};Pooling=true;FailIfMissing=false";
                    }
                    else
                    {
                        // 模板数据库不存在
                        // 同步在UI界面提示：模板库缺失时，指导用户把 default.db 复制到指定目录
                        UiLog?.Invoke($"模板数据库不存在请复制文件到目录: {defaultdb}");
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
        /// 第一次综合校验：检查产品是否通过了前置工序的所有测试项目。
        /// CHECK1 的基础字段来自同 SN 最新非 IR 记录，IR 行只在多测综合判定中按 [IR] 前缀参与，
        /// 避免把绝缘电阻复用到 TVMAXVOLTAGE 后误当作普通耐压最大电压。
        /// 业务场景：在CHECK1工位（外观检测前）进行的数据完整性和合格性校验
        /// 校验项目：拍照留底(TakePhoto1) + 耐压测试(TVResult) + 电阻测试(RES)
        /// 返回值说明：
        ///   0 = 全部合格
        ///   1 = 拍照留底不良（TakePhoto1=false 或数据解析失败 或数据库异常）
        ///   2 = 耐压测试不合格（TVMaxVoltage异常 或 TVResult=false）
        ///   3 = 阻值或压力测试不合格（RES 超阈值、PRESSURE_RESULT=false 或相关字段缺失/解析失败）
        /// 注意：所有系统异常都会被映射为业务不良返回，通过独立日志详细记录实际原因
        /// </summary>
        /// <returns>错误代码：0=合格，1=拍照不良，2=耐压不良，3=阻值或压力不良</returns>
        public static int Check1(string WOCODE, string PARTNOID, string SN, float resMax = 50, bool aoiOnlyMode = false, bool tvOnlyMode = false)
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
                    return tvOnlyMode ? 2 : 1;
                }

                if (!string.IsNullOrEmpty(_connstr))
                {
                    string sql = $"SELECT ID, SN, TAKEPHOTO1, RES, TVMAXVOLTAGE, TVRESULT, PRESSURE_RESULT FROM BusbarCompressionData  where ID=(SELECT max(ID)  FROM BusbarCompressionData WHERE sn='{SN}' AND {NonIrTvInfoCondition})";
                    DataTable dt = Read(sql, _connstr);
                    
                    if (dt == null || dt.Rows.Count <= 0)
                    {
                        // 【系统异常】数据不存在
                        WriteErrorLog("[数据缺失]CHECK1-记录不存在",
                            $"产品数据不存在，可能未扫码进站或数据库记录丢失，SQL=[{sql}]，返回值=1",
                            SN, WOCODE);
                        return tvOnlyMode ? 2 : 1;
                    }
                    
                    if (dt != null && dt.Rows.Count > 0)
                    {
                        // 记录查询到的记录详情
                        string recordId = dt.Rows[0]["SN"]?.ToString() ?? "未知";
                        string dbPath = _connstr.Replace("Data Source=", "").Replace(";Pooling=true;FailIfMissing=false", "");
                        WriteErrorLog("[调试信息]CHECK1-查询记录详情",
                            $"数据库文件={dbPath}, readId={dt.Rows[0]["ID"]?.ToString()}, SN={recordId}, TAKEPHOTO1原始值=[{dt.Rows[0]["TAKEPHOTO1"]?.ToString()}], RES=[{dt.Rows[0]["RES"]?.ToString()}], TVRESULT=[{dt.Rows[0]["TVRESULT"]?.ToString()}]",
                            SN, WOCODE);
                        
                        if (!tvOnlyMode)
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

                            _takephoto1 = TryParseDbBool(dt.Rows[0]["TAKEPHOTO1"]);

                            if (!_takephoto1)
                            {
                                WriteErrorLog("[业务判定]CHECK1-拍照留底不良",
                                    $"TAKEPHOTO1字段值为false，拍照留底失败，返回值=1，数据库文件={dbPath}，查询SQL={sql}",
                                    SN, WOCODE);
                                return 1;
                            }

                            // AOI-only模式：仅检查拍照留底，不检查任何电测相关字段（TV/RES/压力）
                            // 说明：该模式下不伪造耐压/电测记录，电测字段允许为空或默认值。
                            if (aoiOnlyMode)
                            {
                                return 0;
                            }
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
                        if (_res > resMax)
                        {
                            WriteErrorLog("[追踪]CHECK1-阻值阈值判定NG3",
                                $"RES={_res} > resMax={resMax}，直接返回=3(阻值不合格)",
                                SN, WOCODE);
                            return 3;
                        }

                        // 4. 阻值合格后，继续校验耐压字段
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

                            _tvresult = TryParseDbBool(dt.Rows[0]["TVRESULT"]);

                            // 5. 判断耐压测试结果
                            if (_tvmaxvoltage == 0 || _tvmaxvoltage == -1)
                            {
                                return 2;
                            }
                            if (!_tvresult)
                            {
                                return 2;
                            }

                        // 6. 判断压力结果（与耐压同优先级，失败即返回NG3）
                        bool _pressureResult = false;
                        string s_pressureResult = dt.Rows[0]["PRESSURE_RESULT"]?.ToString();

                        if (string.IsNullOrWhiteSpace(s_pressureResult))
                        {
                            if (!tvOnlyMode)
                            {
                                WriteErrorLog("[数据缺失]CHECK1-字段为空-PRESSURE_RESULT",
                                   "PRESSURE_RESULT字段为空，可能是UpdatePressure执行失败，返回值=3",
                                   SN, WOCODE);
                                return 3;
                            }
                        }
                        else
                        {
                            // 兼容1/0和True/False两种存储格式
                            if (s_pressureResult == "1")
                            {
                                _pressureResult = true;
                            }
                            else if (s_pressureResult == "0")
                            {
                                _pressureResult = false;
                            }
                            else if (!bool.TryParse(s_pressureResult, out _pressureResult))
                            {
                                WriteErrorLog("[数据异常]CHECK1-字段解析错误-PRESSURE_RESULT",
                                   $"PRESSURE_RESULT字段解析失败，原始值=[{s_pressureResult}]，返回值=3",
                                   SN, WOCODE);
                                return 3;
                            }

                            if (!_pressureResult)
                            {
                                return 3;
                            }
                        }

                        // 7. 检查是否为双测模式
                        if (IsMultiTestMode(WOCODE, PARTNOID, SN))
                        {
                            bool acwResult, dcwResult, irResult;
                            int multiResult = GetMultiTestResult(WOCODE, PARTNOID, SN, out acwResult, out dcwResult, out irResult);
                            if (multiResult != 0)
                            {
                                WriteErrorLog("[多测模式]CHECK-综合判断",
                                    $"ACW={acwResult}, DCW={dcwResult}, IR={irResult}, 综合={multiResult}",
                                    SN, WOCODE);
                                return multiResult;
                            }
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

            return tvOnlyMode ? 2 : 1;
        }

        /// <summary>
        /// 双Y仅电测归档校验：跳过拍照/AOI，保留阻值、耐压与 ACW/DCW 双测综合判定。
        /// </summary>
        public static int CheckElectricalOnly(string WOCODE, string PARTNOID, string SN, float resMax = 50)
        {
            return Check1(WOCODE, PARTNOID, SN, resMax, aoiOnlyMode: false, tvOnlyMode: true);
        }

        /// <summary>
        /// 双Y仅电测归档校验。
        /// 该口径跳过拍照和 AOI 字段，按当前测试模式校验期望 ACW/DCW 行，避免单测产品被双测要求误判。
        /// </summary>
        /// <param name="WOCODE">当前产品工单号，用于定位本地工单数据库。</param>
        /// <param name="PARTNOID">当前产品规格编码。</param>
        /// <param name="SN">当前产品序列号。</param>
        /// <param name="resMax">阻值合格上限，单位沿用工艺参数配置。</param>
        /// <param name="requireAcw">true 表示本轮流程必须存在有效 ACW 过程行。</param>
        /// <param name="requireDcw">true 表示本轮流程必须存在有效 DCW 过程行。</param>
        /// <param name="recordIds">双Y当前工位会话的测试行 ID；传入集合时判定范围严格限定为本轮测试。</param>
        /// <returns>错误代码：0=合格，2=期望耐压数据缺失或不良，3=阻值或压力不良。</returns>
        public static int CheckElectricalOnlyDualTest(string WOCODE, string PARTNOID, string SN, float resMax = 50,
            bool requireAcw = true, bool requireDcw = true, IEnumerable<long> recordIds = null)
        {
            if (!requireAcw && !requireDcw)
            {
                requireAcw = true;
            }

            List<ElectricalTestProcessRow> rows = recordIds == null
                ? GetElectricalTestProcessRows(WOCODE, PARTNOID, SN)
                : GetElectricalTestProcessRowsByIds(WOCODE, PARTNOID, SN, recordIds);
            List<ElectricalTestProcessRow> expectedRows = rows
                .Where(r => (requireAcw && string.Equals(r.TestMode, "ACW", StringComparison.OrdinalIgnoreCase))
                    || (requireDcw && string.Equals(r.TestMode, "DCW", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            List<ElectricalTestProcessRow> effectiveRows = expectedRows
                .GroupBy(r => r.TestMode, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(r => r.Id).First())
                .ToList();

            bool acwOk = !requireAcw || effectiveRows.Any(r => string.Equals(r.TestMode, "ACW", StringComparison.OrdinalIgnoreCase)
                && r.TVResult && r.TVMaxVoltage > 0 && r.TVMaxVoltage != -1);
            bool dcwOk = !requireDcw || effectiveRows.Any(r => string.Equals(r.TestMode, "DCW", StringComparison.OrdinalIgnoreCase)
                && r.TVResult && r.TVMaxVoltage > 0 && r.TVMaxVoltage != -1);

            if (!acwOk || !dcwOk)
            {
                WriteErrorLog("[多测模式]双Y电测综合判断",
                    $"期望ACW={requireAcw}, ACW={(acwOk ? "OK" : "NG")}, 期望DCW={requireDcw}, DCW={(dcwOk ? "OK" : "NG")}, rows={rows.Count}",
                    SN, WOCODE);
                return 2;
            }

            ElectricalTestProcessRow resNgRow = effectiveRows.FirstOrDefault(r => r.Res > resMax);
            if (resNgRow != null)
            {
                WriteErrorLog("[多测模式]双Y阻值判断",
                    $"模式={resNgRow.TestMode}, RES={FormatSqlNumber(resNgRow.Res)} > resMax={FormatSqlNumber(resMax)}",
                    SN, WOCODE);
                return 3;
            }

            ElectricalTestProcessRow pressureNgRow = effectiveRows.FirstOrDefault(r => !r.PressureResult);
            if (pressureNgRow != null)
            {
                WriteErrorLog("[多测模式]双Y压力判断",
                    $"模式={pressureNgRow.TestMode}, PRESSURE_RESULT=NG",
                    SN, WOCODE);
                return 3;
            }

            return 0;
        }

        /// <summary>
        /// 第二次综合校验：检查产品是否通过了所有工序的测试项目（包括外观检测）。
        /// CHECK2 的基础字段来自同 SN 最新非 IR 记录，IR 行仍通过多测综合结果参与最终判定，
        /// 但不作为普通耐压、压力或 AOI 基础行读取。
        /// 业务场景：在CHECK2工位（最终下料前）进行的全流程数据校验
        /// 校验项目：Check1的所有项 + 外观检测(TakePhoto2/AOI)
        /// 返回值说明：
        ///   0 = 全部合格
        ///   1 = 拍照留底不良（或数据库异常）
        ///   2 = 耐压测试不合格
        ///   3 = 阻值或压力测试不合格
        ///   4 = AOI外观检测不合格（TakePhoto2=false）
        /// 设计思路：Check2是最终出站校验，确保所有工序数据完整且合格
        /// 注意：所有系统异常都会被映射为业务不良返回，通过独立日志详细记录实际原因
        /// </summary>
        /// <returns>错误代码：0=合格，1~3同Check1，4=外观不良</returns>
        public static int Check2(string WOCODE, string PARTNOID, string SN, float resMax = 50, bool aoiOnlyMode = false)
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
                    string sql = $"SELECT ID, SN, TAKEPHOTO1, RES, TVMAXVOLTAGE, TVRESULT, PRESSURE_RESULT, TAKEPHOTO2 FROM BusbarCompressionData  where ID=(SELECT max(ID)  FROM BusbarCompressionData WHERE sn='{SN}' AND {NonIrTvInfoCondition})";
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
                        WriteErrorLog("[追踪]CHECK2-TAKEPHOTO2读取依据",
                            $"readId={dt.Rows[0]["ID"]?.ToString()}, TAKEPHOTO2原始值=[{dt.Rows[0]["TAKEPHOTO2"]?.ToString()}], 解析值={(TryParseDbBool(dt.Rows[0]["TAKEPHOTO2"]) ? "OK" : "NG")}, 条件=SN最新非IR行",
                            SN, WOCODE);

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

                        _takephoto1 = TryParseDbBool(dt.Rows[0]["TAKEPHOTO1"]);

                        if (!_takephoto1)
                        {
                            return 1;
                        }

                        // AOI-only模式：跳过全部电测（TV/RES/压力），仅检查AOI外观(TAKEPHOTO2)
                        // 说明：该模式下不伪造耐压/电测记录，电测字段允许为空或默认值。
                        if (!aoiOnlyMode)
                        {
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
                            if (_res > resMax)
                            {
                                WriteErrorLog("[追踪]CHECK2-阻值阈值判定NG3",
                                    $"RES={_res} > resMax={resMax}，直接返回=3(阻值不合格)",
                                    SN, WOCODE);
                                return 3;
                            }

                            // 4. 阻值合格后，继续校验耐压字段
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

                                _tvresult = TryParseDbBool(dt.Rows[0]["TVRESULT"]);

                                // 5. 判断耐压测试结果
                                if (_tvmaxvoltage == 0 || _tvmaxvoltage == -1)
                                {
                                    return 2;
                                }
                                if (!_tvresult)
                                {
                                    return 2;
                                }

                            // 6. 判断压力结果（与耐压同优先级，失败即返回NG2）
                            bool _pressureResult = false;
                            string s_pressureResult = dt.Rows[0]["PRESSURE_RESULT"]?.ToString();

                            if (string.IsNullOrWhiteSpace(s_pressureResult))
                            {
                                WriteErrorLog("[数据缺失]CHECK2-字段为空-PRESSURE_RESULT",
                                   "PRESSURE_RESULT字段为空，可能是UpdatePressure执行失败，返回值:3",
                                   SN, WOCODE);
                                return 3;
                            }

                            // 兼容1/0和True/False两种存储格式
                            if (s_pressureResult == "1")
                            {
                                _pressureResult = true;
                            }
                            else if (s_pressureResult == "0")
                            {
                                _pressureResult = false;
                            }
                            else if (!bool.TryParse(s_pressureResult, out _pressureResult))
                            {
                                WriteErrorLog("[数据异常]CHECK2-字段解析错误-PRESSURE_RESULT",
                                   $"PRESSURE_RESULT字段解析失败，原始值=[{s_pressureResult}]，返回值:3",
                                   SN, WOCODE);
                                return 3;
                            }

                            if (!_pressureResult)
                            {
                                return 3;
                            }
                        }

                        // 7. 解析AOI外观检测
                        bool _takephoto2 = false;
                        string s_takephoto2 = dt.Rows[0]["TAKEPHOTO2"]?.ToString();
                        
                        if (string.IsNullOrWhiteSpace(s_takephoto2))
                        {
                            WriteErrorLog("[数据缺失]CHECK2-字段为空-TAKEPHOTO2",
                                "TAKEPHOTO2字段为空，可能是UpdateTakePhoto2执行失败，返回值:4",
                                SN, WOCODE);
                            return 4;
                        }

                        _takephoto2 = TryParseDbBool(dt.Rows[0]["TAKEPHOTO2"]);

                        if (!_takephoto2)
                        {
                            return 4;
                        }

                        // 8. 检查是否为双测模式（AOI-only模式下跳过电测，因此无需做双测综合判断）
                        if (!aoiOnlyMode && IsMultiTestMode(WOCODE, PARTNOID, SN))
                        {
                            bool acwResult, dcwResult, irResult;
                            int multiResult = GetMultiTestResult(WOCODE, PARTNOID, SN, out acwResult, out dcwResult, out irResult);
                            if (multiResult != 0)
                            {
                                WriteErrorLog("[多测模式]CHECK2-综合判断",
                                    $"ACW={acwResult}, DCW={dcwResult}, IR={irResult}, 综合={multiResult}",
                                    SN, WOCODE);
                                return multiResult;
                            }
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
