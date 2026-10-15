using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Mengqi
{
    public sealed class Reminder
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Title = "休息一下";
        public string Category = "休息";
        public DateTime Next = DateTime.Now.AddMinutes(30);
        public string Repeat = "一次";
        public int Interval = 60;
        public bool Enabled = true;
    }
    public sealed class TaskItem { public string Id=Guid.NewGuid().ToString("N"); public string Text=""; public bool Done; }
    public sealed class ChatEntry { public string Role="user"; public string Text=""; public DateTime Time=DateTime.Now; }
    public sealed class PetEntry { public string Id=""; public string Name=""; public bool Atlas; public List<int> PoseRows=new List<int>(); public override string ToString() {return Name;} }
    public sealed class AppData
    {
        public int Version=1;
        public string Name="小黑", UserName="", Notes="", PetId="";
        public int Scale=100, Opacity=100, Happiness=80, Hunger=80, Energy=80;
        public bool Automatic=true, Topmost=true, Sound=true, Speech=false, Bubbles=true, Ai=false, Startup=false;
        public string ChatEndpoint="", Model="", ProtectedKey="", ImageEndpoint="", ImageModel="";
        public int WorkMinutes=25, BreakMinutes=5, Sessions=0, FocusSeconds=0;
        public bool Quiet=false;
        public int QuietStart=23, QuietEnd=7;
        public int X=int.MinValue, Y=int.MinValue;
        public DateTime LastStats=DateTime.Now;
        public List<Reminder> Reminders=new List<Reminder>();
        public List<TaskItem> Tasks=new List<TaskItem>();
        public List<ChatEntry> Chat=new List<ChatEntry>();
        public List<string> Log=new List<string>();
        public List<PetEntry> Pets=new List<PetEntry>();
        public bool FocusRunning=false, FocusPaused=false, FocusBreak=false;
        public DateTime FocusDeadline;
        public double FocusLeft=0;
        public bool SmoothMotion=true;
        public int MotionSpeed=100;
        public List<string> QuickPhrases=new List<string>();
    }

    internal sealed class DataStore
    {
        internal readonly string Root;
        internal string FilePath { get { return Path.Combine(Root,"data.json"); } }
        internal DataStore()
        {
            Root=Environment.GetEnvironmentVariable("MENGQI_DATA_DIR");
            if (String.IsNullOrWhiteSpace(Root)) Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Mengqi");
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Path.Combine(Root,"pets"));
        }
        internal static JavaScriptSerializer Json() { return new JavaScriptSerializer {MaxJsonLength=32*1024*1024}; }
        internal AppData Load()
        {
            if (!File.Exists(FilePath)) return new AppData();
            try { return Validate(Json().Deserialize<AppData>(File.ReadAllText(FilePath,Encoding.UTF8))); }
            catch
            {
                string backup=FilePath+".bak";
                if (File.Exists(backup)) try { return Validate(Json().Deserialize<AppData>(File.ReadAllText(backup,Encoding.UTF8))); } catch { }
                throw new InvalidDataException("萌栖的数据文件无法读取。请备份数据目录后检查 data.json 和 data.json.bak。");
            }
        }
        internal static AppData Validate(AppData d)
        {
            if (d==null) throw new InvalidDataException("数据为空。");
            if (d.Reminders==null) d.Reminders=new List<Reminder>();
            if (d.Tasks==null) d.Tasks=new List<TaskItem>();
            if (d.Chat==null) d.Chat=new List<ChatEntry>();
            if (d.Log==null) d.Log=new List<string>();
            if (d.Pets==null) d.Pets=new List<PetEntry>();
            if (d.QuickPhrases==null) d.QuickPhrases=new List<string>();
            d.MotionSpeed=Math.Max(50,Math.Min(150,d.MotionSpeed));
            d.Opacity=Math.Max(35,Math.Min(100,d.Opacity));
            d.QuietStart=Math.Max(0,Math.Min(23,d.QuietStart));d.QuietEnd=Math.Max(0,Math.Min(23,d.QuietEnd));
            d.WorkMinutes=Math.Max(1,Math.Min(180,d.WorkMinutes));d.BreakMinutes=Math.Max(1,Math.Min(60,d.BreakMinutes));
            d.Happiness=Math.Max(0,Math.Min(100,d.Happiness));d.Hunger=Math.Max(0,Math.Min(100,d.Hunger));d.Energy=Math.Max(0,Math.Min(100,d.Energy));
            if (d.Scale!=75 && d.Scale!=100 && d.Scale!=125 && d.Scale!=150) d.Scale=100;
            foreach(PetEntry pet in d.Pets) { if (!Regex.IsMatch(pet.Id??"", "^[a-f0-9]{32}$")) throw new InvalidDataException("宠物标识无效。"); if(pet.PoseRows==null) pet.PoseRows=new List<int>(); }
            foreach(Reminder r in d.Reminders) { if(r.Next.Kind==DateTimeKind.Utc) r.Next=r.Next.ToLocalTime(); r.Interval=Math.Max(1,Math.Min(10080,r.Interval)); }
            if(d.FocusDeadline.Kind==DateTimeKind.Utc) d.FocusDeadline=d.FocusDeadline.ToLocalTime();
            return d;
        }
        internal void Save(AppData data)
        {
            while(data.Chat.Count>500) data.Chat.RemoveAt(0);
            while(data.Log.Count>300) data.Log.RemoveAt(0);
            string temp=FilePath+".tmp";
            File.WriteAllText(temp,Json().Serialize(data),Encoding.UTF8);
            if (File.Exists(FilePath)) File.Replace(temp,FilePath,FilePath+".bak",true);
            else File.Move(temp,FilePath);
        }
        internal static string Protect(string value)
        {
            if(String.IsNullOrEmpty(value)) return "";
            return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value),null,DataProtectionScope.CurrentUser));
        }
        internal static string Unprotect(string value)
        {
            if(String.IsNullOrEmpty(value)) return "";
            try {return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value),null,DataProtectionScope.CurrentUser));}
            catch { return ""; }
        }
        internal string PetPath(string id)
        {
            if(!Regex.IsMatch(id??"","^[a-f0-9]{32}$")) throw new InvalidDataException("宠物标识无效。");
            return Path.Combine(Root,"pets",id+".png");
        }
        /// <summary>单图宠物的 AI 生成动作姿势图（一只宠物最多几行有专属姿势）。</summary>
        internal string PetPosePath(string id,int row)
        {
            if(!Regex.IsMatch(id??"","^[a-f0-9]{32}$")) throw new InvalidDataException("宠物标识无效。");
            if(row<0 || row>14) throw new InvalidDataException("动作编号无效。");
            return Path.Combine(Root,"pets",id+"-pose"+row+".png");
        }
    }

    internal static class ReminderEngine
    {
        internal static DateTime Advance(Reminder reminder,DateTime now)
        {
            DateTime due=reminder.Next;
            if(reminder.Repeat=="每天" || reminder.Repeat=="工作日")
            {
                DateTime next=now.Date.Add(due.TimeOfDay);
                if(next<=now) next=next.AddDays(1);
                if(reminder.Repeat=="工作日") while(next.DayOfWeek==DayOfWeek.Saturday || next.DayOfWeek==DayOfWeek.Sunday) next=next.AddDays(1);
                return next;
            }
            if(reminder.Repeat=="每周")
            {
                DateTime next=now.Date.Add(due.TimeOfDay);
                int offset=((int)due.DayOfWeek-(int)now.DayOfWeek+7)%7;
                next=next.AddDays(offset);
                if(next<=now) next=next.AddDays(7);
                return next;
            }
            if(reminder.Repeat=="间隔") return now.AddMinutes(Math.Max(1,reminder.Interval));
            return due;
        }
        internal static bool IsQuiet(AppData data,DateTime now)
        {
            if(!data.Quiet || data.QuietStart==data.QuietEnd) return false;
            if(data.QuietStart<data.QuietEnd) return now.Hour>=data.QuietStart && now.Hour<data.QuietEnd;
            return now.Hour>=data.QuietStart || now.Hour<data.QuietEnd;
        }
        internal static List<Reminder> Poll(AppData data,DateTime now)
        {
            List<Reminder> result=new List<Reminder>();
            if(IsQuiet(data,now)) return result;
            foreach(Reminder reminder in data.Reminders)
            {
                if(!reminder.Enabled || reminder.Next>now) continue;
                result.Add(reminder);
                if(reminder.Repeat=="一次") reminder.Enabled=false;
                else reminder.Next=Advance(reminder,now);
                data.Log.Add(now.ToString("yyyy-MM-dd HH:mm")+" · 已提醒 · "+reminder.Category+" · "+reminder.Title);
            }
            return result;
        }
        internal static Reminder Parse(string text,DateTime now)
        {
            text=text.Replace("半小时","30分钟").Replace("一小时","60分钟");
            Match after=Regex.Match(text,@"(\d{1,4})\s*(分钟|小时|秒)\s*后.*?提醒(?:我)?\s*(.+)");
            if(after.Success)
            {
                int n=int.Parse(after.Groups[1].Value);
                if(n<=0 || n>10080) throw new ArgumentException("提醒间隔请输入 1 至 10080。");
                string unit=after.Groups[2].Value;
                return new Reminder {Title=after.Groups[3].Value.Trim(),Category=Category(after.Groups[3].Value), Next=now.AddSeconds(n*(unit=="小时"?3600:unit=="分钟"?60:1))};
            }
            Match daily=Regex.Match(text,@"每天\s*(\d{1,2})[:：](\d{2}).*?提醒(?:我)?\s*(.+)");
            if(daily.Success)
            {
                int hour=int.Parse(daily.Groups[1].Value),minute=int.Parse(daily.Groups[2].Value);
                if(hour>23 || minute>59) throw new ArgumentException("请输入有效的时钟时间。");
                DateTime next=now.Date.AddHours(hour).AddMinutes(minute);
                if(next<=now) next=next.AddDays(1);
                return new Reminder {Title=daily.Groups[3].Value.Trim(),Category=Category(daily.Groups[3].Value),Next=next,Repeat="每天"};
            }
            return null;
        }
        private static string Category(string text)
        {
            if(text.Contains("药")) return "吃药";
            if(text.Contains("水")) return "喝水";
            if(text.Contains("工作") || text.Contains("会议")) return "工作";
            if(text.Contains("休息") || text.Contains("站")) return "休息";
            return "自定义";
        }
    }

    internal static class ChatService
    {
        internal static Func<HttpClient> ClientFactory=delegate {return new HttpClient();};
        internal static string Offline(string text,AppData data,Random random)
        {
            string name=data.Name,who=String.IsNullOrEmpty(data.UserName)?"你":data.UserName;
            if(text.Contains("我叫")) { Match m=Regex.Match(text,@"我叫([^，。！？\s]{1,15})"); if(m.Success) {data.UserName=m.Groups[1].Value;return "记住啦，"+data.UserName+"！我是"+name+"，以后我会这样叫你。";} }
            if(text.Contains("几点") || text.Contains("时间")) return "现在是 "+DateTime.Now.ToString("HH:mm")+"，我一直在这里陪着"+who+"。";
            if(text.Contains("日期") || text.Contains("星期")) return "今天是 "+DateTime.Now.ToString("yyyy年M月d日 dddd")+"。";
            if(text.Contains("你好") || text.Contains("早上好") || text.Contains("晚上好")) return "你好呀，"+who+"！摸摸我的头，或者告诉我今天想做什么。";
            if(text.Contains("累") || text.Contains("压力")) return "辛苦啦。先把肩膀放松，喝一口水；我们可以把下一件事拆成很小的一步。";
            if(text.Contains("难过") || text.Contains("孤独") || text.Contains("伤心")) return "我在这里听你说。发生了什么？你可以慢慢讲，不需要一下子整理好。";
            if(text.Contains("开心") || text.Contains("成功")) return "太好啦！我也想蹭蹭这份开心。今天最值得庆祝的是哪件事？";
            if(text.Contains("笑话")) {string[] jokes={"小猫为什么喜欢键盘？因为上面有好多可以按的‘喵键’。","我今天认真工作了三秒钟，然后发现尾巴比工作更有趣。","你负责把事情做好，我负责把桌面坐暖。"};return jokes[random.Next(jokes.Length)];}
            if(text.Contains("状态") || text.Contains("饿")) return String.Format("我的心情 {0}/100，饱腹 {1}/100，精力 {2}/100。",data.Happiness,data.Hunger,data.Energy);
            if(text.Contains("提醒")) return "你可以说‘10分钟后提醒我休息’或‘每天09:00提醒我工作’，也可以到提醒页面设置日期和重复方式。";
            if(text.Contains("可以做什么") || text.Contains("能做什么") || text.Contains("会做什么") || text.Contains("能干什么") || text.Contains("有什么功能"))
                return "我会陪你聊天、设提醒、专注计时、记待办笔记；还能动手帮你：发‘删除文件’进入瞄准模式，点中后我会跑过去问你再把它吃掉（彻底删除，不进回收站）；发‘创建文件 名字’或‘创建文件夹 名字’直接在桌面建好！右键我还能清空回收站、一键整理桌面、全屏截图、屏幕取色、念系统状态、锁屏。";
            if(text.Contains("你叫什么") || text.Contains("你的名字") || text.Contains("你是谁") || text.Contains("介绍下自己"))
                return "我是" + name + "呀！就在你桌面上蹲着的小猫，摸摸我可以加心情哦。";
            if(text.Contains("帮助") || text.Contains("能做")) return "我会陪你聊天、设置提醒、一起专注，还能帮你删文件、建文件。试试‘喂食’、‘开始专注25分钟’、‘10分钟后提醒我喝水’或‘删除文件’。";
            Match math=Regex.Match(text,@"^(?:计算)?\s*([\d\s\.\+\-\*/\(\)]+)\s*$");
            if(math.Success) try {return "算好啦："+new System.Data.DataTable().Compute(math.Groups[1].Value,"").ToString();} catch {return "这个式子我没算出来，试试数字和 + - * / 。";}
            string[] replies={"我在听。你愿意再多说一点吗？","嗯嗯，我会陪着你。现在最想先做哪件小事？","收到啦，"+who+"。我们慢慢来，我就在桌面上。","蹭蹭你。要不要一起专注一小会儿，或者先休息一下？"};
            return replies[random.Next(replies.Length)];
        }
        internal static Uri Endpoint(string text)
        {
            Uri uri;
            if(!Uri.TryCreate(text,UriKind.Absolute,out uri) || (uri.Scheme!="https" && !(uri.Scheme=="http" && uri.IsLoopback))) throw new ArgumentException("接口请输入完整 HTTPS 地址；本机服务可以使用 http://localhost。");
            if(!String.IsNullOrEmpty(uri.UserInfo)) throw new ArgumentException("接口地址不能包含账号或密码。");
            return uri;
        }
        internal static string ParseReply(string json)
        {
            Dictionary<string,object> body=DataStore.Json().Deserialize<Dictionary<string,object>>(json);
            object choices;
            if(!body.TryGetValue("choices",out choices)) throw new InvalidDataException("服务回复中没有 choices。");
            System.Collections.IList list=choices as System.Collections.IList;
            if(list==null || list.Count==0) throw new InvalidDataException("服务没有返回聊天内容。");
            Dictionary<string,object> first=(Dictionary<string,object>)list[0];
            Dictionary<string,object> message=(Dictionary<string,object>)first["message"];
            object content;
            if(!message.TryGetValue("content",out content) || !(content is string) || String.IsNullOrWhiteSpace((string)content)) throw new InvalidDataException("服务返回了空的聊天内容。");
            return (string)content;
        }
        internal static async Task<string> Online(AppData data,CancellationToken token)
        {
            Uri endpoint=Endpoint(data.ChatEndpoint);
            if(String.IsNullOrWhiteSpace(data.Model)) throw new ArgumentException("请先在设置中填写聊天模型名称。");
            List<object> messages=new List<object>();
            messages.Add(new {role="system",content="你是萌栖中的宠物"+data.Name+"。用中文简短、温柔地陪伴用户。不要声称你已经创建提醒或操作了桌面，实际工具操作由软件完成。吃药提醒只复述用户设置，不推断药量。"});
            foreach(ChatEntry entry in data.Chat.Skip(Math.Max(0,data.Chat.Count-20))) messages.Add(new {role=entry.Role,content=entry.Text});
            using(HttpClient client=ClientFactory())
            using(HttpRequestMessage request=new HttpRequestMessage(HttpMethod.Post,endpoint))
            {
                client.Timeout=TimeSpan.FromSeconds(60);
                string key=DataStore.Unprotect(data.ProtectedKey);
                if(!String.IsNullOrWhiteSpace(key)) request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",key);
                request.Content=new StringContent(DataStore.Json().Serialize(new {model=data.Model,messages=messages,stream=false}),Encoding.UTF8,"application/json");
                using(HttpResponseMessage response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token))
                {
                    if(!response.IsSuccessStatusCode) throw new HttpRequestException("聊天接口返回 HTTP "+(int)response.StatusCode+"。请检查地址、模型、凭据或服务额度。");
                    string text=await response.Content.ReadAsStringAsync();
                    if(text.Length>2*1024*1024) throw new InvalidDataException("服务回复过大。");
                    return ParseReply(text);
                }
            }
        }
        internal static Task<Bitmap> GeneratePet(AppData data,Bitmap photo,string petName,CancellationToken token)
        {
            return GeneratePet(data,photo,petName,null,token);
        }
        /// <summary>把当前图片连同提示词发给图片编辑接口，返回一张透明背景结果图。抠图和动作生成都走这条通道。</summary>
        internal static async Task<Bitmap> GeneratePet(AppData data,Bitmap photo,string petName,string prompt,CancellationToken token)
        {
            Uri endpoint=Endpoint(data.ImageEndpoint);
            if(String.IsNullOrWhiteSpace(data.ImageModel)) throw new ArgumentException("请在设置中填写图片模型名称。");
            if(String.IsNullOrWhiteSpace(prompt))
                prompt="Create one centered full-body desktop pet of "+petName+" matching the uploaded animal's markings and face. Compact soft cartoon illustration, all body parts visible, transparent background, no floor, shadow, text or accessories.";
            byte[] bytes;
            using(MemoryStream stream=new MemoryStream()) {photo.Save(stream,ImageFormat.Png);bytes=stream.ToArray();}
            using(HttpClient client=ClientFactory())
            using(MultipartFormDataContent content=new MultipartFormDataContent())
            {
                client.Timeout=TimeSpan.FromMinutes(4);
                string key=DataStore.Unprotect(data.ProtectedKey);
                if(!String.IsNullOrWhiteSpace(key)) client.DefaultRequestHeaders.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",key);
                content.Add(new StringContent(data.ImageModel),"model");
                content.Add(new StringContent(prompt),"prompt");
                content.Add(new StringContent("1024x1024"),"size");
                content.Add(new StringContent("transparent"),"background");
                ByteArrayContent image=new ByteArrayContent(bytes);image.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");content.Add(image,"image","pet.png");
                using(HttpResponseMessage response=await client.PostAsync(endpoint,content,token))
                {
                    if(!response.IsSuccessStatusCode) throw new HttpRequestException("图片接口返回 HTTP "+(int)response.StatusCode+"。请检查图片编辑接口和模型是否支持此请求。");
                    string text=await response.Content.ReadAsStringAsync();
                    Dictionary<string,object> body=DataStore.Json().Deserialize<Dictionary<string,object>>(text);
                    System.Collections.IList items=body["data"] as System.Collections.IList;if(items==null || items.Count==0) throw new InvalidDataException("服务没有返回图片。");
                    Dictionary<string,object> item=(Dictionary<string,object>)items[0];
                    object b64;
                    if(!item.TryGetValue("b64_json",out b64) || !(b64 is string)) throw new InvalidDataException("图片服务需要返回 data[0].b64_json 格式；未导入远程图片链接。");
                    byte[] result=Convert.FromBase64String((string)b64);if(result.Length>20*1024*1024) throw new InvalidDataException("生成图片超过 20 MB。");
                    using(MemoryStream stream=new MemoryStream(result)) using(Bitmap loaded=new Bitmap(stream)) return PetImages.Resize(loaded,512);
                }
            }
        }
    }

    internal static class PetImages
    {
        internal static Bitmap Resize(Bitmap image,int limit)
        {
            double factor=Math.Min(1.0,limit/(double)Math.Max(image.Width,image.Height));
            Bitmap result=new Bitmap(Math.Max(1,(int)(image.Width*factor)),Math.Max(1,(int)(image.Height*factor)),PixelFormat.Format32bppArgb);
            using(Graphics g=Graphics.FromImage(result)) {g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(image,new Rectangle(0,0,result.Width,result.Height));}
            return result;
        }
        internal static Bitmap RemoveBackground(Bitmap original,int tolerance)
        {
            Bitmap image=Resize(original,512);
            int w=image.Width,h=image.Height;
            Color[] corners={image.GetPixel(0,0),image.GetPixel(w-1,0),image.GetPixel(0,h-1),image.GetPixel(w-1,h-1)};
            bool[] seen=new bool[w*h];Queue<int> queue=new Queue<int>();
            for(int x=0;x<w;x++) {queue.Enqueue(x);queue.Enqueue((h-1)*w+x);}
            for(int y=0;y<h;y++) {queue.Enqueue(y*w);queue.Enqueue(y*w+w-1);}
            while(queue.Count>0)
            {
                int n=queue.Dequeue();if(seen[n]) continue;seen[n]=true;
                int x=n%w,y=n/w;Color c=image.GetPixel(x,y);
                bool match=c.A<32;
                foreach(Color corner in corners) if(Math.Max(Math.Abs(c.R-corner.R),Math.Max(Math.Abs(c.G-corner.G),Math.Abs(c.B-corner.B)))<=tolerance) match=true;
                if(!match) continue;
                image.SetPixel(x,y,Color.FromArgb(0,0,0,0));
                if(x>0) queue.Enqueue(n-1);if(x+1<w) queue.Enqueue(n+1);if(y>0) queue.Enqueue(n-w);if(y+1<h) queue.Enqueue(n+w);
            }
            return image;
        }
        internal static Rectangle VisibleBounds(Bitmap image)
        {
            int minX=image.Width,minY=image.Height,maxX=-1,maxY=-1;
            for(int y=0;y<image.Height;y++) for(int x=0;x<image.Width;x++) if(image.GetPixel(x,y).A>16) {minX=Math.Min(minX,x);minY=Math.Min(minY,y);maxX=Math.Max(maxX,x);maxY=Math.Max(maxY,y);}
            if(maxX<0) throw new InvalidDataException("宠物图片完全透明。请撤销或降低去背景强度。");
            return Rectangle.FromLTRB(minX,minY,maxX+1,maxY+1);
        }
        /// <summary>单图宠物也按系统的十五组动作做出可辨认的表演：每个动作有自己的旋转/腾跃/压缩节奏。</summary>
        internal static Bitmap Frame(Bitmap image,int row,int frame,int count,int width,int height,bool atlas)
        {
            Bitmap result=new Bitmap(width,height,PixelFormat.Format32bppPArgb);
            using(Graphics g=Graphics.FromImage(result))
            {
                g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                if(atlas)
                {
                    int r=row<9?row:0;int f=row<9?Math.Min(frame,PetWindow.Durations[r].Length-1):frame%6;
                    g.DrawImage(image,new Rectangle(0,0,width,height),new Rectangle(f*192,r*208,192,208),GraphicsUnit.Pixel);
                    return result;
                }
                Rectangle bounds=VisibleBounds(image);
                double phase=frame/(double)Math.Max(1,count)*Math.PI*2;
                float angle=0,lift=0,squeeze=1,stretch=1,bob=0;
                switch(row)
                {
                    case 0: lift=(float)(Math.Sin(phase)*height*.008);squeeze=1f+(float)(Math.Sin(phase)*.015);break;          // 待机：呼吸起伏
                    case 1: case 2: lift=(float)(Math.Abs(Math.Sin(phase))*height*.05);angle=(float)(Math.Sin(phase)*3);break; // 跑动：小跳+前倾
                    case 3: angle=(float)(Math.Sin(phase)*9);break;                                                             // 挥爪：大幅摆动
                    case 4: lift=(float)(Math.Sin(frame/(double)Math.Max(1,count-1)*Math.PI)*height*.2);break;                  // 跳跃：腾空
                    case 5: squeeze=.86f;angle=4f;break;                                                                        // 失落：压扁下垂
                    case 6: angle=(float)(Math.Sin(phase)*2.5f);break;                                                          // 等待：轻轻摇晃
                    case 7: bob=(float)(Math.Abs(Math.Sin(phase*2))*height*.02);break;                                          // 揉爪：双频节奏
                    case 8: angle=(float)(Math.Sin(phase)*5);break;                                                             // 观察：探头探脑
                    case 9: squeeze=.78f+(float)(Math.Sin(phase)*.02);break;                                                    // 睡觉：匀称呼吸
                    case 10: stretch=1f+(float)(Math.Sin(phase)*.05);squeeze=1f-(float)(Math.Sin(phase)*.04);break;             // 伸懒腰：拉压交替
                    case 11: angle=(float)(Math.Sin(phase)*6);break;                                                            // 舔毛：侧倾
                    case 12: squeeze=1f-(float)(Math.Sin(phase)*.08);lift=(float)(Math.Sin(phase)*height*.015);break;           // 哈欠：压缩舒展
                    case 13: bob=(float)(Math.Abs(Math.Sin(phase*2))*height*.04);break;                                         // 吃饭：点头咀嚼
                    case 14: lift=(float)(Math.Abs(Math.Sin(phase))*height*.08);angle=(float)(Math.Sin(phase)*5);break;         // 玩球：蹦跳摇摆
                }
                float factor=Math.Min(width*.84f/bounds.Width,height*.80f/bounds.Height);
                int drawW=Math.Max(1,(int)(bounds.Width*factor*stretch)),drawH=Math.Max(1,(int)(bounds.Height*factor*squeeze));
                g.TranslateTransform(width/2f,height-8-lift-bob);
                if(row==2) g.ScaleTransform(-1,1);
                g.RotateTransform(angle);
                g.DrawImage(image,new Rectangle(-drawW/2,-drawH,drawW,drawH),bounds,GraphicsUnit.Pixel);
            }
            return result;
        }
    }
}
