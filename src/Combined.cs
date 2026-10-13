using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;

namespace FH2Tools
{
    internal static class NativeLibraries
    {
        static readonly object Gate=new object();
        static IntPtr module;
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr LoadLibrary(string file);
        internal static void EnsureViGEmLoaded()
        {
            lock(Gate)
            {
                if(module!=IntPtr.Zero) return;
                byte[] bytes;
                using(Stream resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("FH2Tools.ViGEmClient.x64"))
                using(MemoryStream memory=new MemoryStream())
                {
                    if(resource==null) throw new InvalidOperationException("内置的虚拟手柄客户端缺失。");
                    resource.CopyTo(memory); bytes=memory.ToArray();
                }
                string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ShakeFH2","ViGEmClient-1.21.256-x64");
                Directory.CreateDirectory(directory); string file=Path.Combine(directory,"ViGEmClient.dll");
                bool matching=false;
                if(File.Exists(file))
                {
                    using(SHA256 sha=SHA256.Create()) matching=Equal(sha.ComputeHash(bytes),sha.ComputeHash(File.ReadAllBytes(file)));
                }
                if(!matching)
                {
                    string temporary=file+".tmp"; File.WriteAllBytes(temporary,bytes);
                    if(File.Exists(file)) File.Replace(temporary,file,null); else File.Move(temporary,file);
                }
                module=LoadLibrary(file);
                if(module==IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(),"无法加载内置虚拟手柄客户端。");
            }
        }
        static bool Equal(byte[] a,byte[] b) { if(a.Length!=b.Length) return false; for(int i=0;i<a.Length;i++) if(a[i]!=b[i]) return false; return true; }
    }

    internal sealed class MainWindow : Form
    {
        internal readonly XboxTriggerBridge.Window Vibration=new XboxTriggerBridge.Window(true);
        internal readonly KeyFH2.Window Keyboard=new KeyFH2.Window(true);
        readonly TabControl tabs=new TabControl();
        readonly Label mode=new Label();
        int active=-1;
        internal MainWindow()
        {
            Text="ShakeFH2 · 扳机震动与键盘映射";
            Font=new Font("Microsoft YaHei UI",9); StartPosition=FormStartPosition.CenterScreen;
            FormBorderStyle=FormBorderStyle.FixedSingle; MaximizeBox=false;
            mode.Text="两个功能独立运行；切换页面只改变显示。实体手柄和键盘可以同时输入。";
            mode.Dock=DockStyle.Bottom; mode.Height=31; mode.TextAlign=ContentAlignment.MiddleLeft; mode.Padding=new Padding(12,0,0,0); mode.ForeColor=Color.DimGray;
            tabs.Dock=DockStyle.Fill;
            TabPage vibration=new TabPage("手柄震动"),keyboard=new TabPage("键盘映射");
            tabs.TabPages.Add(vibration); tabs.TabPages.Add(keyboard);
            Embed(vibration,Vibration); Embed(keyboard,Keyboard);
            Controls.Add(tabs); Controls.Add(mode);
            tabs.SelectedIndexChanged+=delegate { SwitchServices(); };
            Shown+=delegate { SwitchServices(); };
            FormClosing+=delegate(object sender,FormClosingEventArgs e)
            {
                try { Keyboard.StopServices(); Vibration.StopServices(); }
                catch(Exception exception) { e.Cancel=true; MessageBox.Show(this,exception.Message,"停止功能失败"); }
            };
            FormClosed+=delegate { Keyboard.Dispose(); Vibration.Dispose(); };
            ResizeForPage(0);
        }
        static void Embed(TabPage page,Form child)
        {
            child.TopLevel=false; child.FormBorderStyle=FormBorderStyle.None; child.Dock=DockStyle.Fill;
            page.Controls.Add(child); child.Show();
        }
        void ResizeForPage(int index)
        {
            ClientSize=index==0?new Size(630,438):new Size(870,838);
        }
        void SwitchServices()
        {
            int selected=tabs.SelectedIndex; if(selected==active) return;
            try
            {
                ResizeForPage(selected);
                Vibration.StartServices(); Keyboard.StartServices();
                active=selected;
                mode.Text=selected==0?"手柄震动与键盘映射独立运行；可同时输入。":"键盘与实体手柄合并输入；F8 暂停键盘映射，实体手柄仍可操作。";
            }
            catch(Exception exception)
            {
                mode.Text="模式切换失败："+exception.Message;
                MessageBox.Show(this,exception.Message,"模式切换失败");
            }
        }
        internal void SelectPage(int index) { tabs.SelectedIndex=index; }
    }

