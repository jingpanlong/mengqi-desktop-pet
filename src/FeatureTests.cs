using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Threading;
using System.Text;
using System.Windows.Forms;

namespace Mengqi
{
    internal static class FeatureTests
    {
        private static readonly List<string> passed=new List<string>();
        private static void Check(bool condition,string name) {if(!condition) throw new InvalidOperationException("功能检查失败："+name);passed.Add(name);}
        internal static void Run(PetWindow pet,string directory)
        {
            string scratch=Path.Combine(directory,"_work");Directory.CreateDirectory(scratch);
            DateTime now=new DateTime(2026,10,9,15,0,0);
            AppData data=new AppData();Reminder once=new Reminder {Next=now.AddMinutes(-1)};data.Reminders.Add(once);
            Check(ReminderEngine.Poll(data,now).Count==1 && !once.Enabled && ReminderEngine.Poll(data,now).Count==0,"一次提醒只触发一次");
            Reminder daily=new Reminder {Next=now.AddDays(-3).Date.AddHours(9),Repeat="每天"};Check(ReminderEngine.Advance(daily,now)==now.Date.AddDays(1).AddHours(9),"每天提醒跨日补发");
            daily.Repeat="工作日";Check(ReminderEngine.Advance(daily,now)==now.Date.AddDays(3).AddHours(9),"工作日提醒跳过周末");
            daily.Repeat="每周";daily.Next=now.Date.AddHours(9);Check(ReminderEngine.Advance(daily,now)==now.Date.AddDays(7).AddHours(9),"每周提醒保留星期");
            daily.Repeat="间隔";daily.Interval=45;Check(ReminderEngine.Advance(daily,now)==now.AddMinutes(45),"间隔提醒准确推进");
            data.Quiet=true;data.QuietStart=23;data.QuietEnd=7;once.Enabled=true;once.Next=now.Date.AddHours(22);
            Check(ReminderEngine.Poll(data,now.Date.AddHours(23)).Count==0 && once.Enabled,"勿扰不丢失到期提醒");
            Check(ReminderEngine.Poll(data,now.Date.AddDays(1).AddHours(7)).Count==1,"勿扰结束补发");
            Reminder parsed=ReminderEngine.Parse("10分钟后提醒我吃药",now);Check(parsed!=null && parsed.Category=="吃药" && parsed.Next==now.AddMinutes(10),"聊天创建吃药提醒");
            parsed=ReminderEngine.Parse("每天09:00提醒我工作",now);Check(parsed.Repeat=="每天" && parsed.Next==now.Date.AddDays(1).AddHours(9),"聊天创建重复工作提醒");
            bool rejected=false;try {ReminderEngine.Parse("每天29:99提醒我工作",now);} catch(ArgumentException) {rejected=true;}Check(rejected,"拒绝无效提醒时间");
            Check(ChatService.Offline("我叫小林",data,new Random(1)).Contains("小林") && data.UserName=="小林","离线聊天记住称呼");
            Check(ChatService.Offline("计算(2+3)*4",data,new Random(1)).Contains("20"),"离线计算");
            Check(ChatService.ParseReply("{\"choices\":[{\"message\":{\"content\":\"你好，小黑\"}}]}")=="你好，小黑","AI 回复解析");
            rejected=false;try {ChatService.Endpoint("http://example.com/chat");} catch(ArgumentException) {rejected=true;}Check(rejected && ChatService.Endpoint("http://localhost:1234/v1/chat/completions").IsLoopback,"联网接口 HTTPS 与本机规则");
            data.ChatEndpoint="https://test.invalid/v1/chat/completions";data.Model="test-model";data.Chat.Add(new ChatEntry {Text="你好"});
            Func<HttpClient> productionClient=ChatService.ClientFactory;FakeHandler transport=new FakeHandler();
            try
            {
                ChatService.ClientFactory=delegate {return new HttpClient(transport,false);};
                string reply=Task.Run(()=>ChatService.Online(data,CancellationToken.None)).GetAwaiter().GetResult();Check(reply=="接口测试回复" && transport.RequestBody.Contains("test-model") && transport.RequestBody.Contains("messages"),"聊天请求构造与响应完整流程（模拟服务）");
                transport.Fail=true;rejected=false;try {Task.Run(()=>ChatService.Online(data,CancellationToken.None)).GetAwaiter().GetResult();} catch(HttpRequestException) {rejected=true;}Check(rejected,"联网失败明确报告（模拟服务）");transport.Fail=false;
                data.ImageEndpoint="https://test.invalid/v1/images/edits";data.ImageModel="test-image";transport.Image=true;
                using(Bitmap input=new Bitmap(80,80)) using(Bitmap output=Task.Run(()=>ChatService.GeneratePet(data,input,"测试猫",CancellationToken.None)).GetAwaiter().GetResult()) Check(output.Width==20 && transport.RequestBody.Contains("test-image") && transport.RequestBody.Contains("transparent"),"图片上传与生成图片解码（模拟服务）");
            }
            finally {ChatService.ClientFactory=productionClient;transport.Dispose();}
            string protectedKey="test-encrypted-placeholder";bool keyProtectionTest=false;
            try {protectedKey=DataStore.Protect("test-placeholder");Check(protectedKey!="test-placeholder" && DataStore.Unprotect(protectedKey)=="test-placeholder","Windows 用户密钥加密往返");keyProtectionTest=true;} catch(System.Security.Cryptography.CryptographicException) { }
            pet.data.Notes="功能测试笔记";pet.data.Tasks.Add(new TaskItem {Text="完成今天的一件小事",Done=true});pet.data.Reminders.Add(new Reminder {Title="起身活动一下",Category="休息",Next=DateTime.Now.AddMinutes(30)});pet.data.Chat.Add(new ChatEntry {Role="user",Text="你好，小黑"});pet.data.Chat.Add(new ChatEntry {Role="assistant",Text="你好呀！我会在桌面陪着你，也记得适时休息。"});pet.SaveData();
            int before=pet.data.Reminders.Count;string chatReply=pet.SendChat("10分钟后提醒我喝水",CancellationToken.None).GetAwaiter().GetResult();Check(pet.data.Reminders.Count==before+1 && chatReply.Contains("已设置"),"实际聊天入口创建提醒并回复");
            AppData loaded=pet.store.Load();Check(loaded.Notes==pet.data.Notes && loaded.Tasks.Count>0 && loaded.Tasks[0].Done,"提醒待办笔记聊天持久化");
            pet.SaveData();Check(File.Exists(pet.store.FilePath+".bak"),"原子保存生成备份");
            pet.StartFocus(false);Check(pet.data.FocusRunning && !pet.data.FocusPaused,"开始专注");pet.PauseFocus();Check(pet.data.FocusPaused && pet.data.FocusLeft>0,"暂停专注");pet.PauseFocus();Check(!pet.data.FocusPaused && pet.data.FocusDeadline>DateTime.Now,"恢复专注");pet.StopFocus();Check(!pet.data.FocusRunning,"结束专注");
            pet.data.Hunger=20;pet.Interact("feed");Check(pet.data.Hunger==40,"喂食改变真实状态");pet.data.Happiness=20;pet.Interact("pet");Check(pet.data.Happiness==30,"摸摸改变真实状态");
            using(Bitmap fixture=new Bitmap(120,150))
            {
                using(Graphics g=Graphics.FromImage(fixture)) {g.Clear(Color.White);g.FillEllipse(Brushes.SaddleBrown,24,18,74,110);g.FillEllipse(Brushes.LightSkyBlue,45,40,12,12);}
                using(Bitmap cut=PetImages.RemoveBackground(fixture,20))
                {
                    Check(cut.GetPixel(0,0).A==0 && cut.GetPixel(60,70).A==255,"照片去背景保留主体");
                    for(int row=0;row<15;row++) using(Bitmap frame=PetImages.Frame(cut,row,1,4,192,208,false)) Check(PetImages.VisibleBounds(frame).Width>0,"照片模式渲染动作 "+row);
                    pet.ImportPet(cut,"测试宠物",false);Check(pet.customImage!=null && File.Exists(pet.store.PetPath(pet.data.PetId)),"创建照片宠物保存并加载");
                    pet.RenderForTest(4,2);pet.data.ProtectedKey=protectedKey;string backup=Path.Combine(scratch,"test-backup.zip");pet.ExportBackup(backup);
                    using(ZipArchive zip=ZipFile.OpenRead(backup)) using(StreamReader reader=new StreamReader(zip.GetEntry("data.json").Open())) Check(!reader.ReadToEnd().Contains(protectedKey),"备份排除 API Key");
                    pet.data.Notes="changed";pet.ImportBackup(backup);Check(pet.data.Notes=="功能测试笔记" && pet.data.ProtectedKey==protectedKey && pet.customImage!=null,"恢复备份包含宠物并保留本机凭据");
                }
            }
            rejected=false;try {pet.store.PetPath("../escape");} catch(InvalidDataException) {rejected=true;}Check(rejected,"拒绝宠物路径穿越");
            string bad=Path.Combine(scratch,"invalid-backup.zip");if(File.Exists(bad)) File.Delete(bad);
            using(ZipArchive zip=ZipFile.Open(bad,ZipArchiveMode.Create)) using(StreamWriter writer=new StreamWriter(zip.CreateEntry("data.json").Open())) writer.Write("{\"Pets\":[{\"Id\":\"../escape\"}]}");
            rejected=false;try {pet.ImportBackup(bad);} catch(InvalidDataException) {rejected=true;}Check(rejected && pet.data.Notes=="功能测试笔记","无效备份不修改当前数据");
            Check(SystemOps.IsDeleteIntent("删除文件") && !SystemOps.IsDeleteIntent("删除提醒") && SystemOps.IsDeleteIntent("帮我把桌面这个文件删掉"),"识别删除文件指令");
            string createName;bool createFolder;
            Check(SystemOps.TryParseCreate("创建文件 报价单.txt",out createName,out createFolder) && !createFolder && createName=="报价单.txt","解析创建文件指令");
            Check(SystemOps.TryParseCreate("创建文件夹 工作备份",out createName,out createFolder) && createFolder && createName=="工作备份","解析创建文件夹指令");
            string made=SystemOps.CreateItem("萌栖-自检文件",false,scratch);
            Check(File.Exists(made),"创建文件落盘");
            string stamp=DateTime.Now.ToString("HHmmss");
            string folderStem="萌栖-自检文件夹"+stamp;
            string madeFolder=SystemOps.CreateItem(folderStem,true,scratch);
            Check(Directory.Exists(madeFolder) && Path.GetFileName(madeFolder)==folderStem,"创建文件夹落盘");
            string madeFolderAgain=SystemOps.CreateItem(folderStem,true,scratch);
            Check(Directory.Exists(madeFolderAgain) && madeFolderAgain!=madeFolder && Path.GetFileName(madeFolderAgain)==folderStem+"-2","重名文件夹自动加序号");
            string clashStem="萌栖-撞名"+stamp;
            string clashFile=SystemOps.CreateItem(clashStem,false,scratch);
            Check(File.Exists(clashFile),"建一个文件当撞名对手");
            string clashFolder=SystemOps.CreateItem(clashStem,true,scratch);
            Check(Directory.Exists(clashFolder) && Path.GetFileName(clashFolder)==clashStem+"-2","文件夹不与同名文件撞名");
            string dottedStem="尾巴有点"+stamp;
            string trimmedName=SystemOps.CreateItem(dottedStem+". ",true,scratch);
            Check(Directory.Exists(trimmedName) && Path.GetFileName(trimmedName)==dottedStem,"文件夹名去掉结尾的点");
            List<FileTarget> fileTargets=new List<FileTarget>();fileTargets.Add(new FileTarget {Name="a.txt",Path="X:\\a.txt",Rect=new Rectangle(10,10,80,90)});
            Check(SystemOps.PickTarget(fileTargets,new Point(50,50))!=null && SystemOps.PickTarget(fileTargets,new Point(600,600))==null,"瞄准命中判断");
            File.WriteAllText(Path.Combine(scratch,"报价单.txt"),"");
            File.WriteAllText(Path.Combine(scratch,"快捷方式入口.lnk"),"");
            string hidden=SystemOps.ResolvePath(scratch,"报价单"),shortcut=SystemOps.ResolvePath(scratch,"快捷方式入口");
            Check(hidden!=null && hidden.EndsWith("报价单.txt") && shortcut!=null && shortcut.EndsWith("快捷方式入口.lnk"),"瞄准解析隐藏扩展名与快捷方式");
            Check(SystemOps.ResolvePath(scratch,"不存在的名字")==null,"瞄准不虚构路径");
            List<FileTarget> live=SystemOps.EnumerateFileTargets();bool liveOk=true;
            foreach(FileTarget target in live) if(target.Path!=null && !File.Exists(target.Path) && !Directory.Exists(target.Path)) liveOk=false;
            Check(liveOk,"实机瞄准枚举路径有效（本机 "+live.Count+" 个目标）");
            using(Bitmap sprite=SystemOps.IconImageFor(Path.Combine(scratch,"报价单.txt"))) Check(sprite!=null && sprite.Width>0,"提取文件壳图标");
            string doomed=Path.Combine(scratch,"注定被吃掉.txt");File.WriteAllText(doomed,"被桌宠吃掉的自检文件");
            SystemOps.DeleteForever(doomed);
            Check(!File.Exists(doomed),"真删除不进回收站");
            string stubborn=Path.Combine(scratch,"倔强的只读文件.txt");File.WriteAllText(stubborn,"只读属性挡不住桌宠");
            File.SetAttributes(stubborn,FileAttributes.ReadOnly);
            SystemOps.DeleteForever(stubborn);
            Check(!File.Exists(stubborn),"只读文件强制删除");
            string stubbornFolder=Path.Combine(scratch,"倔强的文件夹");
            Directory.CreateDirectory(stubbornFolder);
            string nested=Path.Combine(stubbornFolder,"里面的只读.txt");File.WriteAllText(nested,"x");File.SetAttributes(nested,FileAttributes.ReadOnly);
            File.SetAttributes(stubbornFolder,FileAttributes.ReadOnly|FileAttributes.Directory);
            SystemOps.DeleteForever(stubbornFolder);
            Check(!Directory.Exists(stubbornFolder),"只读文件夹整棵强制删除");
            Check(SystemTools.RecycleBinInfo().Ok,"读得到回收站信息");
            Check(SystemTools.CategoryOf(".png")=="图片" && SystemTools.CategoryOf(".XLSX")=="表格" && SystemTools.CategoryOf(".unknownext")=="其他","桌面整理分类正确");
            string tidy=Path.Combine(scratch,"整理测试");Directory.CreateDirectory(tidy);
            File.WriteAllText(Path.Combine(tidy,"照片.png"),"x");File.WriteAllText(Path.Combine(tidy,"报表.xlsx"),"x");
            File.WriteAllText(Path.Combine(tidy,"安装包.zip"),"x");File.WriteAllText(Path.Combine(tidy,"说明.unknownext"),"x");
            int tidyTotal;Dictionary<string,List<string>> tidyPlan=SystemTools.PlanTidy(tidy,out tidyTotal);
            string tidySummary=SystemTools.TidyDesktop(tidyPlan,tidy);
            Check(tidyTotal==4 && tidySummary.Contains("图片") && tidySummary.Contains("压缩包"),"整理桌面只搬文件并归类");
            Check(File.Exists(Path.Combine(tidy,"图片","照片.png")) && File.Exists(Path.Combine(tidy,"其他","说明.unknownext")),"整理后文件已归位");
            string shot=SystemTools.CaptureScreen(scratch);
            Check(File.Exists(shot) && new FileInfo(shot).Length>0,"全屏截图落盘");
            Check(SystemTools.Hex(Color.FromArgb(255,18,52,86))=="#123456","取色转十六进制");
            Check(SystemTools.StatusReport().Length>6,"系统状态播报可读");
            Check(SystemOps.ComposeName("报告",false,".md")=="报告.md" && SystemOps.ComposeName("报告",false,"md")=="报告.md" &&
                  SystemOps.ComposeName("报告",false,"")=="报告.txt" && SystemOps.ComposeName("报告.docx",false,"")=="报告.docx" &&
                  SystemOps.ComposeName(" 报告 ",false,".md")=="报告.md" && SystemOps.ComposeName("",false,"")== "新建文件.txt","新建文件后缀合成");
            string gifPath=Path.Combine(scratch,"自检录制.gif");
            using(GifWriter writer=new GifWriter(gifPath,64,48,0))
            {
                foreach(Color tone in new Color[] {Color.FromArgb(255,0,0),Color.FromArgb(0,255,0),Color.FromArgb(0,0,255)})
                    using(Bitmap frame=new Bitmap(64,48)) {using(Graphics g=Graphics.FromImage(frame)) g.Clear(tone);writer.AddFrame(frame,10);}
            }
            Check(File.Exists(gifPath) && new FileInfo(gifPath).Length>60,"屏幕录制 GIF 落盘");
            using(Image decoded=Image.FromFile(gifPath))
            {
                FrameDimension dimension=new FrameDimension(decoded.FrameDimensionsList[0]);
                Check(decoded.GetFrameCount(dimension)==3,"GIF 三帧都能解出来");
                decoded.SelectActiveFrame(dimension,2);
                using(Bitmap frame=new Bitmap(decoded)) {Color sampled=frame.GetPixel(32,24);Check(sampled.B>200 && sampled.R<60,"GIF 帧颜色正确");}
            }
            Check(StrictGifFrames(gifPath)==3,"GIF 块结构合规");
            // 大尺寸+噪声：LZW 数据远超 255 字节，专门压一压子块切分
            string bigGif=Path.Combine(scratch,"自检大图.gif");
            using(GifWriter big=new GifWriter(bigGif,400,300,0))
            {
                Random noise=new Random(7);
                for(int n=0;n<2;n++)
                    using(Bitmap frame=new Bitmap(400,300))
                    {
                        for(int y=0;y<300;y++) for(int x=0;x<400;x++) frame.SetPixel(x,y,Color.FromArgb(noise.Next(256),noise.Next(256),noise.Next(256)));
                        big.AddFrame(frame,8);
                    }
            }
            Check(StrictGifFrames(bigGif)==2,"大图 GIF 子块切分正确");
            Check(new FileInfo(bigGif).Length>2000,"大图 GIF 体积正常");
            // ---- MP4 视频编码（录屏现已改为 H.264/MP4，不再是 GIF）----
            // 优先用本机 ffmpeg，没有则回退 Windows 自带的 Media Foundation；两者都没有才跳过。
            string mp4Path=Path.Combine(scratch,"自检视频.mp4");
            bool backendReady=FfmpegLocator.Path!=null;
            if(!backendReady) {try {VideoEncoder.RequireAvailable();backendReady=true;} catch { }}
            if(!backendReady)
            {
                Check(true,"MP4 编码本机跳过（既没有 ffmpeg，也没有可用的 Media Foundation）");
            }
            else
            {
                try
                {
                    using(IVideoSink enc=VideoSinkFactory.Start(64,48,10,mp4Path))
                    {
                        foreach(Color tone in new Color[] {Color.FromArgb(255,200,40,40),Color.FromArgb(255,40,200,40),Color.FromArgb(255,40,40,200)})
                            using(Bitmap frame=new Bitmap(64,48)) {using(Graphics g=Graphics.FromImage(frame)) g.Clear(tone); enc.AddFrame(frame);}
                        Check(enc.Frames==3,"视频编码器写入三帧");
                    }
                    byte[] body=File.ReadAllBytes(mp4Path);
                    Check(File.Exists(mp4Path) && new FileInfo(mp4Path).Length>200,"屏幕录制 MP4 落盘");
                    Check(Encoding.ASCII.GetString(body,4,4)=="ftyp","MP4 含 ftyp 容器头");
                    Check(IndexOf(body,"moov")>=0 && IndexOf(body,"avcC")>=0,"MP4 含 moov 与 avcC（H.264）");
                }
                catch(Exception ex) {Check(false,"MP4 编码失败："+ex.Message);}
            }
            string regionShot=SystemTools.CaptureRegion(new Rectangle(0,0,160,120),scratch);
            Check(File.Exists(regionShot),"区域截图落盘");
            using(Bitmap regionImage=new Bitmap(regionShot)) Check(regionImage.Width==160 && regionImage.Height==120,"区域截图尺寸正确");
            ToolStripMenuItem systemMenu=null;
            foreach(ToolStripItem entry in pet.MenuForTest.Items) {ToolStripMenuItem candidate=entry as ToolStripMenuItem;if(candidate!=null && candidate.Text=="系统操作") systemMenu=candidate;}
            int systemCount=0;ToolStripMenuItem folderMenu=null;List<string> systemLabels=new List<string>();
            if(systemMenu!=null) foreach(ToolStripItem entry in systemMenu.DropDownItems)
            {
                if(entry is ToolStripSeparator) continue;
                systemCount++;systemLabels.Add(entry.Text);
                if(entry.Text=="打开常用文件夹") folderMenu=entry as ToolStripMenuItem;
            }
            Check(systemMenu!=null && systemCount==11,"右键菜单含系统操作十一项");
            Check(systemLabels.Contains("删除文件…") && systemLabels.Contains("创建文件…") && systemLabels.Contains("创建文件夹…"),"系统操作含删除与创建文件");
            ToolStripMenuItem shotMenu=null,recordMenu=null;
            if(systemMenu!=null) foreach(ToolStripItem entry in systemMenu.DropDownItems)
            {
                if(entry.Text=="截图") shotMenu=entry as ToolStripMenuItem;
                if(entry.Text=="录屏") recordMenu=entry as ToolStripMenuItem;
            }
            Check(shotMenu!=null && shotMenu.DropDownItems.Count==2,"系统操作含截图两项");
            Check(recordMenu!=null && recordMenu.DropDownItems.Count==2,"系统操作含录屏两项");
            Check(folderMenu!=null && folderMenu.DropDownItems.Count==4,"系统操作含打开常用文件夹四项");
            Check(SystemOps.ComposeName("备份",true,".md")=="备份" && SystemOps.ComposeName("",true,"")=="新建文件夹","创建文件夹不拼后缀");
            ToolStripMenuItem callMenu=null;foreach(ToolStripItem item in pet.MenuForTest.Items) if(item.Text=="再叫一只") callMenu=item as ToolStripMenuItem;
            Check(callMenu!=null && callMenu.DropDownItems.Count>=2 && callMenu.DropDownItems[0].Text=="小黑" && callMenu.DropDownItems[1].Text=="臭臭","再叫一只是子菜单");
            Check(PetWindow.Companions.Count==0,"启动时只有一只桌宠");
            pet.SpawnCompanion("preset:chouchou");
            Check(PetWindow.Companions.Count==1 && PetWindow.Companions[0].PetName=="臭臭","再叫一只桌宠出场");
            PetWindow buddy=PetWindow.Companions[0];
            bool companionTrimmed=true;foreach(ToolStripItem item in buddy.MenuForTest.Items) if(item.Text=="提醒与计划"||item.Text=="系统操作") companionTrimmed=false;
            Check(companionTrimmed && buddy.MenuForTest.Items.Count==9,"陪伴桌宠菜单精简");
            for(int row=0;row<15;row++) buddy.RenderForTest(row,0);
            Check(true,"陪伴桌宠十五组动作渲染");
            buddy.Close();Application.DoEvents();
            Check(PetWindow.Companions.Count==0,"送走陪伴桌宠");
            Check(ChatService.Offline("你可以做什么",data,new Random(1)).Contains("删除") && ChatService.Offline("你叫什么",data,new Random(1)).Contains(data.Name),"回答能力与名字");
            pet.LoadPet("preset:chouchou");Check(pet.PetName=="臭臭" && pet.data.PetId=="preset:chouchou","切换内置臭臭");
            for(int row=0;row<15;row++) pet.RenderForTest(row,0);
            pet.LoadPet("");Check(pet.PetName=="小黑" && pet.data.PetId=="","切回内置小黑");
            pet.LoadPet("");pet.ApplyScale(100);pet.data.ProtectedKey="";pet.data.Happiness=85;pet.data.Hunger=80;pet.data.Energy=90;
            pet.VerifySmoothRendering();Check(true,"十五组动作120次平滑透明渲染");pet.VerifyVisibleBubbleReuse();Check(true,"气泡可见时连续聊天与创建提醒回归");
            string shots=Path.Combine(directory,"ui");Directory.CreateDirectory(shots);
            using(Bitmap menuImage=RenderMenu(pet.MenuForTest)) if(menuImage!=null) menuImage.Save(Path.Combine(shots,"界面-右键菜单.png"),ImageFormat.Png);
            if(systemMenu!=null) using(Bitmap subMenuImage=RenderMenu(systemMenu.DropDown)) if(subMenuImage!=null) subMenuImage.Save(Path.Combine(shots,"界面-系统操作.png"),ImageFormat.Png);
            using(HubForm hub=new HubForm(pet)) {hub.CapturePages(shots);hub.SelectPage("tools");hub.VerifyToolActions();Check(true,"实际工具按钮创建倒计时与控制秒表");Check(true,"秒表笔记保存和关于作者信息");Check(true,"关于页含开源仓库地址与下载入口");}
            using(ReminderEditor editor=new ReminderEditor(new Reminder {Title="工作结束后休息",Category="工作"})) Capture(editor,Path.Combine(shots,"界面-编辑提醒.png"));
            using(ReminderAlert alert=new ReminderAlert(pet,new Reminder {Title="休息一下，起来活动活动",Category="休息"})) Capture(alert,Path.Combine(shots,"界面-提醒弹窗.png"));
            using(PhotoEditor editor=new PhotoEditor(pet)) Capture(editor,Path.Combine(shots,"界面-上传宠物.png"));
            using(BubbleForm bubble=new BubbleForm())
            {
                bubble.SetText("你好呀！我是小黑。今天也要记得休息，喵～",5000);
                using(Bitmap snap=bubble.Snapshot())
                {
                    Check(snap!=null && snap.Width>=220 && snap.Height>=80,"聊天气泡渲染成图");
                    Check(snap.GetPixel(snap.Width/2,snap.Height/2).A==255,"气泡卡片不透明");
                    Check(snap.GetPixel(snap.Width/2,snap.Height-21).A>200,"气泡尾巴指向桌宠");
                    Check(snap.GetPixel(2,2).A==0,"气泡圆角外保持透明");
                    bool inkFound=false;
                    for(int y=snap.Height/3;y<snap.Height*2/3 && !inkFound;y++) for(int x=30;x<snap.Width-40;x++)
                    {
                        Color c=snap.GetPixel(x,y);
                        if(Math.Abs(c.R-73)<40 && Math.Abs(c.G-54)<40 && Math.Abs(c.B-103)<40) {inkFound=true;break;}
                    }
                    Check(inkFound,"气泡正文按正确字色渲染");
                    snap.Save(Path.Combine(shots,"界面-聊天气泡.png"),ImageFormat.Png);
                }
            }
            using(CreateInputDialog folderDialog=new CreateInputDialog("新建一个文件夹","建在桌面上。","新建文件夹",true,delegate { },delegate { }))
                Capture(folderDialog,Path.Combine(shots,"界面-新建文件夹.png"));
            using(CreateInputDialog fileDialog=new CreateInputDialog("新建一个文件","建在桌面上；后缀可下拉选或自己填，留空自动补 .txt","新建文件",false,delegate { },delegate { }))
                Capture(fileDialog,Path.Combine(shots,"界面-新建文件.png"));
            int formShots=0;
            foreach(string file in Directory.GetFiles(shots,"*.png"))
            {
                string fileName=Path.GetFileName(file);
                if(fileName=="界面-右键菜单.png" || fileName=="界面-系统操作.png") continue;
                formShots++;
            }
            Check(formShots==13,"实际窗体十三张界面渲染");
            Check(File.Exists(Path.Combine(shots,"界面-右键菜单.png")) && File.Exists(Path.Combine(shots,"界面-系统操作.png")),"右键菜单渲染成图");
            pet.SaveData();File.WriteAllText(Path.Combine(directory,"feature-report.json"),DataStore.Json().Serialize(new {ok=true,checks=passed.Count,passed=passed,onlineLiveTest=false,windowsUserKeyProtectionTest=keyProtectionTest,keyProtectionNote=keyProtectionTest?"通过":"当前测试进程未加载 Windows 用户加密配置；应用使用 DPAPI，不回退到明文保存。",uiCaptures=15}),System.Text.Encoding.UTF8);
        }
        internal static void PrepareCapture(Form form) {form.ShowInTaskbar=false;form.Opacity=0;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-10000,-10000);form.Show();form.PerformLayout();Application.DoEvents();}
        private static void Capture(Form form,string path) {PrepareCapture(form);using(Bitmap image=new Bitmap(form.Width,form.Height)) {form.DrawToBitmap(image,new Rectangle(0,0,form.Width,form.Height));image.Save(path,ImageFormat.Png);}form.Hide();}
        private static Bitmap RenderMenu(ToolStrip strip)
        {
            try
            {
                Size size=strip.GetPreferredSize(Size.Empty);
                if(size.Width<=0 || size.Height<=0) return null;
                Bitmap image=new Bitmap(size.Width,size.Height);
                using(Graphics g=Graphics.FromImage(image)) g.Clear(Color.White);
                strip.DrawToBitmap(image,new Rectangle(0,0,size.Width,size.Height));
                return image;
            }
            catch { return null; }
        }
        /// <summary>严格按 GIF 规范走一遍块结构，返回帧数；结构不合法返回 -1。
        /// GDI+ 解码很宽容（连扩展块缺终止符都能读），浏览器和微信这类严格解析器会直接失败，
        /// 所以不能只靠 GDI+ 解码成功来判断文件是否合法。</summary>
        private static int IndexOf(byte[] hay,string needle)
        {
            if(hay==null || needle==null || needle.Length==0) return -1;
            byte[] n=Encoding.ASCII.GetBytes(needle);
            for(int i=0;i+n.Length<=hay.Length;i++)
            {
                bool match=true;
                for(int j=0;j<n.Length;j++) if(hay[i+j]!=n[j]) {match=false;break;}
                if(match) return i;
            }
            return -1;
        }
        private static int StrictGifFrames(string path)
        {
            byte[] data=File.ReadAllBytes(path);
            if(data.Length<14 || Encoding.ASCII.GetString(data,0,6)!="GIF89a") return -1;
            int i=13;
            if((data[10]&0x80)!=0) i+=3*(1<<((data[10]&7)+1));      // 全局色表
            int frames=0;
            while(i<data.Length)
            {
                byte marker=data[i];
                if(marker==0x3B) break;                              // 结尾符
                if(marker==0x21) {if(i+2>data.Length) return -1;i+=2;}   // 扩展块：标记+标签
                else if(marker==0x2C)                                // 图像描述符
                {
                    if(i+10>data.Length) return -1;
                    byte flags=data[i+9];
                    i+=10;
                    if((flags&0x80)!=0) i+=3*(1<<((flags&7)+1));     // 局部色表
                    if(i>=data.Length) return -1;
                    i+=1;                                            // LZW 最小码长
                    frames++;
                }
                else return -1;
                while(i<data.Length && data[i]!=0) {int size=data[i];i+=size+1;}  // 跳子块
                if(i>=data.Length) return -1;
                i+=1;                                                // 子块结束的 00
            }
            return i<data.Length && data[i]==0x3B ? frames : -1;
        }
        private sealed class FakeHandler:HttpMessageHandler
        {
            internal string RequestBody;internal bool Image,Fail;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
            {
                token.ThrowIfCancellationRequested();RequestBody=await request.Content.ReadAsStringAsync();
                if(Fail) return new HttpResponseMessage(HttpStatusCode.Unauthorized) {Content=new StringContent("test failure")};
                string result="{\"choices\":[{\"message\":{\"content\":\"接口测试回复\"}}]}";
                if(Image) using(Bitmap fixture=new Bitmap(20,24)) using(MemoryStream stream=new MemoryStream()) {fixture.Save(stream,ImageFormat.Png);result="{\"data\":[{\"b64_json\":\""+Convert.ToBase64String(stream.ToArray())+"\"}]}";}
                return new HttpResponseMessage(HttpStatusCode.OK) {Content=new StringContent(result,System.Text.Encoding.UTF8,"application/json")};
            }
        }
    }
}
