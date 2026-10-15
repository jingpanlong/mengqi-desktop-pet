using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Mengqi
{
    internal sealed class PhotoEditor:Form
    {
        private readonly PetWindow pet;
        private readonly PictureBox preview=new PictureBox();
        private readonly TextBox name=new TextBox {Text="我的宠物",MaxLength=40};
        private readonly NumericUpDown tolerance=new NumericUpDown {Minimum=5,Maximum=150,Value=35,Width=70};
        private readonly CheckBox atlas=new CheckBox {Text="导入 8 × 9 动作图集（1536 × 1872 PNG）",AutoSize=true};
        private readonly Label status=new Label();
        private Bitmap original,current;
        private Dictionary<int,Bitmap> generatedPoses;      // AI 生成的动作姿势，随当前图片一起导入
        private CancellationTokenSource cancel;
        private bool generating,erasing;
        internal PhotoEditor(PetWindow pet)
        {
            this.pet=pet;Text="萌栖 · 创建自己的桌宠";Size=new Size(840,720);MinimumSize=new Size(840,720);Icon=pet.Icon;Font=Ui.Font;BackColor=Ui.Background;StartPosition=FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;   // 按 DPI 比例缩放控件与字体，避免高分屏下上传界面文字显示不全
            FlowLayoutPanel top=Ui.Bar(55);top.Padding=new Padding(18,10,8,0);
            top.Controls.Add(Ui.Button("选择照片 / PNG",Choose));top.Controls.Add(Ui.Label("宠物名字",90,30));top.Controls.Add(name);name.Width=170;top.Controls.Add(atlas);
            FlowLayoutPanel tools=Ui.Bar(52);tools.Padding=new Padding(14,5,0,0);tools.Controls.Add(Ui.Button("去除边缘背景",Remove));tools.Controls.Add(Ui.Label("容差",48,28));tools.Controls.Add(tolerance);tools.Controls.Add(Ui.Button("恢复原图",Restore));
            tools.Controls.Add(Ui.Button("联网 AI 抠图",MattingAI));tools.Controls.Add(Ui.Button("联网 AI 绘制",Generate));tools.Controls.Add(Ui.Button("AI 生成动作",GenerateActions));tools.Controls.Add(Ui.Button("取消生成",delegate {if(cancel!=null) cancel.Cancel();}));
            Panel footer=new Panel {Dock=DockStyle.Bottom,Height=126,Padding=new Padding(18,8,18,8)};
            FlowLayoutPanel buttons=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=46};buttons.Controls.Add(Ui.Button("创建并使用",Import));buttons.Controls.Add(Ui.Button("导出透明 PNG",Export));
            status.Dock=DockStyle.Fill;status.ForeColor=Ui.Muted;status.Text="离线：去背景后导入，软件自带呼吸、跳跃、嚼食等 15 组动作变换。\n联网：AI 抠图（精细去背景）、AI 绘制（重绘形象）、AI 生成动作（按当前形象画睡觉 / 挥爪 / 跳跃 / 吃饭姿势，创建后替换对应动作）。需先配置图片编辑接口。";
            footer.Controls.Add(status);footer.Controls.Add(buttons);
            preview.Dock=DockStyle.Fill;preview.SizeMode=PictureBoxSizeMode.Zoom;preview.BackColor=Color.FromArgb(230,226,239);
            preview.Paint+=PaintChecker;preview.MouseDown+=delegate(object sender,MouseEventArgs e) {erasing=true;Erase(e);};preview.MouseMove+=delegate(object sender,MouseEventArgs e) {if(erasing) Erase(e);};preview.MouseUp+=delegate {erasing=false;};
            Controls.Add(preview);Controls.Add(footer);Controls.Add(tools);Controls.Add(top);
            FormClosing+=delegate(object sender,FormClosingEventArgs e) {if(generating) {e.Cancel=true;if(cancel!=null) cancel.Cancel();status.Text="正在取消，请稍候再关闭。";}};
        }
        private void PaintChecker(object sender,PaintEventArgs e)
        {
            // PictureBox paints its image before this event; only draw checker when empty.
            if(current!=null) return;
            for(int y=0;y<preview.Height;y+=24) for(int x=0;x<preview.Width;x+=24) if((x/24+y/24)%2==0) e.Graphics.FillRectangle(Brushes.WhiteSmoke,x,y,24,24);
            TextRenderer.DrawText(e.Graphics,"上传宠物照片，或已经去好背景的透明 PNG",Font,preview.ClientRectangle,Ui.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        }
        private void Choose()
        {
            if(generating) return;
            using(OpenFileDialog dialog=new OpenFileDialog {Filter="宠物图片|*.png;*.jpg;*.jpeg;*.bmp|所有文件|*.*"})
            {
                if(dialog.ShowDialog(this)!=DialogResult.OK) return;
                if(new FileInfo(dialog.FileName).Length>30*1024*1024) throw new ArgumentException("图片请小于 30 MB。");
                using(Bitmap loaded=new Bitmap(dialog.FileName))
                {
                    if(loaded.Width>10000 || loaded.Height>10000) throw new ArgumentException("图片边长请小于 10000 像素。");
                    bool sheet=loaded.Width==1536 && loaded.Height==1872;
                    if(original!=null) original.Dispose();original=sheet?new Bitmap(loaded):PetImages.Resize(loaded,512);atlas.Checked=sheet;SetImage(new Bitmap(original));
                }
                status.Text="已载入。普通照片可去背景或用鼠标擦除，再创建桌宠。图集导入保留原始九种动作。";
            }
        }
        private void SetImage(Bitmap image)
        {
            preview.Image=null;if(current!=null) current.Dispose();current=image;preview.Image=current;preview.Invalidate();
            ClearPoses();                                        // 换了图片，旧姿势就不再匹配
        }
        private void ClearPoses()
        {
            if(generatedPoses==null) return;
            foreach(Bitmap pose in generatedPoses.Values) pose.Dispose();
            generatedPoses=null;
        }
        private void RequireImage() {if(current==null) throw new ArgumentException("请先选择一张宠物图片。");if(generating) throw new ArgumentException("请等待生成完成或先取消。");}
        private void Remove() {RequireImage();if(atlas.Checked) throw new ArgumentException("动作图集请使用已透明的 PNG。");SetImage(PetImages.RemoveBackground(current,(int)tolerance.Value));status.Text="已去除与四角相近、连通到边缘的背景。复杂背景可继续用鼠标擦除；可恢复原图。";}
        private void Restore() {if(generating || original==null) return;SetImage(new Bitmap(original));}
        private void Erase(MouseEventArgs e)
        {
            if(current==null || generating || atlas.Checked || (e.Button!=MouseButtons.Left && e.Button!=MouseButtons.Right)) return;
            float scale=Math.Min(preview.ClientSize.Width/(float)current.Width,preview.ClientSize.Height/(float)current.Height);
            float x=(e.X-(preview.Width-current.Width*scale)/2)/scale,y=(e.Y-(preview.Height-current.Height*scale)/2)/scale;
            if(x<0 || y<0 || x>=current.Width || y>=current.Height) return;
            float radius=(e.Button==MouseButtons.Right?28:12)/scale;
            using(Graphics g=Graphics.FromImage(current)) {g.CompositingMode=System.Drawing.Drawing2D.CompositingMode.SourceCopy;using(Brush brush=new SolidBrush(Color.Transparent)) g.FillEllipse(brush,x-radius,y-radius,radius*2,radius*2);}preview.Invalidate();
        }
        private async void Generate()
        {
            try
            {
                RequireImage();if(atlas.Checked) throw new ArgumentException("请选择一张普通宠物照片进行 AI 绘制。");
                ChatService.Endpoint(pet.data.ImageEndpoint);if(String.IsNullOrWhiteSpace(pet.data.ImageModel)) throw new ArgumentException("请先在设置中填写图片模型名称。");
                if(MessageBox.Show("将当前图片上传到你配置的图片接口，服务可能计费。继续生成吗？","萌栖 · 联网绘制",MessageBoxButtons.YesNo)!=DialogResult.Yes) return;
                generating=true;cancel=new CancellationTokenSource();status.Text="正在联网绘制，可能需要几分钟…";
                Bitmap result=await ChatService.GeneratePet(pet.data,current,name.Text,cancel.Token);
                if(IsDisposed) {result.Dispose();return;}SetImage(result);status.Text="AI 图片已返回。可继续去背景、导出 PNG 或创建桌宠。";
            }
            catch(OperationCanceledException) {status.Text="已取消生成，原图片保留。";}
            catch(Exception error) {status.Text=error.Message;MessageBox.Show(error.Message,"萌栖 · 图片生成");}
            finally {generating=false;if(cancel!=null) {cancel.Dispose();cancel=null;}}
        }
        private const string MattingPrompt="Remove the background of the uploaded image completely. Keep the animal itself exactly unchanged - same pose, colors, proportions and art style. Preserve fine fur and edge detail. Output the same character on a fully transparent background, centered, no shadow, no floor, no text, no border.";
        private async void MattingAI()
        {
            try
            {
                RequireImage();if(atlas.Checked) throw new ArgumentException("动作图集本身已是透明 PNG，无需抠图。");
                ChatService.Endpoint(pet.data.ImageEndpoint);if(String.IsNullOrWhiteSpace(pet.data.ImageModel)) throw new ArgumentException("请先在设置中填写图片模型名称。");
                if(MessageBox.Show("将当前图片上传到你配置的图片接口进行抠图，服务可能计费。继续吗？","萌栖 · AI 抠图",MessageBoxButtons.YesNo)!=DialogResult.Yes) return;
                generating=true;cancel=new CancellationTokenSource();status.Text="正在联网抠图，可能需要几分钟…";
                Bitmap result=await ChatService.GeneratePet(pet.data,current,name.Text,MattingPrompt,cancel.Token);
                if(IsDisposed) {result.Dispose();return;}SetImage(result);status.Text="AI 抠图完成。若边缘仍有残留，可以用鼠标细擦；不满意可恢复原图重试。";
            }
            catch(OperationCanceledException) {status.Text="已取消抠图，原图片保留。";}
            catch(Exception error) {status.Text=error.Message;MessageBox.Show(error.Message,"萌栖 · AI 抠图");}
            finally {generating=false;if(cancel!=null) {cancel.Dispose();cancel=null;}}
        }
        private sealed class PoseSpec { internal int Row; internal string Label,Prompt; }
        private static readonly PoseSpec[] PoseList=new PoseSpec[] {
            new PoseSpec {Row=9,Label="睡觉",Prompt="The same pet character from the uploaded image, curled up sleeping peacefully with eyes closed, side view, same art style, same colors and markings, full body visible, transparent background, no floor, shadow or text."},
            new PoseSpec {Row=3,Label="挥爪",Prompt="The same pet character from the uploaded image, sitting and happily waving one front paw, eyes open, cheerful expression, same art style and markings, full body visible, transparent background, no floor, shadow or text."},
            new PoseSpec {Row=4,Label="跳跃",Prompt="The same pet character from the uploaded image, stretching in a lively mid-air jump with all paws off the ground, same art style and markings, full body visible, transparent background, no floor, shadow or text."},
            new PoseSpec {Row=13,Label="吃饭",Prompt="The same pet character from the uploaded image, lowering its head to eat from a small bowl, same art style and markings, full body visible, transparent background, no floor, shadow or text."}
        };
        private async void GenerateActions()
        {
            Dictionary<int,Bitmap> poses=null;
            try
            {
                RequireImage();if(atlas.Checked) throw new ArgumentException("动作图集已带完整动作，不需要再生成姿势。");
                ChatService.Endpoint(pet.data.ImageEndpoint);if(String.IsNullOrWhiteSpace(pet.data.ImageModel)) throw new ArgumentException("请先在设置中填写图片模型名称。");
                if(MessageBox.Show("将按当前形象生成 4 组动作姿势（睡觉、挥爪、跳跃、吃饭），共上传 4 次图片，服务可能计费。继续吗？","萌栖 · AI 生成动作",MessageBoxButtons.YesNo)!=DialogResult.Yes) return;
                generating=true;cancel=new CancellationTokenSource();
                poses=new Dictionary<int,Bitmap>();
                foreach(PoseSpec spec in PoseList)
                {
                    status.Text="正在生成「"+spec.Label+"」姿势（"+(Array.IndexOf(PoseList,spec)+1)+"/"+PoseList.Length+"）…";
                    Bitmap pose=await ChatService.GeneratePet(pet.data,current,name.Text,spec.Prompt,cancel.Token);
                    if(IsDisposed) {pose.Dispose();return;}
                    poses[spec.Row]=pose;
                }
                ClearPoses();generatedPoses=poses;poses=null;
                status.Text="动作姿势生成完毕：睡觉 / 挥爪 / 跳跃 / 吃饭。点「创建并使用」后，这四个动作会替换程序化动画，其余动作继续用软件自带的变换。";
            }
            catch(OperationCanceledException) {status.Text="已取消生成，已生成的姿势丢弃，原图片保留。";}
            catch(Exception error) {status.Text=error.Message;MessageBox.Show(error.Message,"萌栖 · AI 生成动作");}
            finally
            {
                generating=false;if(cancel!=null) {cancel.Dispose();cancel=null;}
                if(poses!=null) foreach(Bitmap pose in poses.Values) pose.Dispose();
            }
        }
        private void Import() {RequireImage();pet.ImportPet(current,name.Text,atlas.Checked,atlas.Checked?null:generatedPoses);DialogResult=DialogResult.OK;Close();}
        private void Export() {RequireImage();using(SaveFileDialog dialog=new SaveFileDialog {Filter="透明 PNG|*.png",FileName=name.Text+".png"}) if(dialog.ShowDialog(this)==DialogResult.OK) current.Save(dialog.FileName,ImageFormat.Png);}
        protected override void Dispose(bool disposing) {if(disposing) {if(cancel!=null) cancel.Cancel();preview.Image=null;ClearPoses();if(current!=null) current.Dispose();if(original!=null) original.Dispose();}base.Dispose(disposing);}
    }
}
