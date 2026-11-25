using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace AnnaDesktopPet {
    internal sealed class RoutineSettings {
        public bool Enabled=true;
        public int Evening=22, QuietStart=23, QuietEnd=8, Interval=120;
        public string Greeted="";
        public DateTime LastReminder=DateTime.MinValue;
    }
    internal sealed class Routine {
        private RoutineSettings settings;
        private readonly DispatcherTimer timer=new DispatcherTimer();
        private readonly Action<string,string> notify;
        private string PathName {get{return Path.Combine(Program.DataDir,"routine.json");}}
        internal Routine(Action<string,string> callback) {
            notify=callback; settings=new RoutineSettings();
            try { if(File.Exists(PathName))settings=new JavaScriptSerializer().Deserialize<RoutineSettings>(File.ReadAllText(PathName,Encoding.UTF8))??new RoutineSettings(); }catch { }
            timer.Interval=TimeSpan.FromSeconds(30); timer.Tick+=delegate { Check(); };
        }
        internal void Start() { if(Program.Smoke)return; timer.Start(); Check(); }
        internal void Stop() {timer.Stop();}
        private void Save() { if(Program.Smoke)return; Directory.CreateDirectory(Program.DataDir); string temp=PathName+".tmp"; File.WriteAllText(temp,new JavaScriptSerializer().Serialize(settings),Encoding.UTF8); if(File.Exists(PathName))File.Replace(temp,PathName,null);else File.Move(temp,PathName); }
        internal static bool Quiet(int hour,int start,int end) { return start==end || (start<end ? hour>=start && hour<end : hour>=start || hour<end); }
        private void Check() {
            if(!settings.Enabled)return;
            DateTime now=DateTime.Now;
            if(Quiet(now.Hour,settings.QuietStart,settings.QuietEnd))return;
            try {
                string day=now.ToString("yyyy-MM-dd");
                if(settings.Greeted!=day) {
                    settings.Greeted=day; settings.LastReminder=now; Save();
                    notify(now.Hour<12?"早上好！今天也一起加油吧。":now.Hour<18?"下午好，今天过得怎么样？":"晚上好！忙完也记得照顾自己哦。",now.Hour<12?"おはよう！今日も一緒に頑張ろうね。":now.Hour<18?"こんにちは！今日はどんな一日？":"こんばんは！無理しないでね。"); return;
                }
                if(now.Hour>=settings.Evening && (now-settings.LastReminder).TotalMinutes>=settings.Interval) {
                    settings.LastReminder=now; Save(); notify("已经不早啦，休息一下眼睛，也准备早点睡吧。","もう遅いよ。目を休めて、そろそろ寝る準備をしようね。");
                }
            }catch { Program.Log("Routine reminder could not be saved."); }
        }
        internal void ShowSettings()
        {
            var window = new Window { Title = "杏菜 · 作息问候", Width = 450, SizeToContent = SizeToContent.Height };
            var form = new StackPanel { Margin = new Thickness(20, 18, 20, 18) };
            window.Content = form;
            var enabled = new CheckBox { Name = "RoutineEnabled", Content = "开启每日首次问候和晚间提醒",
                IsChecked = settings.Enabled, FontSize = 13, Foreground = GptChatWindow.PanelText };
            form.Children.Add(enabled);

            var evening = NumberBox("EveningHour", "晚间提醒开始时间", settings.Evening);
            var interval = NumberBox("IntervalMinutes", "提醒间隔", settings.Interval);
            AddTimeGroup(form, "晚间提醒", "开始时间", evening, "0–23 时", "提醒间隔", interval, "15–720 分钟");
            var start = NumberBox("QuietStartHour", "安静开始时间", settings.QuietStart);
            var end = NumberBox("QuietEndHour", "安静结束时间", settings.QuietEnd);
            AddTimeGroup(form, "安静时段", "开始时间", start, "0–23 时", "结束时间", end, "0–23 时");
            var note = GptUi.Text("安静时段内暂停问候和提醒。开始与结束相同表示全天安静。", 12, GptChatWindow.PanelQuiet);
            note.Margin = new Thickness(0, 9, 0, 0);
            form.Children.Add(note);
            var error = new TextBlock { Name = "RoutineValidation", TextWrapping = TextWrapping.Wrap,
                Foreground = GptUi.Brush(153, 58, 66), Visibility = Visibility.Collapsed, Margin = new Thickness(0, 9, 0, 0) };
            form.Children.Add(error);
            var fields = new[] { evening, interval, start, end };
            var save = GptChatWindow.ChatButton("保存设置", delegate {
                var values = new int[fields.Length];
                for (int i = 0; i < fields.Length; i++)
                {
                    int low = i == 1 ? 15 : 0, high = i == 1 ? 720 : 23;
                    if (!Int32.TryParse(fields[i].Text, out values[i]) || values[i] < low || values[i] > high)
                    {
                        error.Text = System.Windows.Automation.AutomationProperties.GetName(fields[i]) + "请输入 " + low + " 到 " + high + " 之间的整数。";
                        error.Visibility = Visibility.Visible;
                        fields[i].Focus();
                        fields[i].SelectAll();
                        return;
                    }
                }
                var previous = settings;
                settings = new RoutineSettings { Enabled = enabled.IsChecked == true, Evening = values[0], Interval = values[1],
                    QuietStart = values[2], QuietEnd = values[3], Greeted = previous.Greeted, LastReminder = DateTime.Now };
                try { Save(); }
                catch {
                    settings = previous;
                    error.Text = "保存失败，请检查配置目录。";
                    error.Visibility = Visibility.Visible;
                    return;
                }
                window.Close();
            }, true);
            save.Name = "SaveRoutine";
            save.IsDefault = true;
            save.Margin = new Thickness(0, 16, 0, 0);
            form.Children.Add(save);
            PanelTheme.Apply(window, "作息问候", "DAILY LIFE");
            window.Show();
        }

        private static TextBox NumberBox(string name, string label, int value)
        {
            var input = new TextBox { Name = name, Text = value.ToString(), MinHeight = 32,
                VerticalContentAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(input, label);
            return input;
        }

        private static void AddTimeGroup(Panel form, string title, string leftLabel, TextBox first, string leftRange,
            string rightLabel, TextBox second, string rightRange)
        {
            var heading = GptUi.Text(title, 14, GptChatWindow.PanelText);
            heading.FontWeight = FontWeights.SemiBold;
            heading.Margin = new Thickness(0, 17, 0, 7);
            form.Children.Add(heading);
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            AddNumberColumn(row, 0, leftLabel, first, leftRange);
            AddNumberColumn(row, 2, rightLabel, second, rightRange);
            form.Children.Add(row);
        }

        private static void AddNumberColumn(Grid row, int column, string label, TextBox input, string range)
        {
            var box = new StackPanel();
            Grid.SetColumn(box, column);
            var caption = GptUi.Text(label, 12, GptChatWindow.PanelQuiet);
            caption.Margin = new Thickness(0, 0, 0, 4);
            box.Children.Add(caption);
            box.Children.Add(input);
            var hint = GptUi.Text(range, 11, GptChatWindow.PanelQuiet);
            hint.Margin = new Thickness(0, 3, 0, 0);
            box.Children.Add(hint);
            row.Children.Add(box);
        }
    }
}
