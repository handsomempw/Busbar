using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SystemFree
{



    public class FreeTest
    {

        public FreeTest()
        {
            new Thread(() =>
           {

               while (true)
               {
                   if (keepOnline)
                   {
                       var r = GetLastInputTime();
                       if (r / 1000 > TriggerSecond)
                       {
                           SystemFreeReceived.Invoke(this, new SystemFreeEventArgs()
                           {
                               MilSeconds = r,
                               Seconds = (float)(r / 1000.0),
                               Minutes = (float)(r / 1000.0 / 60.0)
                           });
                       }
                   }
                   else
                   {

                   }
                   Thread.Sleep(1000);

               }
           }).Start();

        }

        public event EventHandler SystemFreeReceived;

        // 导入Windows API  
        [StructLayout(LayoutKind.Sequential)]
        struct LASTINPUTINFO
        {
            public int cbSize;
            public uint dwTime;
        }
        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        // 获取空闲时间（毫秒）  
        public static long GetLastInputTime()
        {
            LASTINPUTINFO vLastInputInfo = new LASTINPUTINFO();
            vLastInputInfo.cbSize = Marshal.SizeOf(vLastInputInfo);
            if (!GetLastInputInfo(ref vLastInputInfo))
            {
                return 0;
            }
            else
            {
                return Environment.TickCount - (long)vLastInputInfo.dwTime;
            }
        }


        public int TriggerSecond { set; get; } = 10 * 60;

        private bool keepOnline { set; get; } = false;
        Thread t;

        public void Start()
        {
            if (t == null)
            {
                keepOnline = true;

            }
        }

        public void Start(int Seconds)
        {
            TriggerSecond = Seconds;
            Start();
        }

        public void Stop()
        {

        }


    }

    public class SystemFreeEventArgs : EventArgs
    {
        public float MilSeconds { set; get; }
        public float Seconds { set; get; }
        public float Minutes { set; get; }

    }



}
