using System;
using System.Runtime.InteropServices;
namespace AnnaDesktopPet {
    internal static class LifeActivity {
        [StructLayout(LayoutKind.Sequential)] private struct Input {public uint Size,Tick;}
        [StructLayout(LayoutKind.Sequential)] private struct Rect {public int Left,Top,Right,Bottom;}
        [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref Input info);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window,out Rect rect);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        private static DateTime checkedAt=DateTime.MinValue;
        private static bool busy;
        internal static bool Busy {
            get {
                if((DateTime.UtcNow-checkedAt).TotalSeconds<2)return busy;
                checkedAt=DateTime.UtcNow;
                Input input=new Input{Size=(uint)Marshal.SizeOf(typeof(Input))};
                bool recent=GetLastInputInfo(ref input) && unchecked((uint)Environment.TickCount-input.Tick)<20000;
                Rect rect; bool fullscreen=GetWindowRect(GetForegroundWindow(),out rect) && rect.Left<=0 && rect.Top<=0 && rect.Right>=GetSystemMetrics(0) && rect.Bottom>=GetSystemMetrics(1);
                return busy=recent || fullscreen;
            }
        }
    }
}
