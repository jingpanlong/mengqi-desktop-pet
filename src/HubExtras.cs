using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace Mengqi
{
    internal sealed partial class HubForm
    {
        private readonly Stopwatch stopwatch=new Stopwatch();
        private readonly Timer toolsTimer=new Timer {Interval=100};
        /// <summary>开源仓库地址：README、Release 下载页都在这里。</summary>
        internal const string GitHubUrl="https://github.com/jingpanlong/mengqi-desktop-pet";
        private Label stopwatchLabel;
        internal void VerifyToolActions()
        {
            FeatureTests.PrepareCapture(this);SelectPage("tools");Application.DoEvents();
            int before=pet.data.Reminders.Count;FindButton(pages["tools"],"创建倒计时").PerformClick();if(pet.data.Reminders.Count!=before+1) throw new InvalidOperationException("倒计时没有创建。");
            FindButton(pages["tools"],"开始 / 暂停").PerformClick();System.Threading.Thread.Sleep(20);FindButton(pages["tools"],"开始 / 暂停").PerformClick();if(stopwatch.IsRunning || stopwatch.ElapsedMilliseconds<1) throw new InvalidOperationException("秒表控制失败。");
            FindButton(pages["tools"],"记录到笔记").PerformClick();if(!pet.data.Notes.Contains("秒表 ·")) throw new InvalidOperationException("秒表记录未保存。");FindButton(pages["tools"],"清零").PerformClick();if(stopwatch.ElapsedTicks!=0) throw new InvalidOperationException("秒表未清零。");
            if(!AllText(pages["about"]).Contains("微信公众号：景同志") || !AllText(pages["about"]).Contains("小红书：景同学")) throw new InvalidOperationException("关于页作者信息缺失。");
            if(!AllText(pages["about"]).Contains(GitHubUrl) || !AllText(pages["about"]).Contains("打开发布页下载")) throw new InvalidOperationException("关于页缺少 GitHub 开源下载入口。");
            Hide();
        }
        private static Button FindButton(Control parent,string text) {foreach(Control child in parent.Controls) {Button button=child as Button;if(button!=null && button.Text==text) return button;Button nested=FindButton(child,text);if(nested!=null) return nested;}return null;}
        private static string AllText(Control parent) {string text=parent.Text;foreach(Control child in parent.Controls) text+=AllText(child);return text;}
        private void BuildTools()
        {
            Panel body=Page("tools","小工具","倒计时、秒表、常用语和动画调节，让陪伴更顺手。");FlowLayoutPanel area=Ui.Stack();body.Controls.Add(area);
            area.Controls.Add(Ui.Label("快捷倒计时",680,32));
            FlowLayoutPanel countdown=new FlowLayoutPanel {Size=new Size(680,50)};NumericUpDown minutes=new NumericUpDown {Minimum=1,Maximum=1440,Value=10,Width=75};TextBox title=new TextBox {Text="倒计时结束啦",Width=260};countdown.Controls.Add(minutes);countdown.Controls.Add(Ui.Label("分钟",52,28));countdown.Controls.Add(title);countdown.Controls.Add(Ui.Button("创建倒计时",delegate {pet.AddReminder(new Reminder {Title=title.Text,Category="自定义",Next=DateTime.Now.AddMinutes((double)minutes.Value)});}));area.Controls.Add(countdown);
            area.Controls.Add(Ui.Label("秒表（关闭主窗口仍继续计时，退出软件后清零）",680,32));stopwatchLabel=Ui.Label("00:00:00.0",680,52);stopwatchLabel.Font=new Font("Microsoft YaHei UI",23,FontStyle.Bold);area.Controls.Add(stopwatchLabel);
            FlowLayoutPanel watch=new FlowLayoutPanel {Size=new Size(680,48)};watch.Controls.Add(Ui.Button("开始 / 暂停",delegate {if(stopwatch.IsRunning) stopwatch.Stop();else stopwatch.Start();}));watch.Controls.Add(Ui.Button("清零",delegate {stopwatch.Reset();}));watch.Controls.Add(Ui.Button("记录到笔记",delegate {pet.data.Notes+=Environment.NewLine+"秒表 · "+DateTime.Now.ToString("HH:mm")+" · "+stopwatch.Elapsed.ToString(@"hh\:mm\:ss\.f");pet.SaveData();}));area.Controls.Add(watch);
            toolsTimer.Tick+=delegate {stopwatchLabel.Text=stopwatch.Elapsed.ToString(@"hh\:mm\:ss\.f");};toolsTimer.Start();Disposed+=delegate {toolsTimer.Dispose();};
            area.Controls.Add(Ui.Label("动画设置",680,32));FlowLayoutPanel motion=new FlowLayoutPanel {Size=new Size(680,50)};CheckBox smooth=new CheckBox {Text="平滑过渡",Checked=pet.data.SmoothMotion,Width=140};NumericUpDown speed=new NumericUpDown {Minimum=50,Maximum=150,Value=pet.data.MotionSpeed,Width=75};motion.Controls.Add(smooth);motion.Controls.Add(Ui.Label("播放速度 %",130,28));motion.Controls.Add(speed);motion.Controls.Add(Ui.Button("应用动画设置",delegate {pet.data.SmoothMotion=smooth.Checked;pet.data.MotionSpeed=(int)speed.Value;pet.SaveData();}));area.Controls.Add(motion);
            area.Controls.Add(Ui.Label("常用语 · 保存后下次启动仍保留",680,32));ListBox phrases=new ListBox {Size=new Size(680,110)};foreach(string phrase in pet.data.QuickPhrases) phrases.Items.Add(phrase);area.Controls.Add(phrases);
            FlowLayoutPanel phraseBar=new FlowLayoutPanel {Size=new Size(680,48)};TextBox phraseText=new TextBox {Width=280,MaxLength=100};phraseBar.Controls.Add(phraseText);phraseBar.Controls.Add(Ui.Button("保存常用语",delegate {if(!String.IsNullOrWhiteSpace(phraseText.Text)) {pet.data.QuickPhrases.Add(phraseText.Text.Trim());phrases.Items.Add(phraseText.Text.Trim());phraseText.Clear();pet.SaveData();}}));phraseBar.Controls.Add(Ui.Button("发到聊天",delegate {if(phrases.SelectedItem!=null) {input.Text=phrases.SelectedItem.ToString();SelectPage("chat");Send();}}));phraseBar.Controls.Add(Ui.Button("移除",delegate {if(phrases.SelectedIndex>=0) {pet.data.QuickPhrases.RemoveAt(phrases.SelectedIndex);phrases.Items.RemoveAt(phrases.SelectedIndex);pet.SaveData();}}));area.Controls.Add(phraseBar);
        }
        private void BuildAbout()
        {
            Panel body=Page("about","关于萌栖","让陪伴住进桌面。版本 1.7.3");FlowLayoutPanel area=Ui.Stack();body.Controls.Add(area);
            area.Controls.Add(Ui.Label("萌栖是一款桌面宠物陪伴软件。小黑会在桌面活动、回应互动，陪你聊天、专注和休息；你也可以上传自己的宠物照片，制作属于自己的桌宠。",680,95));
            area.Controls.Add(Ui.Label("支持离线陪伴、气泡聊天、工作与生活提醒、专注计时、待办笔记、宠物导入、备份和可选联网 AI。",680,70));
            Label openSource=Ui.Label("开源与下载",680,40);openSource.Font=new Font("Microsoft YaHei UI",16,FontStyle.Bold);area.Controls.Add(openSource);
            area.Controls.Add(Ui.Label("本项目已在 GitHub 开源（MIT 许可）。源码、使用说明和最新版安装包都在这里：\r\n"+GitHubUrl,680,84));
            FlowLayoutPanel links=new FlowLayoutPanel {Size=new Size(680,50)};
            links.Controls.Add(Ui.Button("打开发布页下载",delegate {OpenUrl(GitHubUrl+"/releases/latest");}));
            links.Controls.Add(Ui.Button("打开仓库主页",delegate {OpenUrl(GitHubUrl);}));
            links.Controls.Add(Ui.Button("复制仓库地址",delegate {try {Clipboard.SetText(GitHubUrl);} catch { }}));area.Controls.Add(links);
            Label author=Ui.Label("联系作者",680,40);author.Font=new Font("Microsoft YaHei UI",16,FontStyle.Bold);area.Controls.Add(author);
            area.Controls.Add(Ui.Label("微信公众号：景同志\n\n小红书：景同学",680,100));
            FlowLayoutPanel buttons=new FlowLayoutPanel {Size=new Size(680,50)};buttons.Controls.Add(Ui.Button("复制公众号名称",delegate {Clipboard.SetText("景同志");}));buttons.Controls.Add(Ui.Button("复制小红书名称",delegate {Clipboard.SetText("景同学");}));area.Controls.Add(buttons);
            area.Controls.Add(Ui.Label("离线功能在本机运行。联网 AI 需要用户自行配置服务；提醒需要软件运行、电脑保持唤醒。",680,70));
        }
        /// <summary>用系统默认浏览器打开链接；失败就静默忽略，不影响软件运行。</summary>
        private static void OpenUrl(string url)
        {
            try {Process.Start(new ProcessStartInfo(url) {UseShellExecute=true});} catch { }
        }
    }
}
