// 独立进程验证已构建程序的真实存图方法；通过未初始化的视图模型避开相机、PLC和机器人连接。
// 参数：主程序 bin/Debug 目录、独立测试输出目录。使用 .NET Framework x64 编译运行。
using HalconDotNet;
using System;
using System.Collections;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

internal static class AoiImageSaveSmoke
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static Type viewModelType;
    private static object viewModel;
    private static Assembly application;
    private static string output;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string binaries = Path.GetFullPath(args[0]);
            output = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(output);
            Environment.CurrentDirectory = output;
            AppDomain.CurrentDomain.AssemblyResolve += (sender, eventArgs) =>
            {
                string file = Path.Combine(binaries, new AssemblyName(eventArgs.Name).Name + ".dll");
                return File.Exists(file) ? Assembly.LoadFrom(file) : null;
            };
            application = Assembly.LoadFrom(Path.Combine(binaries, "BusbarCompressionSystem.exe"));
            viewModelType = application.GetType("BusbarCompressionSystem.ViewModel.MainViewModel", true);
            viewModel = FormatterServices.GetUninitializedObject(viewModelType);
            SetField(viewModel, "_aoiImageSaveLogLock", new object());
            SetField(viewModel, "writeLog_Locker", new object());

            FrozenImagesSurviveSourceDisposal();
            FileFailureAllowsRemainingFilesAndNextBatch();
            WaitAndShutdownObserveCompletion();
            RepeatedBatchesReleaseImages();
            CommandReplyUsesCompletedGroup();
            Console.WriteLine("PASS: prepared overlays, source ownership, paired files, failure continuation, wait, shutdown, repeated disposal, command replies");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void FrozenImagesSurviveSourceDisposal()
    {
        HObject source = CreateImage(0);
        object batch = CreateBatch(source, "A2");
        object tool = CreateTemplateTool();
        for (int i = 0; i < 3; i++)
        {
            string original = Path.Combine(output, "产品甲", "原图", i + ".jpg");
            string annotated = Path.Combine(output, "产品甲", "标注图", i + ".jpg");
            object prepared = Call("PrepareToolResultImages", source, tool, "A2", original, annotated);
            Assert(!File.Exists(original) && !File.Exists(annotated), "准备阶段应只有内存图片");
            Bitmap bitmap = (Bitmap)GetField(prepared, "AnnotatedBitmap");
            Assert(bitmap != null, "真实HALCON窗口和文字应生成标注图");
            using (var bytes = new MemoryStream())
            {
                bitmap.Save(bytes, ImageFormat.Jpeg);
                File.WriteAllBytes(Path.Combine(output, "expected-" + i + ".jpg"), bytes.ToArray());
            }
            ((IList)GetField(batch, "Images")).Add(prepared);
        }

        // 模拟下一帧覆盖工具状态并释放上一帧相机对象。
        tool.GetType().GetProperty("Name").SetValue(tool, "后续产品工具");
        tool.GetType().GetProperty("ActualScore").SetValue(tool, 0.01);
        source.Dispose();
        Call("StartAoiImageSave", batch);
        Call("WaitForAoiImageSave", "测试下一帧");
        for (int i = 0; i < 3; i++)
        {
            string original = Path.Combine(output, "产品甲", "原图", i + ".jpg");
            string annotated = Path.Combine(output, "产品甲", "标注图", i + ".jpg");
            Assert(File.ReadAllBytes(annotated).SequenceEqual(File.ReadAllBytes(Path.Combine(output, "expected-" + i + ".jpg"))),
                "后台必须保存准备阶段的标注像素");
            using (var saved = new Bitmap(original))
                Assert(saved.Width == 640 && saved.Height == 480 && saved.GetPixel(300, 300).R < 5, "原图副本应可独立保存");
        }
        Assert(!((HObject)GetField(batch, "OriginalImage")).IsInitialized(), "整批完成应释放原图副本");
        Console.WriteLine("PASS: original/annotation pairs preserve pixels after source disposal and tool mutation");
    }

    private static void FileFailureAllowsRemainingFilesAndNextBatch()
    {
        string blocked = Path.Combine(output, "目录位置已是文件");
        File.WriteAllText(blocked, "occupied");
        using (HObject source = CreateImage(160))
        {
            object batch = CreateBatch(source, "A3");
            AddPrepared(batch, Path.Combine(blocked, "bad.jpg"), Path.Combine(output, "失败后标注.jpg"));
            AddPrepared(batch, Path.Combine(output, "后续原图.jpg"), Path.Combine(output, "后续标注.jpg"));
            Call("StartAoiImageSave", batch);
            Call("WaitForAoiImageSave", "失败批次结束");
            Assert(File.Exists(Path.Combine(output, "失败后标注.jpg")) && File.Exists(Path.Combine(output, "后续原图.jpg")),
                "原图失败后仍应尝试标注与后续工具");

            object next = CreateBatch(source, "A4");
            AddPrepared(next, Path.Combine(output, "下一帧原图.jpg"), Path.Combine(output, "下一帧标注.jpg"));
            Call("StartAoiImageSave", next);
            Call("WaitForAoiImageSave", "下一批结束");
            Assert(File.Exists(Path.Combine(output, "下一帧标注.jpg")), "上一批失败后下一批应继续");
        }
        string log = File.ReadAllText(Directory.GetFiles(Path.Combine(output, "日志", "AOI存图")).Single());
        Assert(log.Contains(blocked) && log.Contains("失败=1"), "保存失败应记录完整路径和数量");
        Console.WriteLine("PASS: file failure is recorded and remaining files/next batch continue");
    }

    private static void WaitAndShutdownObserveCompletion()
    {
        foreach (string method in new[] { "WaitForAoiImageSave", "StopAoiImageSaving" })
        {
            var completion = new TaskCompletionSource<bool>();
            SetField(viewModel, "_aoiImageSaveTask", completion.Task);
            using (var entered = new ManualResetEventSlim())
            {
                Task waiting = Task.Run(() =>
                {
                    entered.Set();
                    if (method == "WaitForAoiImageSave") Call(method, "慢盘测试");
                    else Call(method);
                });
                Assert(entered.Wait(2000), "等待线程应启动");
                Assert(!waiting.Wait(100), "存图结束前应持续等待");
                completion.SetResult(true);
                Assert(waiting.Wait(3000), "存图结束后应继续");
                Assert(GetField(viewModel, "_aoiImageSaveTask") == null, "等待完成应清除任务引用");
            }
        }
        Assert((bool)GetField(viewModel, "_aoiImageSavingStopped"), "退出应关闭后续处理入口");
        SetField(viewModel, "_aoiImageSavingStopped", false);

        var fault = new TaskCompletionSource<bool>();
        fault.SetException(new IOException("injected worker failure"));
        SetField(viewModel, "_aoiImageSaveTask", fault.Task);
        Call("WaitForAoiImageSave", "任务失败测试");
        Assert(GetField(viewModel, "_aoiImageSaveTask") == null, "任务异常应记录并结束等待");
        Console.WriteLine("PASS: slow save/shutdown wait for completion; faulted task permits continuation");
    }

    private static void RepeatedBatchesReleaseImages()
    {
        for (int i = 0; i < 20; i++)
        {
            using (HObject source = CreateImage(i * 10))
            {
                object batch = CreateBatch(source, "repeat-" + i);
                object prepared = AddPrepared(batch, Path.Combine(output, "repeat-original.jpg"), Path.Combine(output, "repeat-annotated.jpg"));
                var bitmap = new Bitmap(64, 64);
                SetField(prepared, "AnnotatedBitmap", bitmap);
                Call("StartAoiImageSave", batch);
                Call("WaitForAoiImageSave", "连续帧");
                Assert(!((HObject)GetField(batch, "OriginalImage")).IsInitialized(), "每批原图都应释放");
                bool disposed = false;
                try { bitmap.GetPixel(0, 0); } catch (ArgumentException) { disposed = true; }
                Assert(disposed, "每批标注位图都应释放");
            }
        }
        object empty = Activator.CreateInstance(viewModelType.GetNestedType("AoiImageSaveBatch", BindingFlags.NonPublic), true);
        Call("StartAoiImageSave", empty);
        Assert(GetField(viewModel, "_aoiImageSaveTask") == null, "无图片时直接完成");
        Console.WriteLine("PASS: twenty consecutive batches release native images and bitmaps; empty batch completes");
    }

    private static void CommandReplyUsesCompletedGroup()
    {
        object data = Uninitialized("BusbarCompressionSystem.Model.DataModel");
        object vision = Uninitialized("BusbarCompressionSystem.FaraVision.FaraVisionDataModel");
        object process = Uninitialized("BusbarCompressionSystem.FaraVision.Processmodel");
        object setting = Uninitialized("BusbarCompressionSystem.Model.SettingModel");
        var collection = (IList)Activator.CreateInstance(process.GetType().GetProperty("Tools").PropertyType);
        SetProperty(process, "Tools", collection);
        SetProperty(vision, "Processmodel", process);
        SetProperty(data, "FaraVisionDataModel", vision);
        SetProperty(data, "Settingmodel", setting);
        SetProperty(viewModel, "DataModel", data);

        object first = CreateTemplateTool();
        object last = CreateTemplateTool();
        object locator = CreateTemplateTool();
        foreach (object tool in new[] { first, last, locator })
        {
            SetProperty(tool, "Command", "A2");
            SetProperty(tool, "OKCMD", "OK");
            SetProperty(tool, "NG1CMD", "NG");
            SetProperty(tool, "NG2CMD", "NG2");
            collection.Add(tool);
        }
        SetProperty(locator, "TestMode", Enum.Parse(locator.GetType().GetProperty("TestMode").PropertyType, "模板定位"));
        SetStatus(locator, "NG2");
        SetProperty(first, "SendStatus", true);
        SetProperty(last, "SendStatus", false);
        SetProperty(locator, "SendStatus", true);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            using (var client = new TcpClient())
            {
                client.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
                using (TcpClient peer = listener.AcceptTcpClient())
                {
                    object server = Activator.CreateInstance(setting.GetType().GetProperty("TcpServerRobot").PropertyType);
                    SetField(server, "_stream", peer.GetStream());
                    SetField(server, "_tcpClient", peer);
                    SetField(server, "isConnected", true);
                    SetProperty(setting, "TcpServerRobot", server);
                    NetworkStream received = client.GetStream();
                    received.ReadTimeout = 2000;
                    SetStatus(last, "等待中");
                    Call("SendAoiCommandResult", "A2");
                    Assert(!received.DataAvailable, "有等待工具时保持等待");
                    SetStatus(last, "NG");
                    Call("SendAoiCommandResult", "A2");
                    AssertReply(received, "NG");
                    SetStatus(last, "OK");
                    Call("SendAoiCommandResult", "A2");
                    AssertReply(received, "OK");
                    SetStatus(first, "NG");
                    SetStatus(last, "NG2");
                    SetProperty(last, "SendStatus", true);
                    SetProperty(last, "NG2CMD", "CUSTOM_NG2");
                    Call("SendAoiCommandResult", "A2");
                    AssertReply(received, "CUSTOM_NG2");
                    SetProperty(first, "SendStatus", false);
                    SetProperty(last, "SendStatus", false);
                    Call("SendAoiCommandResult", "A2");
                    Assert(!received.DataAvailable, "判定工具全部关闭发送时保持静默");
                }
            }
        }
        finally { listener.Stop(); }
        Console.WriteLine("PASS: group completion, single loopback reply, NG2 precedence, configured codes, locator exclusion, disabled sending");
    }

    private static void AssertReply(NetworkStream stream, string expected)
    {
        byte[] buffer = new byte[expected.Length];
        int offset = 0;
        while (offset < buffer.Length)
        {
            int count = stream.Read(buffer, offset, buffer.Length - offset);
            Assert(count > 0, "测试连接应收到完整回包");
            offset += count;
        }
        Assert(Encoding.ASCII.GetString(buffer) == expected && !stream.DataAvailable, "每个指令组应只发送一个预期结果");
    }

    private static object Uninitialized(string type) { return FormatterServices.GetUninitializedObject(application.GetType(type, true)); }
    private static void SetProperty(object value, string name, object data) { value.GetType().GetProperty(name).SetValue(value, data); }
    private static void SetStatus(object tool, string status)
    {
        SetProperty(tool, "ToolStatus", Enum.Parse(tool.GetType().GetProperty("ToolStatus").PropertyType, status));
    }

    private static HObject CreateImage(int gray)
    {
        HObject blank, image;
        HOperatorSet.GenImageConst(out blank, "byte", 640, 480);
        using (blank) HOperatorSet.ScaleImage(blank, out image, 1, gray);
        return image;
    }

    private static object CreateBatch(HObject source, string command)
    {
        object batch = Activator.CreateInstance(viewModelType.GetNestedType("AoiImageSaveBatch", BindingFlags.NonPublic), true);
        HObject copy;
        HOperatorSet.CopyImage(source, out copy);
        SetField(batch, "OriginalImage", copy);
        SetField(batch, "Command", command);
        return batch;
    }

    private static object AddPrepared(object batch, string original, string annotated)
    {
        object image = Activator.CreateInstance(viewModelType.GetNestedType("AoiPreparedImage", BindingFlags.NonPublic), true);
        SetField(image, "OriginalPath", original);
        SetField(image, "AnnotatedPath", annotated);
        ((IList)GetField(batch, "Images")).Add(image);
        return image;
    }

    private static object CreateTemplateTool()
    {
        Type type = application.GetType("BusbarCompressionSystem.Model.FaraVision.Tool.ToolModel", true);
        object tool = FormatterServices.GetUninitializedObject(type);
        type.GetProperty("TestMode").SetValue(tool, Enum.Parse(type.GetProperty("TestMode").PropertyType, "模板匹配"));
        type.GetProperty("ToolStatus").SetValue(tool, Enum.Parse(type.GetProperty("ToolStatus").PropertyType, "OK"));
        type.GetProperty("Index").SetValue(tool, 1);
        type.GetProperty("Name").SetValue(tool, "本次工具");
        type.GetProperty("ActualScore").SetValue(tool, 0.98);
        object roi = Activator.CreateInstance(type.GetProperty("PositionROI").PropertyType);
        foreach (string name in new[] { "Row1", "Col1", "Row2", "Col2" })
        {
            PropertyInfo property = roi.GetType().GetProperty(name);
            property.SetValue(roi, Convert.ChangeType(name.EndsWith("1") ? 100 : 350, property.PropertyType));
        }
        type.GetProperty("PositionROI").SetValue(tool, roi);
        return tool;
    }

    private static object Call(string name, params object[] args)
    {
        return viewModelType.GetMethod(name, Members).Invoke(viewModel, args);
    }

    private static object GetField(object value, string name) { return value.GetType().GetField(name, Members).GetValue(value); }
    private static void SetField(object value, string name, object data) { value.GetType().GetField(name, Members).SetValue(value, data); }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
