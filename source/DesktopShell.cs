using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace AnnaDesktopPet
{
    // Owns the notification icon and the native registrations; no keyboard hook or key logging.
    internal sealed class DesktopShell : IDisposable
    {
        private const int ToggleId = 0xA110, ChatId = 0xA111, StopId = 0xA112;
        private readonly Window pet;
        private readonly Action openChat, stopSpeech;
        private readonly Action<bool> visibilityChanged;
        private readonly List<Window> hiddenWindows = new List<Window>();
        private readonly List<IntPtr> hiddenDialogs = new List<IntPtr>();
        private readonly List<int> registered = new List<int>();
        private readonly List<string> shortcutStatus = new List<string>();
        private readonly HwndSource source;
        private readonly IntPtr nativeHandle;
        private readonly Forms.NotifyIcon tray;
        private readonly Drawing.Icon icon;
        private readonly Forms.ContextMenuStrip menu;
        private readonly Forms.ToolStripMenuItem toggleItem;
        private bool disposed;
        internal bool Hidden { get; private set; }
        internal int RegisteredCount { get { return registered.Count; } }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr window, int id);
        private delegate bool WindowVisitor(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumWindows(WindowVisitor visitor, IntPtr parameter);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int count);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);

        internal DesktopShell(Window owner, Action chat, Action stop, Action<bool> changed, bool diagnostic = false)
        {
            pet = owner; openChat = chat; stopSpeech = stop; visibilityChanged = changed;
            source = HwndSource.FromHwnd(new WindowInteropHelper(pet).Handle);
            if (source == null) throw new InvalidOperationException("桌宠窗口尚未准备好。");
            nativeHandle = source.Handle;
            source.AddHook(WindowMessage);
            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "杏菜Q版头像.ico");
                try { icon = File.Exists(iconPath) ? new Drawing.Icon(iconPath, 32, 32) : (Drawing.Icon)Drawing.SystemIcons.Application.Clone(); }
                catch { icon = (Drawing.Icon)Drawing.SystemIcons.Application.Clone(); }
                menu = new Forms.ContextMenuStrip();
                toggleItem = AddItem("隐藏桌宠与面板    Ctrl+Alt+H", Toggle);
                AddItem("和杏菜聊天    Ctrl+Alt+C", ShowChat);
                AddItem("停止配音    Ctrl+Alt+S", stopSpeech);
                AddItem("语音聊天 · 按住鼠标后退侧键", delegate { MessageBox.Show(pet,"按住鼠标后退侧键（XButton1）说中文，松开后识别并发送。\n最长30秒，需要默认麦克风、中文识别组件和 GPT API Key。\n识别在本机进行，识别文字发送给 GPT；回复显示中文并使用当前日语声线。","杏菜 · 语音聊天"); });
                menu.Items.Add(new Forms.ToolStripSeparator());
                AddItem("快捷键说明…", ShowHelp);
                menu.Items.Add(new Forms.ToolStripSeparator());
                AddItem("退出桌宠", delegate { pet.Close(); });
                tray = new Forms.NotifyIcon { Text = "杏菜桌宠 · 双击显示/隐藏", Icon = icon, ContextMenuStrip = menu };
                tray.DoubleClick += delegate { Invoke(Toggle); };
                tray.Visible = !diagnostic;
                // Diagnostics use alternate virtual keys, without occupying the user's shortcuts.
                Register(ToggleId, diagnostic ? 0x85u : 0x48u, "Ctrl+Alt+H");
                Register(ChatId, diagnostic ? 0x86u : 0x43u, "Ctrl+Alt+C");
                Register(StopId, diagnostic ? 0x87u : 0x53u, "Ctrl+Alt+S");
            }
            catch { Dispose(); throw; }
        }

        private Forms.ToolStripMenuItem AddItem(string title, Action action)
        {
            var item = new Forms.ToolStripMenuItem(title);
            item.Click += delegate { Invoke(action); };
            menu.Items.Add(item); return item;
        }
        private void Invoke(Action action)
        {
            if (disposed) return;
            if (pet.Dispatcher.CheckAccess()) action();
            else pet.Dispatcher.BeginInvoke(new Action(delegate { if (!disposed) action(); }));
        }
        private void Register(int id, uint key, string name)
        {
            if (RegisterHotKey(nativeHandle, id, 0x4003, key))
            { registered.Add(id); shortcutStatus.Add(name + "：可用"); }
            else
            {
                int error = Marshal.GetLastWin32Error();
                shortcutStatus.Add(name + "：注册失败（错误 " + error + "，可能被其他软件占用），请使用托盘菜单");
                Program.Log("Hotkey " + name + " unavailable: " + new Win32Exception(error).Message);
            }
        }
        private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (disposed || message != 0x0312) return IntPtr.Zero;
            int id = wParam.ToInt32();
            if (!registered.Contains(id)) return IntPtr.Zero;
            handled = true;
            if (id == ToggleId) Toggle();
            else if (id == ChatId) ShowChat();
            else if (id == StopId) stopSpeech();
            return IntPtr.Zero;
        }
        internal void Toggle() { if (Hidden) Restore(); else HideAll(); }
        internal void HideAll()
        {
            if (disposed || Hidden) return;
            Hidden = true; hiddenWindows.Clear();
            hiddenDialogs.Clear();
            uint ourProcess = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            // File pickers and message boxes are native dialogs, not Application.Windows.
            EnumWindows(delegate(IntPtr window, IntPtr parameter) {
                uint process; GetWindowThreadProcessId(window, out process);
                if (process == ourProcess && IsWindowVisible(window))
                {
                    var name = new System.Text.StringBuilder(128); GetClassName(window, name, name.Capacity);
                    if (name.ToString() == "#32770") hiddenDialogs.Add(window);
                }
                return true;
            }, IntPtr.Zero);
            var visible = new List<Window>();
            foreach (Window window in Application.Current.Windows)
                if (window != pet && window.IsVisible) visible.Add(window);
            // Remember before hiding owners (which can also hide their owned windows).
            hiddenWindows.AddRange(visible);
            visibilityChanged(true); stopSpeech();
            foreach (IntPtr window in hiddenDialogs) ShowWindow(window, 0);
            foreach (Window window in visible) window.Hide();
            pet.Hide();
            toggleItem.Text = "显示桌宠与面板    Ctrl+Alt+H";
        }
        internal void Restore()
        {
            if (disposed) return;
            pet.Show();
            if (!Hidden) return;
            Hidden = false;
            var alive = new HashSet<Window>();
            foreach (Window window in Application.Current.Windows) alive.Add(window);
            foreach (Window window in hiddenWindows)
            {
                if (!alive.Contains(window)) continue;
                // Showing a panel must not pull focus from the user's current app.
                bool activate = window.ShowActivated;
                window.ShowActivated = false;
                window.Show();
                window.ShowActivated = activate;
            }
            hiddenWindows.Clear();
            foreach (IntPtr window in hiddenDialogs)
            {
                uint process; GetWindowThreadProcessId(window, out process);
                if (IsWindow(window) && process == (uint)System.Diagnostics.Process.GetCurrentProcess().Id) ShowWindow(window, 4);
            }
            hiddenDialogs.Clear();
            toggleItem.Text = "隐藏桌宠与面板    Ctrl+Alt+H";
            visibilityChanged(false);
        }
        internal void ShowChat() { if (disposed) return; Restore(); openChat(); }
        internal void ShowHelp()
        {
            MessageBox.Show(pet, "Ctrl + Alt + H：隐藏 / 显示桌宠及面板\nCtrl + Alt + C：呼出聊天\nCtrl + Alt + S：停止日语配音\n\n双击托盘头像也可显示 / 隐藏。\n隐藏时停止当前配音并暂停自主活动；QQ 音乐不受影响。\n专注到点仍会计时，提示会留到恢复桌宠后显示。\n\n" + String.Join("\n", shortcutStatus.ToArray()), "杏菜 · 托盘与快捷键");
        }
        internal void VerifyMessageDispatch()
        {
            if (registered.Count != 3) throw new InvalidOperationException("Diagnostic hotkeys could not be registered. " + String.Join("; ", shortcutStatus.ToArray()));
            bool handled = false;
            WindowMessage(source.Handle, 0x0312, new IntPtr(ToggleId), IntPtr.Zero, ref handled);
            if (!handled || !Hidden || pet.IsVisible) throw new InvalidOperationException("Hide hotkey failed.");
            handled = false;
            WindowMessage(source.Handle, 0x0312, new IntPtr(ChatId), IntPtr.Zero, ref handled);
            if (!handled || Hidden || !pet.IsVisible) throw new InvalidOperationException("Chat hotkey failed to restore.");
            handled = false;
            WindowMessage(source.Handle, 0x0312, new IntPtr(StopId), IntPtr.Zero, ref handled);
            if (!handled) throw new InvalidOperationException("Stop speech hotkey failed.");
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (int id in registered) UnregisterHotKey(nativeHandle, id);
            registered.Clear(); if (!source.IsDisposed) source.RemoveHook(WindowMessage);
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            if (menu != null) menu.Dispose();
            if (icon != null) icon.Dispose();
        }
    }
}
