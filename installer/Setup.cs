using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Diagnostics;

[assembly:AssemblyTitle("萌栖安装向导")]
[assembly:AssemblyProduct("萌栖")]
[assembly:AssemblyVersion("1.7.3.0")]
[assembly:AssemblyFileVersion("1.7.3.0")]

namespace MengqiSetup
{
    internal static class Program
    {
        internal const string Marker="Mengqi-8f98f0be-1734-4585-93ba-97fae9eabafa";
        internal const string KeyPath=@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Mengqi";
        [STAThread] static int Main(string[] args)
        {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            if(args.Length==2 && args[0]=="--self-test") return SelfTest(args[1]);
            try {Application.Run(new Wizard(Path.GetFileName(Application.ExecutablePath)=="卸载萌栖.exe"));return 0;}
            catch(Exception e) {MessageBox.Show(e.Message,"萌栖安装程序",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
        }
        internal static byte[] Payload()
        {
            using(Stream source=Assembly.GetExecutingAssembly().GetManifestResourceStream("mengqi.payload")) using(MemoryStream memory=new MemoryStream()) {source.CopyTo(memory);return memory.ToArray();}
        }
        internal static string Install(string root,bool desktop,bool systemIntegration)
        {
            root=Path.GetFullPath(root);string marker=Path.Combine(root,"mengqi.install");
            if(File.Exists(Path.Combine(root,"萌栖.exe")) && (!File.Exists(marker) || File.ReadAllText(marker)!=Marker)) throw new IOException("该目录已有同名程序，请选择一个新的萌栖安装目录。");
            EnsureStopped(root);Directory.CreateDirectory(root);byte[] payload=Payload();string temp=Path.Combine(root,"萌栖.exe.new");File.WriteAllBytes(temp,payload);
            string app=Path.Combine(root,"萌栖.exe");if(File.Exists(app)) File.Replace(temp,app,null);else File.Move(temp,app);
            string uninstall=Path.Combine(root,"卸载萌栖.exe");File.Copy(Application.ExecutablePath,uninstall,true);File.WriteAllText(marker,Marker);
            if(systemIntegration)
            {
                Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"萌栖.lnk"),app);
                if(desktop) Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"萌栖.lnk"),app);
                using(RegistryKey key=Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    key.SetValue("DisplayName","萌栖");key.SetValue("DisplayVersion","1.7.3");key.SetValue("Publisher","景同志 / 景同学");key.SetValue("DisplayIcon",app);key.SetValue("InstallLocation",root);key.SetValue("UninstallString","\""+uninstall+"\"");key.SetValue("EstimatedSize",payload.Length*2/1024,RegistryValueKind.DWord);key.SetValue("NoModify",1);key.SetValue("NoRepair",1);
                }
            }
            return app;
        }
        private static void EnsureStopped(string root)
        {
            foreach(Process process in Process.GetProcessesByName("萌栖"))
            {
                bool same=false;try {same=String.Equals(process.MainModule.FileName,Path.Combine(root,"萌栖.exe"),StringComparison.OrdinalIgnoreCase);} catch { }
                process.Dispose();if(same) throw new IOException("请先从托盘退出正在运行的萌栖，再进行安装或卸载。");
            }
        }
        internal static void Shortcut(string path,string target)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));Type type=Type.GetTypeFromProgID("WScript.Shell");object shell=Activator.CreateInstance(type);
            try {object link=type.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{path});try {Type t=link.GetType();t.InvokeMember("TargetPath",BindingFlags.SetProperty,null,link,new object[]{target});t.InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,link,new object[]{Path.GetDirectoryName(target)});t.InvokeMember("IconLocation",BindingFlags.SetProperty,null,link,new object[]{target+",0"});t.InvokeMember("Save",BindingFlags.InvokeMethod,null,link,new object[0]);}finally {System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);}}
            finally {System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);}
        }
        private static void RemoveShortcut(string path,string app)
        {
            if(!File.Exists(path)) return;Type type=Type.GetTypeFromProgID("WScript.Shell");object shell=Activator.CreateInstance(type);
            try {object link=type.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{path});try {string target=(string)link.GetType().InvokeMember("TargetPath",BindingFlags.GetProperty,null,link,new object[0]);if(String.Equals(target,app,StringComparison.OrdinalIgnoreCase)) File.Delete(path);}finally {System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);}}finally {System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);}
        }
        internal static void Uninstall(string root,bool systemIntegration,bool selfCleanup)
        {
            root=Path.GetFullPath(root);string marker=Path.Combine(root,"mengqi.install");if(!File.Exists(marker) || File.ReadAllText(marker)!=Marker) throw new IOException("未找到萌栖安装标识，停止卸载。");EnsureStopped(root);
            string app=Path.Combine(root,"萌栖.exe");
            if(systemIntegration)
            {
                RemoveShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"萌栖.lnk"),app);RemoveShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"萌栖.lnk"),app);
                using(RegistryKey key=Registry.CurrentUser.OpenSubKey(KeyPath)) {if(key!=null && String.Equals((string)key.GetValue("InstallLocation"),root,StringComparison.OrdinalIgnoreCase)) {key.Close();Registry.CurrentUser.DeleteSubKey(KeyPath,false);}}
                using(RegistryKey key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true)) {if(key!=null && String.Equals((string)key.GetValue("Mengqi"),"\""+app+"\"",StringComparison.OrdinalIgnoreCase)) key.DeleteValue("Mengqi",false);}
            }
            if(File.Exists(app)) File.Delete(app);File.Delete(marker);
            string uninstaller=Path.Combine(root,"卸载萌栖.exe");
            if(!selfCleanup) {if(File.Exists(uninstaller)) File.Delete(uninstaller);return;}
            // Only delete this named uninstaller after its process exits. Never recursively delete the folder.
            string quoted=uninstaller.Replace("'","''"),dir=root.Replace("'","''");int pid=Process.GetCurrentProcess().Id;
            string command="Wait-Process -Id "+pid+" -ErrorAction SilentlyContinue; Remove-Item -LiteralPath '"+quoted+"' -Force -ErrorAction SilentlyContinue; if ((Test-Path -LiteralPath '"+dir+"') -and @(Get-ChildItem -LiteralPath '"+dir+"' -Force).Count -eq 0) {Remove-Item -LiteralPath '"+dir+"' -ErrorAction SilentlyContinue}";
            Process.Start(new ProcessStartInfo("powershell.exe","-NoProfile -NonInteractive -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(command))) {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});
        }
        private static int SelfTest(string directory)
        {
            try
            {
                Directory.CreateDirectory(directory);string root=Path.Combine(directory,"_work","installed");string app=Install(root,false,false);byte[] actual=File.ReadAllBytes(app),expected=Payload();
                string hash;using(SHA256 sha=SHA256.Create()) {hash=Convert.ToBase64String(sha.ComputeHash(actual));if(hash!=Convert.ToBase64String(sha.ComputeHash(expected))) throw new Exception("安装文件不一致");}
                Install(root,false,false);File.WriteAllText(Path.Combine(root,"用户的其他文件.txt"),"保留");Uninstall(root,false,false);
                if(File.Exists(app) || !File.Exists(Path.Combine(root,"用户的其他文件.txt"))) throw new Exception("卸载范围检查失败");
                using(Wizard form=new Wizard(false)) {form.ShowInTaskbar=false;form.Opacity=0;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-10000,-10000);form.Show();Application.DoEvents();using(Bitmap bitmap=new Bitmap(form.Width,form.Height)) {form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(Path.Combine(directory,"安装界面.png"));}form.Hide();}
                File.WriteAllText(Path.Combine(directory,"installer-report.json"),"{\"ok\":true,\"installPayloadHash\":\""+hash+"\",\"upgradeCheck\":true,\"uninstallPreservesOtherFiles\":true,\"realRegistryAndShortcutChangesTested\":false}");return 0;
            }
            catch(Exception e) {File.WriteAllText(Path.Combine(directory,"installer-report.json"),"{\"ok\":false,\"error\":\""+e.Message.Replace("\\","\\\\").Replace("\"","\\\"")+"\"}");return 1;}
        }
    }
    internal sealed class Wizard:Form
    {
        internal Wizard(bool uninstall)
        {
            Text=uninstall?"卸载萌栖":"萌栖安装向导";Size=new Size(650,460);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;Font=new Font("Microsoft YaHei UI",10);BackColor=Color.FromArgb(246,244,252);
            AutoScaleMode = AutoScaleMode.Dpi;   // 安装向导同样按 DPI 缩放，避免在其它电脑上文字显示不全
            Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Label heading=new Label {Text=uninstall?"卸载萌栖":"欢迎安装萌栖 1.7.3",Location=new Point(30,25),Size=new Size(570,45),Font=new Font(Font.FontFamily,22,FontStyle.Bold),ForeColor=Color.FromArgb(80,54,137)};Controls.Add(heading);
            Controls.Add(new Label {Text=uninstall?"卸载软件及快捷方式。聊天、提醒和自定义宠物数据保留在本机。":"小黑和臭臭的桌面陪伴、聊天、提醒和专注软件。\n为当前 Windows 用户安装，无需管理员权限。",Location=new Point(30,86),Size=new Size(570,65)});
            TextBox path=new TextBox {Text=uninstall?Path.GetDirectoryName(Application.ExecutablePath):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Mengqi"),Location=new Point(30,182),Width=460,ReadOnly=uninstall};Controls.Add(path);
            if(!uninstall) {Button browse=new Button {Text="浏览…",Location=new Point(500,180),Size=new Size(90,32)};browse.Click+=delegate {using(FolderBrowserDialog dialog=new FolderBrowserDialog()) if(dialog.ShowDialog(this)==DialogResult.OK) path.Text=Path.Combine(dialog.SelectedPath,"Mengqi");};Controls.Add(browse);}
            CheckBox desktop=new CheckBox {Text="创建桌面快捷方式",Checked=true,Location=new Point(30,238),AutoSize=true,Visible=!uninstall};Controls.Add(desktop);
            CheckBox run=new CheckBox {Text="安装后运行萌栖",Checked=true,Location=new Point(30,276),AutoSize=true,Visible=!uninstall};Controls.Add(run);
            Button perform=new Button {Text=uninstall?"卸载":"安装",Location=new Point(390,355),Size=new Size(100,38)},cancel=new Button {Text="取消",Location=new Point(500,355),Size=new Size(90,38)};Controls.Add(perform);Controls.Add(cancel);cancel.Click+=delegate {Close();};
            perform.Click+=delegate {perform.Enabled=false;try {if(uninstall) {Program.Uninstall(path.Text,true,true);MessageBox.Show("萌栖已卸载，个人数据已保留。","萌栖");}else {string app=Program.Install(path.Text,desktop.Checked,true);if(run.Checked) Process.Start(app);MessageBox.Show("安装完成。","萌栖");}Close();}catch(Exception e) {MessageBox.Show(e.Message,"萌栖安装程序");perform.Enabled=true;}};
        }
    }
}