    internal static class Program
    {
        static readonly string[] MutexNames={"Local\\ShakeFH2-Combined-v2","Local\\FH2XboxTriggerBridge-v1","Local\\KeyFH2-KeyboardMapper-v1"};
        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                if(args.Length>0)
                {
                    if(args[0]=="--fixture") return XboxTriggerBridge.Tests.Fixture(args[1]);
                    if(args[0]=="--keyboard-worker") return KeyboardIsolation.RunWorker(args[1],int.Parse(args[2]));
                    if(args[0]=="--self-test") { XboxTriggerBridge.Tests.SelfTest(); KeyFH2.Tests.Run(); KeyFH2.Tests.MappingOnly(); return 0; }
                    if(args[0]=="--keyboard-self-test") { KeyFH2.Tests.Run(); KeyFH2.Tests.MappingOnly(); return 0; }
                    if(args[0]=="--test-modes") { ModeTests.Run(); return 0; }
                    if(args[0]=="--test-mixing") { ModeTests.Mixing(); return 0; }
                    if(args[0]=="--render-ui") { ModeTests.Render(); return 0; }
                    if(args[0]=="--probe") { XboxTriggerBridge.Tests.Probe(false); return 0; }
                }
                List<Mutex> owned=new List<Mutex>();
                try
                {
                    foreach(string name in MutexNames)
                    {
                        bool created; Mutex mutex=new Mutex(true,name,out created);
                        if(!created) { mutex.Dispose(); MessageBox.Show("请先关闭旧版 ShakeFH2、KeyFH2 或已经运行的合并版。","ShakeFH2"); return 1; }
                        owned.Add(mutex);
                    }
                    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new MainWindow()); return 0;
                }
                finally { for(int i=owned.Count-1;i>=0;i--) { owned[i].ReleaseMutex(); owned[i].Dispose(); } }
            }
            catch(Exception exception) { Console.Error.WriteLine(exception); XboxTriggerBridge.Log.Write(exception.ToString()); return 1; }
        }
    }

    internal static class ModeTests
    {
        [DllImport("XboxTriggerOutput.dll",CallingConvention=CallingConvention.StdCall)] static extern uint TestInputMix(ref KeyFH2.Report physical,ref KeyFH2.Report keyboard,out KeyFH2.Report output);
        internal static void Mixing()
        {
            KeyFH2.Report physical=new KeyFH2.Report {Buttons=0x1000,LT=80,RT=90,LX=-12345,RY=23456};
            KeyFH2.Report keyboard=new KeyFH2.Report {Buttons=0x4000,RT=255,LX=32767},output;
            TestInputMix(ref physical,ref keyboard,out output);
            Assert(output.Buttons==0x5000,"physical and keyboard buttons combine");
            Assert(output.LT==80&&output.RT==255,"triggers preserve physical input and take larger values");
            Assert(output.LX==32767&&output.RY==23456,"active keyboard axis takes priority without discarding other physical axes");
            keyboard=new KeyFH2.Report(); TestInputMix(ref physical,ref keyboard,out output);
            Assert(output.Same(physical),"neutral keyboard restores unchanged physical controls");
        }
        static void Assert(bool condition,string label) { if(!condition) throw new Exception("FAILED: "+label); Console.WriteLine("PASS: "+label); }
        static int ConnectedPads()
        {
            int count=0; for(uint i=0;i<4;i++) { KeyFH2.XState state; if(KeyFH2.Native.XInputGetState(i,out state)==0) count++; } return count;
        }
        static void Pump(int milliseconds)
        {
            Stopwatch clock=Stopwatch.StartNew(); while(clock.ElapsedMilliseconds<milliseconds) { Application.DoEvents(); Thread.Sleep(10); }
        }
        static bool WaitConnections(int count)
        {
            for(int i=0;i<40;i++) { if(ConnectedPads()==count) return true; Pump(50); } return false;
        }
        internal static void Run()
        {
            if(Process.GetProcessesByName("azimuth").Length!=0) throw new Exception("Close the game before mode lifecycle tests.");
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            int baseline=ConnectedPads();
            using(MainWindow window=new MainWindow())
            {
                window.ShowInTaskbar=false; window.StartPosition=FormStartPosition.Manual; window.Location=new Point(-20000,-20000); window.Show(); Pump(150);
                for(int i=0;i<40&&window.Keyboard.PadNumber==0;i++) Pump(100);
                Assert(window.Vibration.ServicesRunning&&window.Keyboard.ServicesRunning,"both services run independently");
                Assert(WaitConnections(baseline+1),"one stable virtual controller created");
                for(int cycle=0;cycle<2;cycle++)
                {
                    window.SelectPage(1);
                    for(int i=0;i<30&&window.Keyboard.PadNumber==0;i++) Pump(100);
                    Assert(window.Keyboard.ServicesRunning&&window.Vibration.ServicesRunning&&window.Keyboard.PadNumber>0,"keyboard page keeps both services active, cycle "+cycle);
                    Assert(WaitConnections(baseline+1),"exactly one virtual controller added, cycle "+cycle);
                    window.SelectPage(0); Pump(150);
                    Assert(window.Vibration.ServicesRunning&&window.Keyboard.ServicesRunning,"vibration page keeps keyboard input active, cycle "+cycle);
                    Assert(WaitConnections(baseline+1),"page switch preserves the same device count, cycle "+cycle);
                }
                window.Close();
                Assert(!window.Keyboard.ServicesRunning&&!window.Vibration.ServicesRunning,"closing combined window stops both modules");
                Assert(WaitConnections(baseline),"closing removes the virtual controller");
            }
            Console.WriteLine("All mode lifecycle tests passed.");
        }
        internal static void Render()
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            using(MainWindow window=new MainWindow())
            {
                window.ShowInTaskbar=false; window.StartPosition=FormStartPosition.Manual; window.Location=new Point(-20000,-20000); window.Show(); Pump(200);
                Save(window,"vibration-preview.png"); window.SelectPage(1); Pump(200); Save(window,"keyboard-preview.png"); window.Close();
            }
        }
        static void Save(Form window,string name)
        {
            using(Bitmap bitmap=new Bitmap(window.Width,window.Height))
            {
                window.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));
                bitmap.Save(Path.Combine(XboxTriggerBridge.Log.Folder,name),System.Drawing.Imaging.ImageFormat.Png);
            }
        }
    }
}
