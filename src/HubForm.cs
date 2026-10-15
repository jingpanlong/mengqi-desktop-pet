using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Mengqi
{
    internal static class Ui
    {
        internal static readonly Font Font=new Font("Microsoft YaHei UI",10);
        internal static readonly Color Background=Color.FromArgb(246,244,252), Ink=Color.FromArgb(48,39,73), Purple=Color.FromArgb(117,89,188), Muted=Color.FromArgb(113,108,131);
        internal static Button Button(string text,Action action)
        {
            Button button=new Button {Text=text,AutoSize=true,Height=36,MinimumSize=new Size(90,36),FlatStyle=FlatStyle.Flat,BackColor=Color.White,ForeColor=Purple,Margin=new Padding(0,0,10,8),Font=Font,Padding=new Padding(8,2,8,2)};
            button.FlatAppearance.BorderColor=Color.FromArgb(220,211,242);button.Click+=delegate {try {action();} catch(Exception error) {MessageBox.Show(error.Message,"萌栖",MessageBoxButtons.OK,MessageBoxIcon.Information);}};return button;
        }
        internal static Label Label(string text,int width,int height) {return new Label {Text=text,Width=width,Height=height,ForeColor=Ink,Font=Font,Margin=new Padding(0,4,0,8)};}
        internal static FlowLayoutPanel Stack() {return new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(20),BackColor=Background};}
        internal static FlowLayoutPanel Bar(int height) {return new FlowLayoutPanel {Dock=DockStyle.Top,Height=height,Padding=new Padding(0,6,0,0),BackColor=Background};}
        internal static DataGridView Grid()
        {
            DataGridView grid=new DataGridView {Dock=DockStyle.Fill,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,BackgroundColor=Color.White,BorderStyle=BorderStyle.None,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,Font=Font,RowTemplate={Height=36},EnableHeadersVisualStyles=false};
            grid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(234,227,249);grid.ColumnHeadersDefaultCellStyle.ForeColor=Ink;grid.ColumnHeadersHeight=40;grid.DefaultCellStyle.SelectionBackColor=Color.FromArgb(228,218,249);grid.DefaultCellStyle.SelectionForeColor=Ink;return grid;
        }
        internal static void Row(FlowLayoutPanel parent,string label,Control control)
        {
            Panel row=new Panel {Width=575,Height=60,Margin=new Padding(0,0,0,3)};
            row.Controls.Add(new Label {Text=label,Location=new Point(0,8),Width=180,Height=44,ForeColor=Ink});control.Location=new Point(185,5);control.Width=Math.Max(control.Width,380);row.Controls.Add(control);parent.Controls.Add(row);
        }
    }

    internal sealed partial class HubForm:Form
    {
        private readonly PetWindow pet;
        private readonly Panel host=new Panel {Dock=DockStyle.Fill,BackColor=Ui.Background};
        private readonly Dictionary<string,Panel> pages=new Dictionary<string,Panel>();
        private readonly Dictionary<string,Button> navigation=new Dictionary<string,Button>();
        private readonly TextBox history=new TextBox {Multiline=true,ScrollBars=ScrollBars.Vertical};
        private readonly TextBox input=new TextBox();
        private readonly Label chatStatus=new Label();
        private readonly DataGridView reminders=Ui.Grid();
        private readonly TextBox search=new TextBox();
        private readonly DataGridView tasks=Ui.Grid();
        private readonly Label focusClock=new Label(), stats=new Label(), petStats=new Label();
        private readonly ListBox pets=new ListBox();
        private readonly PictureBox petPreview=new PictureBox();
        private CancellationTokenSource chatCancel;
        private bool sending,loading;
        private TextBox notesBox;
        private int displayedChat=-1,displayedPets=-1;
        internal HubForm(PetWindow pet)
        {
            this.pet=pet;Text="萌栖 · 让陪伴住进桌面";Icon=pet.Icon;Size=new Size(1040,760);MinimumSize=new Size(960,680);StartPosition=FormStartPosition.CenterScreen;Font=Ui.Font;BackColor=Ui.Background;
            AutoScaleMode = AutoScaleMode.Dpi;   // 按系统 DPI 比例统一缩放控件与字体，避免高分屏下文字被裁切（显示不全）
            Panel header=new Panel {Dock=DockStyle.Top,Height=90,BackColor=Color.White};
            Bitmap brand;using(Stream source=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("mengqi.icon.png")) using(Bitmap loaded=new Bitmap(source)) brand=new Bitmap(loaded);
            PictureBox icon=new PictureBox {Image=brand,SizeMode=PictureBoxSizeMode.Zoom,Location=new Point(24,14),Size=new Size(60,60)};header.Controls.Add(icon);
            header.Controls.Add(new Label {Text="萌栖",Location=new Point(98,14),Size=new Size(180,36),Font=new Font("Microsoft YaHei UI",23,FontStyle.Bold),ForeColor=Ui.Ink});
            header.Controls.Add(new Label {Text="一只宠物，一点陪伴，一天好节奏",Location=new Point(100,55),Size=new Size(600,24),ForeColor=Ui.Muted});
            FlowLayoutPanel nav=new FlowLayoutPanel {Dock=DockStyle.Left,Width=162,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(16,22,12,0),BackColor=Color.FromArgb(239,234,249)};
            foreach(string[] entry in new[] {new[]{"chat","陪伴聊天"},new[]{"reminders","提醒与计划"},new[]{"focus","专注与待办"},new[]{"pets","我的宠物"},new[]{"tools","小工具"},new[]{"settings","设置与备份"},new[]{"about","关于萌栖"}})
            {
                string id=entry[0];Button button=Ui.Button(entry[1],delegate {SelectPage(id);});button.Width=130;button.Height=48;button.AutoSize=false;navigation.Add(id,button);nav.Controls.Add(button);
            }
            nav.Controls.Add(Ui.Label("Alt + Shift + M\n快速打开萌栖\n关闭窗口继续陪伴",132,75));
            Controls.Add(host);Controls.Add(nav);Controls.Add(header);
            BuildChat();BuildReminders();BuildFocus();BuildPets();BuildSettings();BuildTools();BuildAbout();
            pet.DataChanged+=Changed;
            FormClosing+=delegate(object sender,FormClosingEventArgs e) {if(e.CloseReason==CloseReason.UserClosing) {e.Cancel=true;Hide();}};
            SelectPage("chat");RefreshAll();RefreshTasks();
        }
        private Panel Page(string id,string title,string subtitle)
        {
            Panel page=new Panel {Dock=DockStyle.Fill,Padding=new Padding(20,12,20,18),BackColor=Ui.Background};
            Panel heading=new Panel {Dock=DockStyle.Top,Height=74};heading.Controls.Add(new Label {Text=title,Dock=DockStyle.Top,Height=34,Font=new Font("Microsoft YaHei UI",17,FontStyle.Bold),ForeColor=Ui.Ink});heading.Controls.Add(new Label {Text=subtitle,Location=new Point(0,40),Height=30,Width=760,ForeColor=Ui.Muted});
            Panel body=new Panel {Dock=DockStyle.Fill};page.Controls.Add(body);page.Controls.Add(heading);host.Controls.Add(page);pages.Add(id,page);return body;
        }
        internal void SelectPage(string id)
        {
            if(!pages.ContainsKey(id)) id="chat";
            foreach(KeyValuePair<string,Panel> page in pages) page.Value.Visible=page.Key==id;
            pages[id].BringToFront();
            foreach(KeyValuePair<string,Button> button in navigation) {button.Value.BackColor=button.Key==id?Ui.Purple:Color.White;button.Value.ForeColor=button.Key==id?Color.White:Ui.Purple;}
            RefreshAll();
        }
        private void BuildChat()
        {
            Panel body=Page("chat","陪伴聊天","和宠物聊聊天，也可以直接告诉它你想设置的提醒。");
            history.Dock=DockStyle.Fill;history.ReadOnly=true;history.BackColor=Color.White;history.BorderStyle=BorderStyle.None;history.Font=new Font("Microsoft YaHei UI",11);history.ForeColor=Ui.Ink;
            Panel footer=new Panel {Dock=DockStyle.Bottom,Height=138,Padding=new Padding(0,10,0,0)};
            input.Multiline=true;input.Dock=DockStyle.Fill;input.Font=Ui.Font;input.MaxLength=4000;
            FlowLayoutPanel controls=new FlowLayoutPanel {Dock=DockStyle.Right,Width=116,FlowDirection=FlowDirection.TopDown,WrapContents=false};
            Button send=Ui.Button("发送",delegate {Send();});controls.Controls.Add(send);controls.Controls.Add(Ui.Button("取消",delegate {if(chatCancel!=null) chatCancel.Cancel();}));
            Panel typing=new Panel {Dock=DockStyle.Fill};typing.Controls.Add(input);typing.Controls.Add(controls);
            chatStatus.Dock=DockStyle.Bottom;chatStatus.Height=30;chatStatus.ForeColor=Ui.Muted;footer.Controls.Add(typing);footer.Controls.Add(chatStatus);
            FlowLayoutPanel quick=Ui.Bar(48);quick.Controls.Add(Ui.Button("10分钟后休息",delegate {input.Text="10分钟后提醒我休息";Send();}));quick.Controls.Add(Ui.Button("喂食",delegate {pet.Interact("feed");}));quick.Controls.Add(Ui.Button("摸摸",delegate {pet.Interact("pet");}));quick.Controls.Add(Ui.Button("导出聊天",ExportChat));
            input.KeyDown+=delegate(object sender,KeyEventArgs e) {if(e.KeyCode==Keys.Enter && !e.Shift) {e.SuppressKeyPress=true;Send();}};
            body.Controls.Add(history);body.Controls.Add(footer);body.Controls.Add(quick);
        }
        private async void Send()
        {
            if(sending || String.IsNullOrWhiteSpace(input.Text)) return;
            string text=input.Text;input.Clear();sending=true;chatCancel=new CancellationTokenSource();chatStatus.Text="正在回复…";
            try {await pet.SendChat(text,chatCancel.Token);}
            catch(OperationCanceledException) {chatStatus.Text="已取消；刚才的消息保留在聊天记录中。";}
            catch(Exception error) {chatStatus.Text=error.Message;MessageBox.Show(error.Message,"萌栖 · 聊天");}
            finally {sending=false;chatCancel.Dispose();chatCancel=null;RefreshChat();}
        }
        private void ExportChat()
        {
            using(SaveFileDialog dialog=new SaveFileDialog {Filter="文本文件|*.txt",FileName="萌栖聊天记录.txt"}) if(dialog.ShowDialog()==DialogResult.OK) File.WriteAllLines(dialog.FileName,pet.data.Chat.Select(c=>c.Time.ToString("yyyy-MM-dd HH:mm")+" · "+(c.Role=="user"?"我":pet.PetName)+"\n"+c.Text+"\n").ToArray(),System.Text.Encoding.UTF8);
        }
        private void RefreshChat()
        {
            if(displayedChat==pet.data.Chat.Count) return;displayedChat=pet.data.Chat.Count;
            history.Clear();
            foreach(ChatEntry entry in pet.data.Chat)
            {
                history.AppendText((entry.Role=="user"?"我":pet.PetName)+"  "+entry.Time.ToString("HH:mm")+Environment.NewLine+entry.Text+Environment.NewLine+Environment.NewLine);
            }
            history.SelectionStart=history.TextLength;history.ScrollToCaret();
            if(!sending) chatStatus.Text=(pet.data.Ai?"联网 AI":"离线陪伴 · 规则对话")+"  |  Enter 发送，Shift + Enter 换行";
        }
        private void BuildReminders()
        {
            Panel body=Page("reminders","提醒与计划","工作、休息、喝水、吃药和自定义提醒；仅在萌栖运行时触发，重启后补发到期提醒。");
            reminders.ReadOnly=true;reminders.Columns.Add("title","提醒");reminders.Columns.Add("category","分类");reminders.Columns.Add("next","下次时间");reminders.Columns.Add("repeat","重复");reminders.Columns.Add("enabled","状态");
            FlowLayoutPanel bar=Ui.Bar(52);bar.Controls.Add(Ui.Button("新增提醒",delegate {EditReminder(null);}));bar.Controls.Add(Ui.Button("编辑",delegate {Reminder r=SelectedReminder();if(r!=null) EditReminder(r);}));bar.Controls.Add(Ui.Button("启用 / 暂停",delegate {Reminder r=SelectedReminder();if(r!=null) {r.Enabled=!r.Enabled;pet.SaveData();}}));bar.Controls.Add(Ui.Button("删除",delegate {Reminder r=SelectedReminder();if(r!=null && MessageBox.Show("删除这个提醒？", "萌栖",MessageBoxButtons.YesNo)==DialogResult.Yes) {pet.data.Reminders.Remove(r);pet.SaveData();}}));bar.Controls.Add(Ui.Button("历史记录",ShowLog));
            FlowLayoutPanel templates=Ui.Bar(52);
            foreach(string[] item in new[] {new[]{"每小时喝水","喝水","60"},new[]{"每45分钟休息","休息","45"},new[]{"每日工作提醒","工作","1440"},new[]{"吃药提醒","吃药","0"}})
            {
                string[] value=item;templates.Controls.Add(Ui.Button(value[0],delegate {if(value[2]=="0") {EditReminder(new Reminder {Title="吃药",Category="吃药",Repeat="每天"},true);return;}pet.AddReminder(new Reminder {Title=value[0],Category=value[1],Repeat=value[1]=="工作"?"每天":"间隔",Interval=int.Parse(value[2]),Next=DateTime.Now.AddMinutes(value[1]=="工作"?30:int.Parse(value[2]))});}));
            }
            search.Dock=DockStyle.Top;search.Height=28;search.TextChanged+=delegate {RefreshReminders();};
            Panel top=new Panel {Dock=DockStyle.Top,Height=138};top.Controls.Add(search);templates.Dock=DockStyle.Top;bar.Dock=DockStyle.Top;top.Controls.Add(templates);top.Controls.Add(bar);
            body.Controls.Add(reminders);body.Controls.Add(top);
        }
        private Reminder SelectedReminder() {return reminders.CurrentRow==null?null:reminders.CurrentRow.Tag as Reminder;}
        private void EditReminder(Reminder reminder) {EditReminder(reminder,false);}
        private void EditReminder(Reminder reminder,bool isNew)
        {
            bool fresh=reminder==null || isNew;Reminder copy=reminder==null?new Reminder():DataStore.Json().Deserialize<Reminder>(DataStore.Json().Serialize(reminder));
            using(ReminderEditor editor=new ReminderEditor(copy)) if(editor.ShowDialog(this)==DialogResult.OK)
            {
                if(fresh) pet.AddReminder(editor.Value);
                else {int index=pet.data.Reminders.IndexOf(reminder);pet.data.Reminders[index]=editor.Value;pet.SaveData();}
            }
        }
        private void RefreshReminders()
        {
            string selected=SelectedReminder()==null?null:SelectedReminder().Id;
            reminders.Rows.Clear();
            foreach(Reminder r in pet.data.Reminders.OrderBy(r=>r.Next))
            {
                if(!String.IsNullOrWhiteSpace(search.Text) && !(r.Title+r.Category).Contains(search.Text)) continue;
                int i=reminders.Rows.Add(r.Title,r.Category,r.Next.ToString("MM-dd HH:mm"),r.Repeat=="间隔"?"每"+r.Interval+"分钟":r.Repeat,r.Enabled?"启用":"暂停 / 已提醒");reminders.Rows[i].Tag=r;
                if(r.Id==selected) reminders.Rows[i].Selected=true;
            }
        }
        private void ShowLog()
        {
            using(Form form=new Form {Text="萌栖 · 提醒记录",Size=new Size(720,520),Font=Ui.Font,StartPosition=FormStartPosition.CenterParent,AutoScaleMode=AutoScaleMode.Dpi})
            {
                TextBox log=new TextBox {Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Text=String.Join(Environment.NewLine,pet.data.Log.AsEnumerable().Reverse())};form.Controls.Add(log);form.ShowDialog(this);
            }
        }
        private void BuildFocus()
        {
            Panel body=Page("focus","专注与待办","给时间一个小目标，工作和休息都有人陪伴。");
            FlowLayoutPanel area=Ui.Stack();body.Controls.Add(area);
            focusClock.Font=new Font("Microsoft YaHei UI",32,FontStyle.Bold);focusClock.Size=new Size(650,70);focusClock.ForeColor=Ui.Purple;area.Controls.Add(focusClock);
            FlowLayoutPanel times=new FlowLayoutPanel {Size=new Size(680,44)};
            NumericUpDown work=new NumericUpDown {Minimum=1,Maximum=180,Value=Math.Max(1,Math.Min(180,pet.data.WorkMinutes)),Width=64};
            NumericUpDown rest=new NumericUpDown {Minimum=1,Maximum=60,Value=Math.Max(1,Math.Min(60,pet.data.BreakMinutes)),Width=64};
            times.Controls.Add(Ui.Label("专注（分钟）",105,30));times.Controls.Add(work);times.Controls.Add(Ui.Label("休息（分钟）",105,30));times.Controls.Add(rest);area.Controls.Add(times);
            FlowLayoutPanel buttons=new FlowLayoutPanel {Size=new Size(680,48)};
            buttons.Controls.Add(Ui.Button("开始专注",delegate {pet.data.WorkMinutes=(int)work.Value;pet.data.BreakMinutes=(int)rest.Value;pet.StartFocus(false);}));buttons.Controls.Add(Ui.Button("开始休息",delegate {pet.data.BreakMinutes=(int)rest.Value;pet.StartFocus(true);}));buttons.Controls.Add(Ui.Button("暂停 / 继续",pet.PauseFocus));buttons.Controls.Add(Ui.Button("结束本轮",pet.StopFocus));area.Controls.Add(buttons);
            stats.Size=new Size(680,34);stats.ForeColor=Ui.Muted;area.Controls.Add(stats);
            area.Controls.Add(Ui.Label("今日待办",650,30));
            tasks.Dock=DockStyle.None;tasks.Size=new Size(680,160);tasks.Columns.Add(new DataGridViewCheckBoxColumn {Name="done",HeaderText="完成",FillWeight=18});tasks.Columns.Add("text","任务");tasks.Columns[1].ReadOnly=true;
            tasks.CurrentCellDirtyStateChanged+=delegate {if(tasks.IsCurrentCellDirty) tasks.CommitEdit(DataGridViewDataErrorContexts.Commit);};
            tasks.CellValueChanged+=delegate(object sender,DataGridViewCellEventArgs e) {if(loading || e.RowIndex<0 || e.ColumnIndex!=0) return;TaskItem task=tasks.Rows[e.RowIndex].Tag as TaskItem;if(task!=null) {task.Done=Convert.ToBoolean(tasks.Rows[e.RowIndex].Cells[0].Value);pet.SaveData();}};area.Controls.Add(tasks);
            FlowLayoutPanel taskBar=new FlowLayoutPanel {Size=new Size(680,46)};TextBox taskText=new TextBox {Width=380,MaxLength=200};taskBar.Controls.Add(taskText);taskBar.Controls.Add(Ui.Button("添加",delegate {if(!String.IsNullOrWhiteSpace(taskText.Text)) {pet.data.Tasks.Add(new TaskItem {Text=taskText.Text.Trim()});taskText.Clear();pet.SaveData();RefreshTasks();}}));taskBar.Controls.Add(Ui.Button("移除选中",delegate {if(tasks.CurrentRow!=null) {pet.data.Tasks.Remove((TaskItem)tasks.CurrentRow.Tag);pet.SaveData();RefreshTasks();}}));area.Controls.Add(taskBar);
            area.Controls.Add(Ui.Label("随手记",650,28));TextBox notes=new TextBox {Multiline=true,Width=680,Height=110,ScrollBars=ScrollBars.Vertical,Text=pet.data.Notes};notesBox=notes;notes.TextChanged+=delegate {pet.data.Notes=notes.Text;};notes.Leave+=delegate {pet.SaveData();};area.Controls.Add(notes);
        }
        private void RefreshTasks()
        {
            loading=true;tasks.Rows.Clear();foreach(TaskItem task in pet.data.Tasks) {int row=tasks.Rows.Add(task.Done,task.Text);tasks.Rows[row].Tag=task;}loading=false;
        }
        private void BuildPets()
        {
            Panel body=Page("pets","我的宠物","内置小黑与臭臭两套15组动作；上传照片制作动效桌宠，或导入完整的9行动作图集。");
            Panel left=new Panel {Dock=DockStyle.Left,Width=230,Padding=new Padding(0,0,16,0)};
            pets.Dock=DockStyle.Fill;pets.Font=Ui.Font;pets.ItemHeight=32;left.Controls.Add(pets);
            FlowLayoutPanel buttons=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=106};buttons.Controls.Add(Ui.Button("上传宠物",ImportPhoto));buttons.Controls.Add(Ui.Button("切换宠物",delegate {PetEntry entry=pets.SelectedItem as PetEntry;pet.LoadPet(entry==null?"":entry.Id);pet.SaveData();RefreshPetPreview();}));buttons.Controls.Add(Ui.Button("移除",RemovePet));left.Controls.Add(buttons);
            Panel right=new Panel {Dock=DockStyle.Fill,Padding=new Padding(22,0,0,0)};
            petPreview.SizeMode=PictureBoxSizeMode.Zoom;petPreview.Dock=DockStyle.Top;petPreview.Height=280;petPreview.BackColor=Color.White;right.Controls.Add(petPreview);
            FlowLayoutPanel actions=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=150};foreach(string[] item in new[] {new[]{"摸摸","pet"},new[]{"喂食","feed"},new[]{"玩球","play"},new[]{"睡觉 / 唤醒","sleep"}}) {string action=item[1];actions.Controls.Add(Ui.Button(item[0],delegate {pet.Interact(action);}));}
            ComboBox actionChoice=new ComboBox {Width=330,DropDownStyle=ComboBoxStyle.DropDownList};actionChoice.Items.AddRange(new object[]{"待机","右跑","左跑","挥爪","跳跃","失落","等待","专注","观察","睡觉","伸懒腰","舔毛","打哈欠","吃饭","玩球"});actionChoice.SelectedIndex=0;actions.Controls.Add(actionChoice);actions.Controls.Add(Ui.Button("播放动作",delegate {pet.PreviewAction(actionChoice.SelectedIndex);}));
            petStats.Size=new Size(490,42);petStats.ForeColor=Ui.Muted;actions.Controls.Add(petStats);right.Controls.Add(actions);
            body.Controls.Add(right);body.Controls.Add(left);
            pets.DisplayMember="Name";pets.SelectedIndexChanged+=delegate {RefreshPetPreview();};
        }
        private void ImportPhoto() {using(PhotoEditor editor=new PhotoEditor(pet)) editor.ShowDialog(this);RefreshPets();RefreshPetPreview();}
        private void RemovePet()
        {
            PetEntry entry=pets.SelectedItem as PetEntry;if(entry==null || entry.Id=="preset:chouchou") return;
            if(MessageBox.Show("移除这个已导入的宠物？原始照片不会被修改。","萌栖",MessageBoxButtons.YesNo)!=DialogResult.Yes) return;
            if(pet.data.PetId==entry.Id) pet.LoadPet("");pet.data.Pets.Remove(entry);File.Delete(pet.store.PetPath(entry.Id));pet.SaveData();RefreshPets();
        }
        private static readonly PetEntry PresetChouchou=new PetEntry {Id="preset:chouchou",Name="臭臭 · 内置",Atlas=true};
        private void RefreshPets()
        {
            if(displayedPets==pet.data.Pets.Count) return;displayedPets=pet.data.Pets.Count;
            pets.Items.Clear();pets.Items.Add("小黑 · 内置");pets.Items.Add(PresetChouchou);foreach(PetEntry entry in pet.data.Pets) pets.Items.Add(entry);pets.SelectedIndex=0;
        }
        private void RefreshPetPreview()
        {
            if(petPreview.Image!=null) {petPreview.Image.Dispose();petPreview.Image=null;}
            PetEntry entry=pets.SelectedItem as PetEntry;
            if(entry==null) {using(Stream stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("xiaohei.atlas.png")) using(Bitmap atlas=new Bitmap(stream)) petPreview.Image=atlas.Clone(new Rectangle(0,0,192,208),PixelFormat.Format32bppArgb);}
            else if(entry.Id=="preset:chouchou") {using(Stream stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("chouchou.atlas.png")) using(Bitmap atlas=new Bitmap(stream)) petPreview.Image=atlas.Clone(new Rectangle(0,0,192,208),PixelFormat.Format32bppArgb);}
            else using(Bitmap image=new Bitmap(pet.store.PetPath(entry.Id))) petPreview.Image=entry.Atlas?image.Clone(new Rectangle(0,0,192,208),PixelFormat.Format32bppArgb):(Bitmap)image.Clone();
        }
        private void BuildSettings()
        {
            Panel body=Page("settings","设置与备份","默认离线陪伴；联网功能只在开启后连接你填写的服务。");FlowLayoutPanel area=Ui.Stack();body.Controls.Add(area);
            TextBox user=new TextBox {Text=pet.data.UserName},chatUrl=new TextBox {Text=pet.data.ChatEndpoint},model=new TextBox {Text=pet.data.Model},key=new TextBox {Text=DataStore.Unprotect(pet.data.ProtectedKey),UseSystemPasswordChar=true},imageUrl=new TextBox {Text=pet.data.ImageEndpoint},imageModel=new TextBox {Text=pet.data.ImageModel};
            Ui.Row(area,"我希望被称呼为",user);
            CheckBox ai=new CheckBox {Text="启用联网 AI 聊天",Checked=pet.data.Ai,AutoSize=true};area.Controls.Add(ai);
            Ui.Row(area,"聊天接口完整地址",chatUrl);Ui.Row(area,"聊天模型名称",model);Ui.Row(area,"API Key（本机加密）",key);Ui.Row(area,"图片编辑接口地址",imageUrl);Ui.Row(area,"图片模型名称",imageModel);
            area.Controls.Add(Ui.Label("接口采用 Chat Completions / Images Edits 格式。开启联网后，对话或所选照片会发送到填写的服务。",680,48));
            FlowLayoutPanel switches=new FlowLayoutPanel {Size=new Size(680,78)};
            CheckBox bubbles=new CheckBox {Text="气泡聊天",Checked=pet.data.Bubbles,Width=135},sound=new CheckBox {Text="提醒声音",Checked=pet.data.Sound,Width=135},speech=new CheckBox {Text="朗读气泡",Checked=pet.data.Speech,Width=135},startup=new CheckBox {Text="开机启动",Checked=pet.data.Startup,Width=135},quiet=new CheckBox {Text="勿扰时间",Checked=pet.data.Quiet,Width=135};
            switches.Controls.AddRange(new Control[]{bubbles,sound,speech,startup,quiet});area.Controls.Add(switches);
            NumericUpDown quietStart=new NumericUpDown {Minimum=0,Maximum=23,Value=pet.data.QuietStart,Width=64},quietEnd=new NumericUpDown {Minimum=0,Maximum=23,Value=pet.data.QuietEnd,Width=64},opacity=new NumericUpDown {Minimum=35,Maximum=100,Value=pet.data.Opacity,Width=64};
            FlowLayoutPanel quietBar=new FlowLayoutPanel {Size=new Size(680,42)};quietBar.Controls.Add(Ui.Label("勿扰：",70,28));quietBar.Controls.Add(quietStart);quietBar.Controls.Add(Ui.Label("时 至",54,28));quietBar.Controls.Add(quietEnd);quietBar.Controls.Add(Ui.Label("时　透明度：",130,28));quietBar.Controls.Add(opacity);quietBar.Controls.Add(Ui.Label("%",24,28));area.Controls.Add(quietBar);
            area.Controls.Add(Ui.Button("保存设置",delegate {
                if(ai.Checked) {ChatService.Endpoint(chatUrl.Text.Trim());if(String.IsNullOrWhiteSpace(model.Text)) throw new ArgumentException("请填写聊天模型名称。");}
                if(!String.IsNullOrWhiteSpace(imageUrl.Text)) ChatService.Endpoint(imageUrl.Text.Trim());
                if(startup.Checked!=pet.data.Startup) pet.SetStartup(startup.Checked);
                pet.data.UserName=user.Text.Trim();pet.data.ChatEndpoint=chatUrl.Text.Trim();pet.data.Model=model.Text.Trim();pet.data.ProtectedKey=DataStore.Protect(key.Text.Trim());pet.data.ImageEndpoint=imageUrl.Text.Trim();pet.data.ImageModel=imageModel.Text.Trim();pet.data.Ai=ai.Checked;
                pet.data.Bubbles=bubbles.Checked;pet.data.Sound=sound.Checked;pet.data.Speech=speech.Checked;pet.data.Quiet=quiet.Checked;pet.data.QuietStart=(int)quietStart.Value;pet.data.QuietEnd=(int)quietEnd.Value;pet.data.Opacity=(int)opacity.Value;pet.ApplySettings();MessageBox.Show("设置已保存。","萌栖");
            }));
            FlowLayoutPanel backups=new FlowLayoutPanel {Size=new Size(680,52)};
            backups.Controls.Add(Ui.Button("导出备份",delegate {using(SaveFileDialog dialog=new SaveFileDialog {Filter="萌栖备份|*.zip",FileName="萌栖备份.zip"}) if(dialog.ShowDialog()==DialogResult.OK) pet.ExportBackup(dialog.FileName);}));
            backups.Controls.Add(Ui.Button("恢复备份",delegate {using(OpenFileDialog dialog=new OpenFileDialog {Filter="萌栖备份|*.zip"}) if(dialog.ShowDialog()==DialogResult.OK && MessageBox.Show("恢复会替换当前提醒、聊天和待办。继续吗？","萌栖",MessageBoxButtons.YesNo)==DialogResult.Yes) {pet.ImportBackup(dialog.FileName);displayedChat=-1;displayedPets=-1;RefreshAll();RefreshTasks();MessageBox.Show("已恢复。关闭并重新打开萌栖可刷新所有设置项。","萌栖");}}));
            backups.Controls.Add(Ui.Button("打开数据目录",delegate {System.Diagnostics.Process.Start("explorer.exe",pet.store.Root);}));area.Controls.Add(backups);
            area.Controls.Add(Ui.Label("备份不包含 API Key。提醒、聊天、待办和自定义宠物保存在本机数据目录。",680,48));
        }
        private void Changed(object sender,EventArgs e) {if(IsDisposed) return;if(notesBox!=null && !notesBox.Focused && notesBox.Text!=pet.data.Notes) notesBox.Text=pet.data.Notes;RefreshAll();}
        private void RefreshAll() {RefreshChat();RefreshReminders();RefreshPets();UpdateLive();}
        internal void UpdateLive()
        {
            focusClock.Text=pet.FocusStatus();stats.Text="累计完成 "+pet.data.Sessions+" 轮专注 · "+(pet.data.FocusSeconds/60)+" 分钟";
            petStats.Text="当前："+pet.PetName+"   心情 "+pet.data.Happiness+"   饱腹 "+pet.data.Hunger+"   精力 "+pet.data.Energy;
        }
        internal void CapturePages(string directory)
        {
            FeatureTests.PrepareCapture(this);RefreshTasks();foreach(string page in pages.Keys) {SelectPage(page);PerformLayout();Application.DoEvents();using(Bitmap image=new Bitmap(Width,Height)) {DrawToBitmap(image,new Rectangle(0,0,Width,Height));image.Save(Path.Combine(directory,"界面-"+page+".png"),ImageFormat.Png);}}Hide();
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing) {pet.DataChanged-=Changed;if(chatCancel!=null) chatCancel.Cancel();if(petPreview.Image!=null) petPreview.Image.Dispose();}
            base.Dispose(disposing);
        }
    }

    internal sealed class ReminderEditor:Form
    {
        internal Reminder Value;
        internal ReminderEditor(Reminder reminder)
        {
            Value=reminder;Text="萌栖 · 设置提醒";Size=new Size(650,540);MinimumSize=Size;MaximumSize=Size;Font=Ui.Font;StartPosition=FormStartPosition.CenterParent;BackColor=Ui.Background;
            AutoScaleMode = AutoScaleMode.Dpi;   // 按 DPI 比例缩放，MinimumSize/MaximumSize 也会被一同缩放，不会卡在旧尺寸导致文字裁切
            FlowLayoutPanel area=Ui.Stack();Controls.Add(area);
            TextBox title=new TextBox {Text=reminder.Title,MaxLength=200};ComboBox category=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList};category.Items.AddRange(new object[]{"工作","休息","喝水","吃药","起身","眼睛休息","自定义"});category.SelectedItem=reminder.Category;if(category.SelectedIndex<0) category.SelectedIndex=6;
            DateTimePicker next=new DateTimePicker {Format=DateTimePickerFormat.Custom,CustomFormat="yyyy-MM-dd HH:mm",Value=reminder.Next};ComboBox repeat=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList};repeat.Items.AddRange(new object[]{"一次","每天","工作日","每周","间隔"});repeat.SelectedItem=reminder.Repeat;if(repeat.SelectedIndex<0) repeat.SelectedIndex=0;
            NumericUpDown interval=new NumericUpDown {Minimum=1,Maximum=10080,Value=Math.Max(1,Math.Min(10080,reminder.Interval))};CheckBox enabled=new CheckBox {Text="启用这个提醒",Checked=reminder.Enabled,AutoSize=true};
            Ui.Row(area,"提醒内容",title);Ui.Row(area,"分类",category);Ui.Row(area,"下次提醒",next);Ui.Row(area,"重复方式",repeat);Ui.Row(area,"间隔（分钟）",interval);area.Controls.Add(enabled);
            area.Controls.Add(Ui.Label("吃药提醒的时间和内容由你设置。勿扰时段内的提醒会等到勿扰结束后补发。",590,50));
            area.Controls.Add(Ui.Button("保存提醒",delegate {if(String.IsNullOrWhiteSpace(title.Text)) throw new ArgumentException("请输入提醒内容。");Value.Title=title.Text.Trim();Value.Category=(string)category.SelectedItem;Value.Next=next.Value;Value.Repeat=(string)repeat.SelectedItem;Value.Interval=(int)interval.Value;Value.Enabled=enabled.Checked;DialogResult=DialogResult.OK;Close();}));
        }
    }
}
