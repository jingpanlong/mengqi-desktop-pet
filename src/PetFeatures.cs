using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Speech.Synthesis;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Mengqi
{
    internal sealed partial class PetWindow
    {
        internal AppData data;
        internal DataStore store;
        internal Bitmap customImage;
        private bool customAtlas;
        private readonly Dictionary<int,Bitmap> customPoses=new Dictionary<int,Bitmap>();   // AI 生成或图集外的单行动作姿势
        private HubForm hub;
        private BubbleForm bubble;
        private SpeechSynthesizer voice;
        private DateTime lastFeatureTick=DateTime.MinValue;
        private readonly Queue<Reminder> pending=new Queue<Reminder>();
        private Form reminderWindow;
        private bool featureFocusRunning {get {return data!=null && data.FocusRunning;}}
        private bool shuttingDown;
        private bool clickThrough;
        internal string PetName {get {return data.Name;}}
        internal event EventHandler DataChanged;

        private void InitializeFeatures()
        {
            store=new DataStore();
            data=companionData!=null?companionData:store.Load();
            automatic=data.Automatic;automaticItem.Checked=automatic;
            TopMost=data.Topmost;topmostItem.Checked=TopMost;
            ApplyScale(data.Scale);
            if(companion) LoadPet(companionPetId);
            else if(!String.IsNullOrEmpty(data.PetId)) LoadPet(data.PetId);
            menu.Items[0].Text=MenuTitle(data.Name);
            ToolStripMenuItem interactions=new ToolStripMenuItem("互动");
            interactions.DropDownItems.Add("摸摸",null,delegate {Interact("pet");});
            interactions.DropDownItems.Add("喂食",null,delegate {Interact("feed");});
            interactions.DropDownItems.Add("玩一会儿",null,delegate {Interact("play");});
            interactions.DropDownItems.Add("睡觉 / 唤醒",null,delegate {Interact("sleep");});
            if(companion)
            {
                menu.Items.Insert(1,interactions);
                foreach(ToolStripItem item in menu.Items) if(item.Text=="退出萌栖") {item.Text="送它离开";break;}
                tray.Text="萌栖陪伴 · 右键打开菜单";
                return;
            }
            menu.Items.Insert(1,new ToolStripMenuItem("打开萌栖",null,delegate {OpenHub("chat");}));
            menu.Items.Insert(2,new ToolStripMenuItem("提醒与计划",null,delegate {OpenHub("reminders");}));
            menu.Items.Insert(3,new ToolStripMenuItem("专注计时",null,delegate {OpenHub("focus");}));
            menu.Items.Insert(4,interactions);
            menu.Items.Insert(5,BuildSystemMenu());
            ToolStripMenuItem call=new ToolStripMenuItem("再叫一只");
            call.DropDownOpening+=delegate {BuildCompanionItems(call);};
            BuildCompanionItems(call);
            menu.Items.Insert(6,call);
            ToolStripMenuItem through=new ToolStripMenuItem("鼠标穿透（托盘可恢复）");
            through.CheckOnClick=true;through.CheckedChanged+=delegate {clickThrough=through.Checked;SetClickThrough();};menu.Items.Insert(menu.Items.Count-2,through);
            tray.Text="萌栖 · 双击打开陪伴中心";
        }
        private ToolStripMenuItem BuildSystemMenu()
        {
            ToolStripMenuItem system=new ToolStripMenuItem("系统操作");
            system.DropDownItems.Add(new ToolStripMenuItem("删除文件…",null,delegate {MenuDeleteFiles();}));
            system.DropDownItems.Add(new ToolStripMenuItem("创建文件…",null,delegate {MenuCreate(false);}));
            system.DropDownItems.Add(new ToolStripMenuItem("创建文件夹…",null,delegate {MenuCreate(true);}));
            system.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem shots=new ToolStripMenuItem("截图");
            shots.DropDownItems.Add(new ToolStripMenuItem("全屏截图",null,delegate {TakeScreenshot();}));
            shots.DropDownItems.Add(new ToolStripMenuItem("区域截图…",null,delegate {TakeRegionScreenshot();}));
            system.DropDownItems.Add(shots);
            ToolStripMenuItem record=new ToolStripMenuItem("录屏");
            record.DropDownItems.Add(new ToolStripMenuItem("录制全屏",null,delegate {RecordScreen(true);}));
            record.DropDownItems.Add(new ToolStripMenuItem("录制选区…",null,delegate {RecordScreen(false);}));
            system.DropDownItems.Add(record);
            system.DropDownItems.Add(new ToolStripSeparator());
            system.DropDownItems.Add(new ToolStripMenuItem("清空回收站",null,delegate {EmptyBin();}));
            system.DropDownItems.Add(new ToolStripMenuItem("一键整理桌面",null,delegate {TidyDesktop();}));
            system.DropDownItems.Add(new ToolStripMenuItem("屏幕取色",null,delegate {PickColor();}));
            system.DropDownItems.Add(new ToolStripMenuItem("系统状态播报",null,delegate {ReportStatus();}));
            system.DropDownItems.Add(new ToolStripMenuItem("锁定屏幕",null,delegate {LockScreenNow();}));
            system.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem folders=new ToolStripMenuItem("打开常用文件夹");
            folders.DropDownItems.Add(new ToolStripMenuItem("桌面",null,delegate {OpenFolderSafe(SystemTools.DesktopFolder);}));
            folders.DropDownItems.Add(new ToolStripMenuItem("下载",null,delegate {OpenFolderSafe(SystemTools.DownloadsFolder);}));
            folders.DropDownItems.Add(new ToolStripMenuItem("文档",null,delegate {OpenFolderSafe(SystemTools.DocumentsFolder);}));
            folders.DropDownItems.Add(new ToolStripMenuItem("图片",null,delegate {OpenFolderSafe(SystemTools.PicturesFolder);}));
            system.DropDownItems.Add(folders);
            return system;
        }

        private void OpenFolderSafe(string path)
        {
            try {SystemTools.OpenFolder(path);Speak("打开啦："+Path.GetFileName(path.TrimEnd('\\')),4000);}
            catch(Exception error) {Play(5,2400,0);Speak("打不开："+error.Message,5000);}
        }

        private void FeaturesShown()
        {
            if(companion) return;                              // 陪伴桌宠不抢热键、不自报家门，位置由主人安排
            if(data.X!=int.MinValue && data.Y!=int.MinValue) {Location=new Point(data.X,data.Y);ConstrainToScreen();}
            Native.RegisterHotKey(Handle,712,0x0001|0x0004,0x4D);
            Speak("我来啦！双击我聊天，右键设置提醒。",7000);
        }
        // ---- 多桌宠同屏 ----
        /// <summary>「再叫一只」子菜单：内置两只 + 所有自定义宠物。每次展开时重建，宠物列表变了也跟得上。</summary>
        private void BuildCompanionItems(ToolStripMenuItem host)
        {
            host.DropDownItems.Clear();
            host.DropDownItems.Add(new ToolStripMenuItem("小黑",null,delegate {SpawnCompanion("");}));
            host.DropDownItems.Add(new ToolStripMenuItem("臭臭",null,delegate {SpawnCompanion("preset:chouchou");}));
            if(data.Pets.Count>0)
            {
                host.DropDownItems.Add(new ToolStripSeparator());
                foreach(PetEntry entry in data.Pets)
                {
                    string id=entry.Id;
                    host.DropDownItems.Add(new ToolStripMenuItem(entry.Name,null,delegate {SpawnCompanion(id);}));
                }
            }
        }
        internal void SpawnCompanion(string petId)
        {
            if(companion) return;
            if(Companions.Count>=4) {Speak("桌面上已经热热闹闹啦，最多同时四只哦。",5000);return;}
            try
            {
                AppData clone=DataStore.Validate(DataStore.Json().Deserialize<AppData>(DataStore.Json().Serialize(data)));
                clone.PetId=petId;
                PetWindow buddy=new PetWindow(clone,petId);
                Rectangle area=Screen.FromControl(this).WorkingArea;
                int x=Right+16;if(x+buddy.Width>area.Right) x=Math.Max(area.Left,Left-buddy.Width-16);
                buddy.Location=new Point(x,Top);
                buddy.Show(this);
                Play(3,2400,0);
                Speak("「"+buddy.PetName+"」也来啦，一起陪你！",5000);
            }
            catch(Exception error) {Play(5,2600,0);Speak("叫不动它："+error.Message,5000);}
        }
        protected override void WndProc(ref Message m)
        {
            if(m.Msg==0x0312 && m.WParam.ToInt32()==712) {OpenHub("chat");return;}
            base.WndProc(ref m);
        }
        private void SetClickThrough()
        {
            int style=Native.GetWindowLong(Handle,-20);
            Native.SetWindowLong(Handle,-20,clickThrough?style|0x20:style&~0x20);
        }
        internal void OpenHub(string page)
        {
            if(companion) {Play(3,2000,0);return;}             // 陪伴桌宠没有陪伴中心，挥挥爪就好
            if(hub==null || hub.IsDisposed) hub=new HubForm(this);
            hub.SelectPage(page);hub.Show();hub.WindowState=FormWindowState.Normal;hub.Activate();
        }
        internal void SaveData()
        {
            if(companion) return;                              // 陪伴桌宠的数据是克隆，落盘只由主桌宠负责
            data.Scale=scalePercent;data.Automatic=automatic;data.Topmost=TopMost;
            data.X=Left;data.Y=Top;
            store.Save(data);
            if(DataChanged!=null) DataChanged(this,EventArgs.Empty);
        }
        internal void Speak(string text,int duration)
        {
            if(data==null || shuttingDown) return;
            if(data.Bubbles)
            {
                if(bubble==null || bubble.IsDisposed) bubble=new BubbleForm();
                bubble.SetText(text,duration);PositionBubble();
                if(Visible && !bubble.Visible) bubble.Show(this);
            }
            if(data.Speech && !ReminderEngine.IsQuiet(data,DateTime.Now))
            {
                try {if(voice==null) voice=new SpeechSynthesizer();voice.SpeakAsyncCancelAll();voice.SpeakAsync(text);}
                catch {data.Speech=false;}
            }
        }
        private void PositionBubble()
        {
            if(bubble==null || bubble.IsDisposed) return;
            Rectangle area=Screen.FromRectangle(Bounds).WorkingArea;
            // 气泡画布四周留了投影边距、尾巴尖离画布边 20px，所以贴身距离按尾巴尖算
            bool below=Top-bubble.Height+14<area.Top;
            int y=below?Bottom+14:Top-bubble.Height+14;
            if(below && y+bubble.Height>area.Bottom) y=Math.Max(area.Top,area.Bottom-bubble.Height);
            bubble.PointTail(!below);
            bubble.Location=new Point(Math.Max(area.Left,Math.Min(area.Right-bubble.Width,Left+Width/2-bubble.Width/2)),y);
        }
        internal void VerifyVisibleBubbleReuse()
        {
            bubble=new BubbleForm();Location=new Point(-10000,-10000);ShowInTaskbar=false;Show();   // 画在屏幕外，不用 Opacity（会和分层窗口的 ULW 冲突）
            try {Speak("第一条气泡",5000);Speak("连续更新第二条气泡",5000);SendChat("1分钟后提醒我休息",CancellationToken.None).GetAwaiter().GetResult();if(!bubble.Visible) throw new InvalidOperationException("气泡没有显示。");}
            finally {Hide();bubble.Hide();}
        }
        private void TickFeatures()
        {
            if(data==null || shuttingDown || companion) return;
            DateTime now=DateTime.Now;
            if(Math.Abs((now-lastFeatureTick).TotalMilliseconds)<700) return;
            lastFeatureTick=now;
            List<Reminder> due=ReminderEngine.Poll(data,now);
            foreach(Reminder reminder in due) pending.Enqueue(reminder);
            bool changed=due.Count>0;
            int periods=(int)((now-data.LastStats.ToLocalTime()).TotalMinutes/30);
            if(periods>0)
            {
                int step=Math.Min(periods,48);
                data.Hunger=Math.Max(0,data.Hunger-step*2);data.Happiness=Math.Max(0,data.Happiness-step);
                data.Energy=state==9?Math.Min(100,data.Energy+step*8):Math.Max(0,data.Energy-step);
                data.LastStats=now;changed=true;
            }
            if(data.FocusRunning && !data.FocusPaused && now>=data.FocusDeadline)
            {
                data.FocusRunning=false;
                if(!data.FocusBreak) {data.Sessions++;data.FocusSeconds+=data.WorkMinutes*60;}
                string text=data.FocusBreak?"休息结束啦，要开始下一轮专注吗？":"这一轮专注完成啦！起来伸展一下，休息几分钟。";
                pending.Enqueue(new Reminder {Title=text,Category="专注",Enabled=false});
                Play(data.FocusBreak?3:10,2500,0);changed=true;
            }
            if(changed) SaveData();
            if(pending.Count>0 && (reminderWindow==null || reminderWindow.IsDisposed)) PresentReminder(pending.Dequeue());
            if(hub!=null && !hub.IsDisposed && hub.Visible) hub.UpdateLive();
        }
        private void PresentReminder(Reminder reminder)
        {
            Speak(reminder.Category+"提醒："+reminder.Title,12000);
            tray.ShowBalloonTip(10000,"萌栖 · "+reminder.Category,reminder.Title,ToolTipIcon.Info);
            if(data.Sound) System.Media.SystemSounds.Exclamation.Play();
            reminderWindow=new ReminderAlert(this,reminder);
            reminderWindow.Show();
        }
        internal void CompleteReminder(Reminder reminder)
        {
            data.Log.Add(DateTime.Now.ToString("yyyy-MM-dd HH:mm")+" · 已完成 · "+reminder.Title);SaveData();
        }
        internal void Snooze(Reminder reminder,int minutes)
        {
            // A snooze is a separate one-shot, so recurring schedules keep their next time.
            data.Reminders.Add(new Reminder {Title=reminder.Title,Category=reminder.Category,Next=DateTime.Now.AddMinutes(minutes)});
            SaveData();Speak(minutes+"分钟后再提醒你。",5000);
        }
        internal void AddReminder(Reminder reminder) {data.Reminders.Add(reminder);SaveData();Speak("设置好了："+reminder.Title+"，"+reminder.Next.ToString("M月d日 HH:mm"),6000);}
        internal void StartFocus(bool rest)
        {
            data.FocusBreak=rest;data.FocusRunning=true;data.FocusPaused=false;
            data.FocusDeadline=DateTime.Now.AddMinutes(rest?data.BreakMinutes:data.WorkMinutes);data.FocusLeft=0;
            Play(rest?9:7,0,0);SaveData();Speak(rest?"一起休息一会儿。":"开始专注啦，我会安静陪着你。",5000);
        }
        internal void PauseFocus()
        {
            if(!data.FocusRunning) return;
            if(data.FocusPaused) {data.FocusDeadline=DateTime.Now.AddSeconds(data.FocusLeft);data.FocusPaused=false;}
            else {data.FocusLeft=Math.Max(0,(data.FocusDeadline-DateTime.Now).TotalSeconds);data.FocusPaused=true;}
            SaveData();
        }
        internal void StopFocus() {data.FocusRunning=false;data.FocusPaused=false;Play(0,0,0);SaveData();}
        internal string FocusStatus()
        {
            if(!data.FocusRunning) return "准备开始";
            double seconds=data.FocusPaused?data.FocusLeft:Math.Max(0,(data.FocusDeadline-DateTime.Now).TotalSeconds);
            TimeSpan span=TimeSpan.FromSeconds(seconds);
            return (data.FocusPaused?"已暂停 · ":data.FocusBreak?"休息 · ":"专注 · ")+((int)span.TotalMinutes).ToString("00")+":"+span.Seconds.ToString("00");
        }
        internal void Interact(string action)
        {
            if(action=="feed") {data.Hunger=Math.Min(100,data.Hunger+20);data.Happiness=Math.Min(100,data.Happiness+5);Play(13,3000,0);Speak("喵，吃饱啦！谢谢你照顾我。",5000);}
            else if(action=="play") {data.Happiness=Math.Min(100,data.Happiness+15);data.Energy=Math.Max(0,data.Energy-5);Play(14,4000,0);Speak("来玩一会儿吧！",5000);}
            else if(action=="sleep") {if(state==9) {Play(0,0,0);Speak("醒啦，我们继续吧。",4000);} else {Play(9,0,0);Speak("我先睡一小会儿，你也记得休息。",5000);} }
            else {data.Happiness=Math.Min(100,data.Happiness+10);Play(3,2000,0);Speak("呼噜呼噜，喜欢你摸摸我。",4500);}
            SaveData();
        }
        internal void PreviewAction(int row) {Play(row,row==9?0:3200,0);}
        internal async Task<string> SendChat(string text,CancellationToken token)
        {
            if(String.IsNullOrWhiteSpace(text)) return "";
            text=text.Trim();if(text.Length>4000) throw new ArgumentException("单条消息请控制在 4000 字以内。");
            data.Chat.Add(new ChatEntry {Role="user",Text=text});SaveData();
            string reply=null;
            string createName;bool createFolder=false;
            Reminder reminder=ReminderEngine.Parse(text,DateTime.Now);
            if(reminder!=null) {AddReminder(reminder);reply="提醒已设置："+reminder.Title+"，"+reminder.Next.ToString("M月d日 HH:mm")+"，"+reminder.Repeat+"。";}
            else if(text=="喂食" || text=="吃饭") {Interact("feed");reply="谢谢你，我开饭啦。";}
            else if(text=="摸摸") {Interact("pet");reply="呼噜呼噜，心情变好啦。";}
            else if(text=="玩球") {Interact("play");reply="小球准备好啦，一起玩！";}
            else if(text=="睡觉" || text=="醒来") {if((text=="睡觉" && state!=9) || (text=="醒来" && state==9)) Interact("sleep");reply=text=="睡觉"?"晚安，我就在这里。":"醒来啦。";}
            else if(text.StartsWith("开始专注"))
            {
                System.Text.RegularExpressions.Match match=System.Text.RegularExpressions.Regex.Match(text,@"(\d{1,3})");
                if(match.Success) data.WorkMinutes=Math.Max(1,Math.Min(180,int.Parse(match.Value)));
                StartFocus(false);reply="已开始 "+data.WorkMinutes+" 分钟专注。";
            }
            else if(SystemOps.IsDeleteIntent(text))
            {
                StartDeleteTargeting();
                reply="瞄准模式开启！点中文件后我会跑过去问你，你点头我就把它吃掉——真删，不进回收站；按 Esc 取消。";
            }
            else if(SystemOps.TryParseCreate(text,out createName,out createFolder)) reply=CreateNamed(createName,createFolder,null);
            else if(data.Ai) reply=await ChatService.Online(data,token);
            else reply=ChatService.Offline(text,data,random);
            data.Chat.Add(new ChatEntry {Role="assistant",Text=reply});SaveData();Speak(reply,12000);return reply;
        }
        internal void PlayAction(int row,int milliseconds) {Play(row,milliseconds,0);}
        internal void ShowEffect(string kind) {EffectLayer.Launch(new Point(Left+Width/2,Top+Height/2),kind);}
        private TargetOverlay targeting;
        internal void StartDeleteTargeting()
        {
            if(confirm!=null && !confirm.IsDisposed) confirm.Close();
            if(targeting!=null && !targeting.IsDisposed) {targeting.Activate();return;}
            Play(8,0,0);
            targeting=new TargetOverlay(this);
            targeting.Show();targeting.Activate();
            BringToFront();
        }
        internal Point MouthPoint() {return new Point(Left+Width/2,Top+(int)(Height*0.30));}

        // ---- walk to the file, ask face to face, then eat it ----
        internal void WalkToAndAsk(FileTarget target)
        {
            Point home=Location;                                   // 事情办完要跑回原地
            Rectangle area=Screen.FromControl(this).WorkingArea;
            Rectangle icon=target.Rect;
            int x=icon.Left+icon.Width/2-Width/2;                 // 头顶正对文件，文件悬在嘴上方
            int y=icon.Bottom+6;
            if(y+Height>area.Bottom) y=icon.Top-Height-6;         // 下面放不下就站到文件上面
            x=Math.Max(area.Left,Math.Min(area.Right-Width,x));
            y=Math.Max(area.Top,Math.Min(area.Bottom-Height,y));
            Point goal=new Point(x,y);
            double distance=Math.Sqrt(Math.Pow(goal.X-Left,2)+Math.Pow(goal.Y-Top,2));
            int duration=(int)Math.Max(380,Math.Min(1500,distance/0.55));
            WalkTo(goal,duration,delegate {AskToDelete(target,icon,home);});
        }

        internal void WalkTo(Point goal,int durationMs,Action done)
        {
            Play(goal.X>=Left?1:2,0,0);
            Point start=Location;
            long started=clock.ElapsedMilliseconds;
            System.Windows.Forms.Timer walk=new System.Windows.Forms.Timer {Interval=16};
            walk.Tick+=delegate
            {
                if(IsDisposed || walk==null) {walk.Stop();walk.Dispose();return;}
                double t=(clock.ElapsedMilliseconds-started)/(double)durationMs;
                if(t>=1)
                {
                    walk.Stop();walk.Dispose();
                    Location=goal;preciseX=goal.X;
                    Play(0,0,0);
                    if(done!=null) done();
                    return;
                }
                double ease=t*t*(3-2*t);
                int x=start.X+(int)Math.Round((goal.X-start.X)*ease);
                int y=start.Y+(int)Math.Round((goal.Y-start.Y)*ease);
                Location=new Point(x,y);preciseX=x;
            };
            walk.Start();
        }

        private DeleteConfirm confirm;
        private void AskConfirm(string title,string detail,string yesLabel,string noLabel,Action yes,Action no)
        {
            if(confirm!=null && !confirm.IsDisposed) confirm.Close();
            confirm=new DeleteConfirm(title,detail,yesLabel,noLabel,yes,no);
            confirm.PositionAbove(this);
            confirm.Show(this);confirm.Activate();
        }

        private void AskToDelete(FileTarget target,Rectangle iconRect,Point home)
        {
            Play(8,0,0);
            Speak("让我看看……要把「"+target.Name+"」吃掉吗？",4000);
            string note=SystemOps.KindOf(target)=="文件夹"
                ? "整个文件夹都会被彻底删除，不进回收站，找不回来哦。"
                : "会被彻底删除，不进回收站，找不回来哦。";
            AskConfirm("把「"+target.Name+"」吃掉？",note,"吃掉它","先留着",
                delegate {ConfirmDelete(target,iconRect,home);},
                delegate {RefuseDelete(target,home);});
        }

        private void ConfirmDelete(FileTarget target,Rectangle iconRect,Point home)
        {
            Bitmap sprite=SystemOps.IconImageFor(target.Path);
            if(sprite==null) sprite=SystemOps.IconImageFor("");
            Play(13,currentPreset=="chouchou"?3800:2800,0);       // 吃饭动作：一口吞
            SwallowLayer.Launch(iconRect,MouthPoint(),sprite,delegate
            {
                try
                {
                    SystemOps.DeleteForever(target.Path);
                    ShowEffect("poof");
                    Speak("呼噜——「"+target.Name+"」进肚子啦！这次是真删，回收站里也没有。",6000);
                }
                catch(Exception error)
                {
                    Play(5,2600,0);
                    Speak("呃，没吞下去："+error.Message,6000);
                }
                WalkHome(home);                                    // 不管成没成都回原地
            });
        }

        private void RefuseDelete(FileTarget target,Point home)
        {
            Play(3,2200,0);
            Speak("好的，「"+target.Name+"」先留着力。",4000);
            WalkHome(home);
        }

        private void WalkHome(Point home)
        {
            double distance=Math.Sqrt(Math.Pow(home.X-Left,2)+Math.Pow(home.Y-Top,2));
            if(distance<8) {preciseX=home.X;Location=home;return;}
            int duration=(int)Math.Max(380,Math.Min(1500,distance/0.55));
            WalkTo(home,duration,null);
        }

        // ---- 右键「系统操作」里的六个本机功能 ----
        internal void MenuDeleteFiles()
        {
            Speak("好，进入瞄准模式，点中要删的文件就行。",4000);
            StartDeleteTargeting();
        }

        internal void MenuCreate(bool folder)
        {
            Play(8,0,0);
            CreateInputDialog dialog=new CreateInputDialog(
                folder?"新建一个文件夹":"新建一个文件",
                folder?"建在桌面上。":"建在桌面上；后缀可下拉选或自己填，留空自动补 .txt",
                folder?"新建文件夹":"新建文件",
                folder,
                delegate(string name) {Speak(CreateNamed(name,folder,null),6000);},
                delegate {Play(3,2200,0);Speak("好，那先不建。",3500);});
            dialog.PositionAbove(this);
            dialog.Show(this);dialog.Activate();
        }

        /// <summary>聊天和右键菜单共用：在桌面建文件/文件夹，返回给用户看的话。</summary>
        private string CreateNamed(string rawName,bool folder,string suffix)
        {
            try
            {
                string name=SystemOps.ComposeName(rawName,folder,suffix);
                string made=SystemOps.CreateItem(name,folder,null);
                Play(folder?7:3,3000,0);
                ShowEffect("sparkle");
                return "好啦，"+(folder?"文件夹":"文件")+"「"+Path.GetFileName(made)+"」已经在桌面等你了！";
            }
            catch(Exception error)
            {
                Play(5,2600,0);
                return "没建成："+error.Message;
            }
        }

        internal void EmptyBin()
        {
            SystemTools.BinInfo info=SystemTools.RecycleBinInfo();
            if(info.Items<=0) {Play(0,0,0);Speak("回收站已经很干净啦，没有东西可以清。",5000);return;}
            Play(8,0,0);
            AskConfirm("清空回收站？","里面有 "+info.Items+" 项（约 "+SystemTools.Mega(info.Bytes)+"），清空后找不回来哦。","清空","再想想",
                delegate
                {
                    try {SystemTools.EmptyRecycleBin();Play(13,3000,0);ShowEffect("poof");Speak("呼噜——回收站清空啦，清爽！",6000);}
                    catch(Exception error) {Play(5,2600,0);Speak("没清掉："+error.Message,6000);}
                },
                delegate {Play(3,2200,0);Speak("好，那先留着。",4000);});
        }

        internal void TidyDesktop()
        {
            string folder=SystemTools.DesktopFolder;
            int total;Dictionary<string,List<string>> plan=SystemTools.PlanTidy(folder,out total);
            if(total<2) {Play(0,0,0);Speak("桌面已经很整齐啦，没什么要整理的。",5000);return;}
            Play(8,0,0);
            AskConfirm("整理桌面？","把桌面上 "+total+" 个散落文件按类型归进 "+plan.Count+" 个文件夹（鼠标拖一下就能拿回来）。","整理一下","先不动",
                delegate
                {
                    string summary=SystemTools.TidyDesktop(plan,folder);
                    Play(7,3000,0);ShowEffect("sparkle");
                    Speak("整理好啦："+summary+"，都在桌面的分类文件夹里。",7000);
                },
                delegate {Play(3,2200,0);Speak("好，那就先保持原样。",4000);});
        }

        internal void TakeScreenshot()
        {
            Play(8,0,0);
            if(bubble!=null && bubble.Visible) bubble.Hide();
            SetVisibleForCapture(false);          // 先把自己藏起来，别进照片
            System.Windows.Forms.Timer wait=new System.Windows.Forms.Timer {Interval=200};
            wait.Tick+=delegate
            {
                wait.Stop();wait.Dispose();
                string path=null;string error=null;
                try {path=SystemTools.CaptureScreen(SystemTools.DesktopFolder);}
                catch(Exception problem) {error=problem.Message;}
                SetVisibleForCapture(true);
                FlashLayer.Launch();
                if(path==null) {Play(5,2600,0);Speak("没截到："+error,6000);return;}
                Play(3,2200,0);ShowEffect("sparkle");
                try {Clipboard.SetText(path);} catch { }
                Speak("咔嚓！截图放到桌面啦："+Path.GetFileName(path)+"（路径已帮你复制）",7000);
            };
            wait.Start();
        }

        internal void TakeRegionScreenshot()
        {
            Play(8,0,0);
            HideBubble();
            SetVisibleForCapture(false);                        // 别让桌宠混进截图里
            Speak("按住鼠标拖一个框，松手就截下来；Esc 取消。",5000);
            RegionSelectOverlay overlay=new RegionSelectOverlay(
                delegate(Rectangle region) {SaveRegionShot(region);},
                delegate {SetVisibleForCapture(true);Play(3,2200,0);Speak("不截了，回来陪你。",3000);});
            overlay.Show();overlay.Activate();
        }

        /// <summary>截图/录屏前把提示气泡收掉，不然会被一起拍进去。</summary>
        private void HideBubble() { if(bubble!=null && bubble.Visible) bubble.Hide(); }

        private void SaveRegionShot(Rectangle region)
        {
            HideBubble();
            System.Windows.Forms.Timer wait=new System.Windows.Forms.Timer {Interval=200};   // 等遮罩从屏幕上消失再截
            wait.Tick+=delegate
            {
                wait.Stop();wait.Dispose();
                string path=null;string error=null;
                try {path=SystemTools.CaptureRegion(region,SystemTools.DesktopFolder);}
                catch(Exception problem) {error=problem.Message;}
                SetVisibleForCapture(true);
                FlashLayer.Launch();
                if(path==null) {Play(5,2600,0);Speak("没截到："+error,6000);return;}
                Play(3,2200,0);ShowEffect("sparkle");
                try {Clipboard.SetText(path);} catch { }
                Speak("咔嚓！截图放到桌面啦："+Path.GetFileName(path)+"（路径已帮你复制）",7000);
            };
            wait.Start();
        }

        internal void RecordScreen(bool full)
        {
            Play(8,0,0);
            if(full) {StartRecording(SystemInformation.VirtualScreen);return;}
            Speak("按住鼠标拖一个框，框里的画面会被录下来；Esc 取消。",5000);
            RegionSelectOverlay overlay=new RegionSelectOverlay(StartRecording,
                delegate {Play(3,2200,0);Speak("不录了，回来陪你。",3000);});
            overlay.Show();overlay.Activate();
        }

        private ScreenRecorder recorder;
        private RecordBar recordBar;

        private void StartRecording(Rectangle region)
        {
            if(recorder!=null) {Speak("已经在录啦，先点右下角那个「停止」。",5000);return;}
            region=Rectangle.Intersect(region,SystemInformation.VirtualScreen);
            if(region.Width<32||region.Height<32) {Play(5,2600,0);Speak("这个框太小啦，重新拖一个大点的。",5000);return;}
            string path=SystemTools.NewRecordingPath();
            bool whole=region.Width>=SystemInformation.VirtualScreen.Width && region.Height>=SystemInformation.VirtualScreen.Height;
            Speak(whole?"马上倒数开录，桌宠和提示会先躲开；结束时点小条上的「停止」（可暂停）。"
                       :"准备录这块区域，桌宠和提示会先躲开；结束时点小条上的「停止」（可暂停）。",4000);
            // 先把提示气泡和桌宠退场，再开始抓帧，否则它们会被录进头几帧；3-2-1 倒数给用户准备
            HideBubble();
            SetVisibleForCapture(false);                          // 桌宠不入镜
            CountdownOverlay countdown=new CountdownOverlay(region,delegate {BeginCapture(region,path);});
            countdown.Show();countdown.Activate();
        }

        private void BeginCapture(Rectangle region,string path)
        {
            if(IsDisposed || recorder!=null) return;
            Play(7,0,0);                                          // 专注揉爪：正在录
            try {recorder=ScreenRecorder.Start(region,path,10);}
            catch(Exception error)
            {
                recorder=null;SetVisibleForCapture(true);Play(5,2600,0);
                Speak("录屏没开始："+error.Message,6000);
                return;
            }
            recordBar=new RecordBar(region,delegate {FinishRecording(path);},delegate(bool p){if(recorder!=null) recorder.Paused=p;});
            recordBar.Show();recordBar.Activate();
        }

        private void FinishRecording(string path)
        {
            if(recorder==null) return;
            int frames;
            try {frames=recorder.Frames;} catch {frames=0;}
            try {recorder.Stop();} catch { }
            recorder.Dispose();recorder=null;
            if(recordBar!=null) {recordBar.Dispose();recordBar=null;}
            SetVisibleForCapture(true);
            if(frames<=0)
            {
                try {if(File.Exists(path)) SystemOps.DeleteForever(path);} catch { }
                Play(5,2600,0);Speak("这次没录到画面，再试一次吧。",6000);
                return;
            }
            Play(3,2400,0);ShowEffect("sparkle");
            try {Clipboard.SetText(path);} catch { }
            Speak("录好啦！"+Path.GetFileName(path)+" 在桌面上（路径已复制），微信里可以直接发。",8000);
        }

        internal void PickColor()
        {
            Play(8,0,0);
            Speak("十字光标点哪里，我就取哪里的颜色。",4000);
            PickColorOverlay overlay=new PickColorOverlay(this);
            overlay.Show();overlay.Activate();
        }

        internal void ReportColor(Color color)
        {
            Play(3,2200,0);
            string hex=SystemTools.Hex(color);
            try {Clipboard.SetText(hex);} catch { }
            ShowEffect("sparkle");
            Speak("取到啦："+hex+"（已复制），RGB "+color.R+","+color.G+","+color.B,7000);
        }

        internal void ReportStatus()
        {
            Play(8,0,0);
            Speak(SystemTools.StatusReport(),9000);
        }

        internal void LockScreenNow()
        {
            Play(9,0,0);
            Speak("晚安，我先睡啦，回头见～",4000);
            System.Windows.Forms.Timer wait=new System.Windows.Forms.Timer {Interval=900};
            wait.Tick+=delegate {wait.Stop();wait.Dispose();SystemTools.LockScreen();};
            wait.Start();
        }

        internal void LoadPet(string id)
        {
            if(customImage!=null) {customImage.Dispose();customImage=null;}
            foreach(Bitmap[] list in cache.Values) foreach(Bitmap frame in list) frame.Dispose();cache.Clear();
            foreach(Bitmap pose in customPoses.Values) pose.Dispose();customPoses.Clear();
            if(id=="preset:chouchou") {LoadPreset("chouchou");customAtlas=false;data.Name="臭臭";data.PetId=id;menu.Items[0].Text=MenuTitle("臭臭");Play(0,0,0);return;}
            if(String.IsNullOrEmpty(id)) {LoadPreset("xiaohei");customAtlas=false;data.Name="小黑";data.PetId="";menu.Items[0].Text=MenuTitle("小黑");Play(0,0,0);return;}
            PetEntry entry=data.Pets.FirstOrDefault(p=>p.Id==id);
            if(entry==null || !File.Exists(store.PetPath(id))) {LoadPreset("xiaohei");customAtlas=false;data.PetId="";data.Name="小黑";menu.Items[0].Text=MenuTitle("小黑");Play(0,0,0);return;}
            using(Bitmap loaded=new Bitmap(store.PetPath(id))) customImage=new Bitmap(loaded);
            foreach(int row in entry.PoseRows)
            {
                string file=store.PetPosePath(id,row);
                if(!File.Exists(file)) continue;
                try {using(Bitmap pose=new Bitmap(file)) customPoses[row]=new Bitmap(pose);} catch { }
            }
            customAtlas=entry.Atlas;data.Name=entry.Name;data.PetId=id;menu.Items[0].Text=MenuTitle(entry.Name);Play(0,0,0);
        }
        private string MenuTitle(string name) {return companion?"萌栖 · "+name+"（陪伴）":"萌栖 · "+name;}
        private Bitmap[] CustomFrames(int row)
        {
            int key=scalePercent*10+row;
            Bitmap[] list;if(cache.TryGetValue(key,out list)) return list;
            list=new Bitmap[Durations[row].Length];
            Bitmap source=customPoses.ContainsKey(row)?customPoses[row]:customImage;   // 专属姿势优先，其余用本体图
            for(int frame=0;frame<list.Length;frame++) list[frame]=PetImages.Frame(source,row,frame,list.Length,Width,Height,customAtlas);
            cache.Add(key,list);return list;
        }
        internal void ImportPet(Bitmap image,string name,bool isAtlas)
        {
            ImportPet(image,name,isAtlas,null);
        }
        internal void ImportPet(Bitmap image,string name,bool isAtlas,Dictionary<int,Bitmap> poses)
        {
            if(String.IsNullOrWhiteSpace(name)) throw new ArgumentException("请给宠物取个名字。");
            if(isAtlas && (image.Width!=1536 || image.Height!=1872)) throw new ArgumentException("动作图集需为 1536 × 1872。");
            if(!isAtlas) PetImages.VisibleBounds(image);
            PetEntry entry=new PetEntry {Id=Guid.NewGuid().ToString("N"),Name=name.Trim(),Atlas=isAtlas};
            image.Save(store.PetPath(entry.Id),ImageFormat.Png);
            if(poses!=null) foreach(KeyValuePair<int,Bitmap> pose in poses)
            {
                if(pose.Value==null) continue;
                pose.Value.Save(store.PetPosePath(entry.Id,pose.Key),ImageFormat.Png);
                entry.PoseRows.Add(pose.Key);
            }
            data.Pets.Add(entry);LoadPet(entry.Id);SaveData();Speak("你好，我是"+entry.Name+"！",6000);
        }
        internal void ApplySettings()
        {
            ApplyScale(data.Scale);TopMost=data.Topmost;automatic=data.Automatic;
            automaticItem.Checked=automatic;topmostItem.Checked=TopMost;
            ConstrainToScreen();paintedFrame=-1;SaveData();
        }
        internal void SetStartup(bool enabled)
        {
            using(RegistryKey key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true))
            {
                if(enabled) key.SetValue("Mengqi","\""+Application.ExecutablePath+"\"");
                else key.DeleteValue("Mengqi",false);
            }
            data.Startup=enabled;
        }
        internal void ExportBackup(string destination)
        {
            AppData copy=DataStore.Json().Deserialize<AppData>(DataStore.Json().Serialize(data));copy.ProtectedKey="";
            using(FileStream file=new FileStream(destination,FileMode.Create)) using(ZipArchive zip=new ZipArchive(file,ZipArchiveMode.Create))
            {
                using(StreamWriter writer=new StreamWriter(zip.CreateEntry("data.json").Open(),System.Text.Encoding.UTF8)) writer.Write(DataStore.Json().Serialize(copy));
                foreach(PetEntry entry in data.Pets) if(File.Exists(store.PetPath(entry.Id))) using(Stream src=File.OpenRead(store.PetPath(entry.Id))) using(Stream dst=zip.CreateEntry("pets/"+entry.Id+".png").Open()) src.CopyTo(dst);
            }
        }
        internal void ImportBackup(string source)
        {
            using(FileStream file=File.OpenRead(source)) using(ZipArchive zip=new ZipArchive(file,ZipArchiveMode.Read))
            {
                ZipArchiveEntry json=zip.GetEntry("data.json");if(json==null || json.Length>4*1024*1024) throw new InvalidDataException("备份中的数据文件无效。");
                AppData loaded;using(StreamReader reader=new StreamReader(json.Open(),System.Text.Encoding.UTF8)) loaded=DataStore.Validate(DataStore.Json().Deserialize<AppData>(reader.ReadToEnd()));
                if(loaded.Pets.Count>100) throw new InvalidDataException("备份宠物数量过多。");
                // Validate every image before changing existing data.
                Dictionary<string,byte[]> images=new Dictionary<string,byte[]>();
                foreach(PetEntry pet in loaded.Pets)
                {
                    ZipArchiveEntry item=zip.GetEntry("pets/"+pet.Id+".png");if(item==null || item.Length>20*1024*1024) throw new InvalidDataException("备份宠物图片缺失或过大。");
                    using(MemoryStream memory=new MemoryStream()) {using(Stream stream=item.Open()) stream.CopyTo(memory);using(Bitmap check=new Bitmap(memory)) {if(check.Width>4096 || check.Height>4096) throw new InvalidDataException("备份图片过大。");if(pet.Atlas && (check.Width!=1536 || check.Height!=1872)) throw new InvalidDataException("备份动作图集尺寸错误。");PetImages.VisibleBounds(check);} images.Add(pet.Id,memory.ToArray());}
                }
                loaded.ProtectedKey=data.ProtectedKey;loaded.Startup=data.Startup;
                foreach(KeyValuePair<string,byte[]> item in images) File.WriteAllBytes(store.PetPath(item.Key),item.Value);
                data=loaded;LoadPet(data.PetId);ApplySettings();
            }
        }
        private void ShutdownFeatures()
        {
            if(shuttingDown) return;shuttingDown=true;
            if(recorder!=null) {try {recorder.Stop();} catch { }try {recorder.Dispose();} catch { }recorder=null;}
            if(recordBar!=null) {try {recordBar.Dispose();} catch { }recordBar=null;}
            if(data!=null && store!=null) try {SaveData();} catch { }
            if(IsHandleCreated) Native.UnregisterHotKey(Handle,712);
            if(bubble!=null) bubble.Dispose();if(hub!=null) hub.Dispose();if(reminderWindow!=null) reminderWindow.Dispose();
            if(voice!=null) {voice.SpeakAsyncCancelAll();voice.Dispose();}
            if(customImage!=null) customImage.Dispose();
            foreach(Bitmap pose in customPoses.Values) pose.Dispose();customPoses.Clear();
            Companions.Remove(this);
        }
    }

    /// <summary>桌宠头顶的聊天气泡：分层窗口按像素渲染——渐变玻璃卡片、柔和投影、指向桌宠的小尾巴。</summary>
    internal sealed class BubbleForm:Form
    {
        private Bitmap canvas;
        private Rectangle closeBox;
        private bool tailAtBottom=true;
        private readonly System.Windows.Forms.Timer hideTimer=new System.Windows.Forms.Timer();
        protected override bool ShowWithoutActivation {get {return true;}}
        protected override CreateParams CreateParams
        {
            get {CreateParams cp=base.CreateParams;cp.ExStyle|=0x00080000|0x00000080|0x08000000;return cp;}   // 分层 + 工具窗 + 不抢焦点
        }
        internal BubbleForm()
        {
            FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;
            AutoScaleMode=AutoScaleMode.None;   // 气泡按画布像素自行缩放，交给下面的 LayoutBubble 处理 DPI
            Font=new Font("Microsoft YaHei UI",10);
            BackColor=Color.FromArgb(250,247,255);
            hideTimer.Tick+=delegate {hideTimer.Stop();Hide();};
        }
        /// <summary>尾巴朝下（气泡在桌宠上方）还是朝上（气泡被顶到桌宠下面时）。</summary>
        internal void PointTail(bool atBottom)
        {
            if(tailAtBottom==atBottom) return;
            tailAtBottom=atBottom;
            if(canvas!=null) {LayoutBubble(lastText,lastMilliseconds);Repaint();}
        }
        private string lastText="";
        private int lastMilliseconds;
        internal void SetText(string value,int milliseconds)
        {
            lastText=value??"";lastMilliseconds=milliseconds;
            LayoutBubble(lastText,milliseconds);
            Repaint();
            hideTimer.Stop();hideTimer.Interval=Math.Max(1000,milliseconds);hideTimer.Start();
        }
        internal Bitmap Snapshot() {return canvas==null?null:new Bitmap(canvas);}
        private void LayoutBubble(string value,int milliseconds)
        {
            // 按系统 DPI 比例缩放整张卡片与字号，避免在其它电脑（高分屏）上气泡文字被裁切或显小。
            float s = this.DeviceDpi / 96f;
            if (s <= 0) s = 1;
            if (s > 4) s = 4;
            using(Font inkFont = new Font("Microsoft YaHei UI", 10f * s))
            using(Bitmap probe=new Bitmap(1,1)) using(Graphics probeG=Graphics.FromImage(probe))
            using(StringFormat format=(StringFormat)StringFormat.GenericDefault.Clone())
            {
                format.FormatFlags|=StringFormatFlags.LineLimit;      // 行高贴齐，避免最后一行被裁
                int margin=(int)Math.Round(20*s), tail=(int)Math.Round(14*s);
                int minCard=(int)Math.Round(200*s), maxCard=(int)Math.Round(340*s);
                int radius=(int)Math.Round(18*s);
                int textWidth=maxCard-(int)Math.Round(56*s);
                SizeF measured=probeG.MeasureString(value,inkFont,new SizeF(textWidth,10000),format);
                int cardWidth=Math.Max(minCard,Math.Min(maxCard,(int)Math.Ceiling(Math.Min(textWidth,measured.Width))+(int)Math.Round(56*s)));
                int cardHeight=Math.Max((int)Math.Round(64*s),(int)Math.Ceiling(measured.Height)+(int)Math.Round(34*s));
                // 直通 alpha 画布：预乘交给 Native.Premultiply 一次完成，这里不能先用 PArgb（会被二次预乘，边缘发暗）
                canvas=new Bitmap(cardWidth+margin*2,cardHeight+margin*2+tail,PixelFormat.Format32bppArgb);
                using(Graphics g=Graphics.FromImage(canvas))
                {
                    g.SmoothingMode=SmoothingMode.AntiAlias;
                    // 透明底上必须用灰度抗锯齿：ClearType 是为不透明背景设计的，会有彩色镶边
                    g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    Rectangle card=new Rectangle(margin,tailAtBottom?margin:margin+tail,cardWidth,cardHeight);
                    using(GraphicsPath body=RoundedPath(card,radius))
                    {
                        // 柔和投影：同形状往右下偏移，逐层加粗、alpha 递减
                        for(int i=5;i>=1;i--)
                            using(GraphicsPath halo=RoundedPath(new Rectangle(card.X+(int)Math.Round(2*s),card.Y+(int)Math.Round(3*s),card.Width,card.Height),radius))
                            {
                                halo.Widen(new Pen(Color.FromArgb(10,20,14,34),i*1.6f*s));
                                g.FillPath(new SolidBrush(Color.FromArgb(9,20,14,34)),halo);
                            }
                        using(GraphicsPath halo=RoundedPath(new Rectangle(card.X+(int)Math.Round(2*s),card.Y+(int)Math.Round(3*s),card.Width,card.Height),radius))
                            g.FillPath(new SolidBrush(Color.FromArgb(26,20,14,34)),halo);
                        // 玻璃卡片：上亮下柔的纵向渐变
                        using(LinearGradientBrush wash=new LinearGradientBrush(new Rectangle(card.X,card.Y-1,card.Width,card.Height+2),
                            Color.FromArgb(255,255,255,255),Color.FromArgb(255,243,238,252),LinearGradientMode.Vertical))
                            g.FillPath(wash,body);
                        using(Pen edge=new Pen(Color.FromArgb(210,186,168,222),1.6f*s)) g.DrawPath(edge,body);
                        using(Pen sheen=new Pen(Color.FromArgb(120,255,255,255),1.2f*s))
                            g.DrawLine(sheen,card.X+10*s,card.Y+1.6f*s,card.Right-10*s,card.Y+1.6f*s);
                    }
                    // 尾巴：小三角，盖住卡片边框的连接段，只描两条斜边
                    int tipX=card.Left+card.Width/2;
                    int attachY=tailAtBottom?card.Bottom-(int)Math.Round(2*s):card.Y+(int)Math.Round(2*s);
                    int tipY=tailAtBottom?card.Bottom+tail:card.Y-tail;
                    PointF[] tailShape={new PointF(tipX-(int)Math.Round(13*s),attachY),new PointF(tipX+(int)Math.Round(13*s),attachY),new PointF(tipX,tipY)};
                    using(SolidBrush fill=new SolidBrush(tailAtBottom?Color.FromArgb(255,243,238,252):Color.FromArgb(255,255,255,255))) g.FillPolygon(fill,tailShape);
                    using(Pen edge=new Pen(Color.FromArgb(210,186,168,222),1.6f*s))
                    {
                        g.DrawLine(edge,tipX-(int)Math.Round(13*s),attachY,tipX,tipY);
                        g.DrawLine(edge,tipX+(int)Math.Round(13*s),attachY,tipX,tipY);
                    }
                    // 正文：GDI+ 逐像素 alpha 渲染。不能用 TextRenderer（GDI 不理解 alpha，
                    // 在透明画布上字形边缘的 alpha 是坏的，预乘之后文字发虚发脏）
                    RectangleF textArea=new RectangleF(card.X+(int)Math.Round(20*s),card.Y+(int)Math.Round(13*s),card.Width-(int)Math.Round(56*s),card.Height-(int)Math.Round(22*s));
                    using(SolidBrush ink=new SolidBrush(Color.FromArgb(73,54,103))) g.DrawString(value,inkFont,ink,textArea,format);
                    // 右上角关闭点
                    closeBox=new Rectangle(card.Right-(int)Math.Round(22*s),card.Y+(int)Math.Round(7*s),(int)Math.Round(16*s),(int)Math.Round(16*s));
                    g.FillEllipse(new SolidBrush(Color.FromArgb(40,146,124,176)),closeBox);
                    using(Pen cross=new Pen(Color.FromArgb(150,110,90,140),1.6f*s))
                    {
                        g.DrawLine(cross,closeBox.X+(int)Math.Round(5*s),closeBox.Y+(int)Math.Round(5*s),closeBox.X+(int)Math.Round(11*s),closeBox.Y+(int)Math.Round(11*s));
                        g.DrawLine(cross,closeBox.X+(int)Math.Round(11*s),closeBox.Y+(int)Math.Round(5*s),closeBox.X+(int)Math.Round(5*s),closeBox.Y+(int)Math.Round(11*s));
                    }
                }
            }
            Size = canvas.Size;
        }
        private static GraphicsPath RoundedPath(Rectangle bounds,int radius)
        {
            GraphicsPath path=new GraphicsPath();
            int r=Math.Min(radius,Math.Min(bounds.Width,bounds.Height)/2);
            path.AddArc(bounds.X,bounds.Y,r,r,180,90);
            path.AddArc(bounds.Right-r,bounds.Y,r,r,270,90);
            path.AddArc(bounds.Right-r,bounds.Bottom-r,r,r,0,90);
            path.AddArc(bounds.X,bounds.Bottom-r,r,r,90,90);
            path.CloseFigure();
            return path;
        }
        private void Repaint()
        {
            if(canvas==null || !IsHandleCreated) return;
            using(Bitmap premultiplied=Native.Premultiply(canvas))
                Native.PaintLayer(Handle,premultiplied,Location,255);
        }
        protected override void OnLocationChanged(EventArgs e) {base.OnLocationChanged(e);Repaint();}
        protected override void OnShown(EventArgs e) {base.OnShown(e);Repaint();}
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if(closeBox.Contains(e.Location)) {hideTimer.Stop();Hide();}
        }
        protected override void OnPaint(PaintEventArgs e) {base.OnPaint(e);if(canvas!=null) e.Graphics.DrawImageUnscaled(canvas,0,0);}
        protected override void Dispose(bool disposing)
        {
            if(disposing) {hideTimer.Dispose();if(canvas!=null) {canvas.Dispose();canvas=null;}}
            base.Dispose(disposing);
        }
    }

    internal sealed class ReminderAlert:Form
    {
        internal ReminderAlert(PetWindow pet,Reminder reminder)
        {
            Text="萌栖 · "+reminder.Category+"提醒";Size=new Size(440,250);MinimumSize=Size;MaximumSize=Size;
            StartPosition=FormStartPosition.CenterScreen;TopMost=true;ShowInTaskbar=true;Font=Ui.Font;BackColor=Ui.Background;Icon=pet.Icon;
            AutoScaleMode = AutoScaleMode.Dpi;   // 按 DPI 比例缩放，高分屏下提醒文字完整显示
            Label title=new Label {Text=reminder.Title,Dock=DockStyle.Fill,Padding=new Padding(22),Font=new Font("Microsoft YaHei UI",14),ForeColor=Ui.Ink};Controls.Add(title);
            FlowLayoutPanel buttons=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=62,Padding=new Padding(16,8,8,8)};
            buttons.Controls.Add(Ui.Button("已完成",delegate {pet.CompleteReminder(reminder);Close();}));
            buttons.Controls.Add(Ui.Button("5分钟后",delegate {pet.Snooze(reminder,5);Close();}));
            buttons.Controls.Add(Ui.Button("10分钟后",delegate {pet.Snooze(reminder,10);Close();}));Controls.Add(buttons);
        }
    }
    internal static partial class Native
    {
        [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window,int id);
        [DllImport("user32.dll")] internal static extern int GetWindowLong(IntPtr window,int index);
        [DllImport("user32.dll")] internal static extern int SetWindowLong(IntPtr window,int index,int value);
    }
}
