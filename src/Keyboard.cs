using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace KeyFH2
{
    [StructLayout(LayoutKind.Sequential)]
    struct Report
    {
        public ushort Buttons; public byte LT, RT; public short LX, LY, RX, RY;
        public bool Same(Report r) { return Buttons==r.Buttons && LT==r.LT && RT==r.RT && LX==r.LX && LY==r.LY && RX==r.RX && RY==r.RY; }
    }
    [StructLayout(LayoutKind.Sequential)]
    struct XState { public uint Packet; public Report Gamepad; }

    static class Native
    {
        [DllImport("ViGEmClient.dll", CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr vigem_alloc();
        [DllImport("ViGEmClient.dll", CallingConvention=CallingConvention.Cdecl)] public static extern void vigem_free(IntPtr client);
        [DllImport("ViGEmClient.dll", CallingConvention=CallingConvention.Cdecl)] public static extern uint vigem_connect(IntPtr client);
        [DllImport("ViGEmClient.dll", CallingConvention=CallingConvention.Cdecl)] public static extern void vigem_disconnect(IntPtr client);
        [DllImport("ViGEmClient.dll", CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr vigem_target_x360_alloc();
        [DllImport("ViGEmClient.dll", CallingConvention=CallingConvention.Cdecl)] public static extern void vigem_target_free(IntPtr target);
        [DllImport("ViGEmClient.dll", CallingConvention=CallingConvention.Cdecl)] public static extern uint vigem_target_add(IntPtr client, IntPtr target);
        [DllImport("ViGEmClient.dll", CallingConvention=CallingConvention.Cdecl)] public static extern uint vigem_target_remove(IntPtr client, IntPtr target);
        [DllImport("ViGEmClient.dll", CallingConvention=CallingConvention.Cdecl)] public static extern uint vigem_target_x360_update(IntPtr client, IntPtr target, Report report);
        [DllImport("ViGEmClient.dll", CallingConvention=CallingConvention.Cdecl)] public static extern uint vigem_target_x360_get_user_index(IntPtr client, IntPtr target, out uint index);
        [DllImport("xinput1_4.dll")] public static extern uint XInputGetState(uint index, out XState state);
        [DllImport("xinput1_4.dll", EntryPoint="#108")] public static extern uint XInputGetCapabilitiesEx(uint reserved,uint index,uint flags,IntPtr capabilities);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint period);
        [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint period);
        public static bool Down(int key) { return key>0 && key<256 && (GetAsyncKeyState(key)&0x8000)!=0; }
        public static void Check(uint code)
        {
            if (code==0x20000000) return;
            string reason=code==0xE0000001 ? "没有找到 ViGEmBus 驱动。" : code==0xE0000002 ? "手柄槽位已满，请断开其他手柄。" : "虚拟手柄接口错误。";
            throw new InvalidOperationException(reason+" (0x"+code.ToString("X8")+")");
        }
    }

    sealed class VirtualPad : IDisposable
    {
        IntPtr client, target; bool added; public uint Index;
        public VirtualPad()
        {
            try
            {
                FH2Tools.NativeLibraries.EnsureViGEmLoaded();
                client=Native.vigem_alloc(); if (client==IntPtr.Zero) throw new Exception("无法分配虚拟手柄客户端。");
                Native.Check(Native.vigem_connect(client));
                target=Native.vigem_target_x360_alloc(); if (target==IntPtr.Zero) throw new Exception("无法分配虚拟 Xbox 手柄。");
                Native.Check(Native.vigem_target_add(client,target)); added=true;
                Native.Check(Native.vigem_target_x360_get_user_index(client,target,out Index)); Send(new Report());
            }
            catch { Dispose(); throw; }
        }
        public void Send(Report report)
        {
            Native.Check(Native.vigem_target_x360_update(client,target,report));
            uint current;
            Native.Check(Native.vigem_target_x360_get_user_index(client,target,out current));
            Index=current;
            IntPtr capabilities=Marshal.AllocHGlobal(64);
            try
            {
                for(uint slot=0;slot<4;slot++)
                    if(Native.XInputGetCapabilitiesEx(1,slot,0,capabilities)==0 &&
                        (ushort)Marshal.ReadInt16(capabilities,20)==0x045e &&
                        (ushort)Marshal.ReadInt16(capabilities,22)==0x028e)
                    { Index=slot; break; }
            }
            catch(EntryPointNotFoundException) { }
            finally { Marshal.FreeHGlobal(capabilities); }
        }
        public void Dispose()
        {
            if (added) { try { Send(new Report()); } catch {} Native.vigem_target_remove(client,target); added=false; }
            if (target!=IntPtr.Zero) { Native.vigem_target_free(target); target=IntPtr.Zero; }
            if (client!=IntPtr.Zero) { Native.vigem_disconnect(client); Native.vigem_free(client); client=IntPtr.Zero; }
        }
    }

    enum ActionId { RT,LT,LXLeft,LXRight,LYUp,LYDown,RXLeft,RXRight,RYUp,RYDown,A,B,X,Y,LB,RB,LClick,RClick,Back,Start,Guide,DUp,DDown,DLeft,DRight }
    sealed class Binding
    {
        public ActionId Action; public int Primary, Alternate;
        public Binding(ActionId id, int primary, int alternate) { Action=id; Primary=primary; Alternate=alternate; }
        public Binding Copy() { return new Binding(Action,Primary,Alternate); }
    }
    sealed class Configuration
    {
        public List<Binding> Bindings=new List<Binding>();
        public int SteerMs=150, CenterMs=90, TriggerMs=0, SteerLimit=100;
        public bool ForegroundOnly=true,ExtraControls;
        public string Preset="地平线 4 常用驾驶键位";
        public Configuration Copy()
        {
            Configuration c=new Configuration {SteerMs=SteerMs,CenterMs=CenterMs,TriggerMs=TriggerMs,SteerLimit=SteerLimit,ForegroundOnly=ForegroundOnly,ExtraControls=ExtraControls,Preset=Preset};
            foreach (Binding b in Bindings) c.Bindings.Add(b.Copy()); return c;
        }
        public Binding Get(ActionId id) { return Bindings.Find(delegate(Binding b) { return b.Action==id; }); }
        static void Set(Configuration c, ActionId id, Keys primary, Keys alternate) { c.Get(id).Primary=(int)primary; c.Get(id).Alternate=(int)alternate; }
        public static Configuration Default(int preset)
        {
            Configuration c=new Configuration(); foreach (ActionId id in Enum.GetValues(typeof(ActionId))) c.Bindings.Add(new Binding(id,0,0));
            if (preset==2) { c.Preset="空白预设"; return c; }
            c.Preset=preset==1 ? "方向键驾驶" : "地平线 4 常用驾驶键位";
            Set(c,ActionId.RT,preset==1?Keys.Up:Keys.W,Keys.None); Set(c,ActionId.LT,preset==1?Keys.Down:Keys.S,Keys.None);
            Set(c,ActionId.LXLeft,preset==1?Keys.Left:Keys.A,Keys.None); Set(c,ActionId.LXRight,preset==1?Keys.Right:Keys.D,Keys.None);
            Set(c,ActionId.DUp,preset==1?Keys.W:Keys.Up,Keys.None); Set(c,ActionId.DDown,preset==1?Keys.S:Keys.Down,Keys.None);
            Set(c,ActionId.DLeft,preset==1?Keys.A:Keys.Left,Keys.None); Set(c,ActionId.DRight,preset==1?Keys.D:Keys.Right,Keys.None);
            Set(c,ActionId.A,Keys.Space,Keys.Enter); Set(c,ActionId.B,Keys.E,Keys.Back);
            Set(c,ActionId.X,Keys.Q,Keys.None); Set(c,ActionId.Y,Keys.R,Keys.None);
            Set(c,ActionId.LB,Keys.LShiftKey,Keys.RShiftKey); Set(c,ActionId.RB,Keys.C,Keys.None);
            Set(c,ActionId.Back,Keys.M,Keys.None); Set(c,ActionId.Start,Keys.Escape,Keys.None);
            Set(c,ActionId.RXLeft,Keys.NumPad4,Keys.None); Set(c,ActionId.RXRight,Keys.NumPad6,Keys.None);
            Set(c,ActionId.RYUp,Keys.NumPad8,Keys.None); Set(c,ActionId.RYDown,Keys.NumPad2,Keys.None);
            return c;
        }
        public static string Label(ActionId id)
        {
            switch(id)
            {
                case ActionId.RT:return "RT · 油门"; case ActionId.LT:return "LT · 刹车 / 倒车";
                case ActionId.LXLeft:return "左摇杆 ← · 左转"; case ActionId.LXRight:return "左摇杆 → · 右转";
                case ActionId.LYUp:return "左摇杆 ↑"; case ActionId.LYDown:return "左摇杆 ↓";
                case ActionId.RXLeft:return "右摇杆 ← · 观察"; case ActionId.RXRight:return "右摇杆 → · 观察";
                case ActionId.RYUp:return "右摇杆 ↑ · 观察"; case ActionId.RYDown:return "右摇杆 ↓ · 观察";
                case ActionId.A:return "A · 手刹 / 菜单确认"; case ActionId.B:return "B · 菜单返回 / 升挡（手动挡）";
                case ActionId.X:return "X · 执行动作 / 降挡（手动挡）"; case ActionId.Y:return "Y · 回放";
                case ActionId.LB:return "LB · 离合（手动离合）"; case ActionId.RB:return "RB · 切换视角";
                case ActionId.LClick:return "左摇杆按下（通用扩展控件）"; case ActionId.RClick:return "右摇杆按下（通用扩展控件）";
                case ActionId.Back:return "View / Back · 地图"; case ActionId.Start:return "Menu / Start · 暂停";
                case ActionId.Guide:return "Guide · Xbox 键（默认不绑定）";
                case ActionId.DUp:return "十字键 ↑ · 相片模式（长按）/ 菜单"; case ActionId.DDown:return "十字键 ↓ · 遥测（可长按）/ 菜单";
                case ActionId.DLeft:return "十字键 ← · 广播 / 菜单"; case ActionId.DRight:return "十字键 → · 排行榜 / 菜单";
                default:return id.ToString();
            }
        }
        public static string KeyLabel(int key)
        {
            if (key==0) return "未绑定"; if (key==(int)Keys.Back) return "Backspace"; if (key==(int)Keys.Return) return "Enter";
            if (key==(int)Keys.LShiftKey) return "左 Shift"; if (key==(int)Keys.RShiftKey) return "右 Shift";
            if (key==(int)Keys.LControlKey) return "左 Ctrl"; if (key==(int)Keys.RControlKey) return "右 Ctrl";
            if (key==(int)Keys.LMenu) return "左 Alt"; if (key==(int)Keys.RMenu) return "右 Alt";
            return ((Keys)key).ToString();
        }
        public static bool IsExtra(ActionId id)
        {
            return id==ActionId.LYUp || id==ActionId.LYDown || id==ActionId.LClick || id==ActionId.RClick || id==ActionId.Guide;
        }
        public static Configuration Load(string file)
        {
            Configuration c=Default(0); if (!File.Exists(file)) return c; bool hasExtraSetting=false;
            foreach(string line in File.ReadAllLines(file,Encoding.UTF8))
            {
                int separator=line.IndexOf('='); if(separator<0 || line.StartsWith("#")) continue;
                string key=line.Substring(0,separator),value=line.Substring(separator+1); int n;
                if(key=="Preset") { c.Preset=value; continue; } if(!int.TryParse(value,out n)) continue;
                if(key=="SteerMs") c.SteerMs=Math.Max(0,Math.Min(1000,n));
                else if(key=="CenterMs") c.CenterMs=Math.Max(0,Math.Min(1000,n));
                else if(key=="TriggerMs") c.TriggerMs=Math.Max(0,Math.Min(1000,n));
                else if(key=="SteerLimit") c.SteerLimit=Math.Max(10,Math.Min(100,n));
                else if(key=="ForegroundOnly") c.ForegroundOnly=n!=0;
                else if(key=="ExtraControls") { c.ExtraControls=false; hasExtraSetting=true; }
                else
                {
                    string[] parts=key.Split('.'); ActionId id;
                    if(parts.Length!=2 || !Enum.TryParse<ActionId>(parts[0],out id) || c.Get(id)==null || n<0 || n>255 || n==(int)Keys.F8) continue;
                    if(parts[1]=="Primary") c.Get(id).Primary=n; if(parts[1]=="Alternate") c.Get(id).Alternate=n;
                }
            }
            if(!hasExtraSetting)
            {
                if(c.Get(ActionId.LClick).Primary==(int)Keys.H && c.Get(ActionId.LClick).Alternate==0) c.Get(ActionId.LClick).Primary=0;
                if(c.Get(ActionId.RClick).Primary==(int)Keys.V && c.Get(ActionId.RClick).Alternate==0) c.Get(ActionId.RClick).Primary=0;
            }
            return c;
        }
        public void Save(string file)
        {
            StringBuilder text=new StringBuilder("# KeyFH2 virtual-key bindings. F8 is reserved for pause.\r\n");
            text.AppendLine("Preset="+Preset); text.AppendLine("SteerMs="+SteerMs); text.AppendLine("CenterMs="+CenterMs);
            text.AppendLine("TriggerMs="+TriggerMs); text.AppendLine("SteerLimit="+SteerLimit); text.AppendLine("ForegroundOnly="+(ForegroundOnly?1:0));
            text.AppendLine("ExtraControls="+(ExtraControls?1:0));
            foreach(Binding b in Bindings) { text.AppendLine(b.Action+".Primary="+b.Primary); text.AppendLine(b.Action+".Alternate="+b.Alternate); }
            string temporary=file+".tmp"; File.WriteAllText(temporary,text.ToString(),new UTF8Encoding(false));
            if(File.Exists(file)) File.Replace(temporary,file,null); else File.Move(temporary,file);
        }
    }

    sealed class ReportEngine
    {
        double lx,ly,lt,rt;
        public void Reset() { lx=ly=lt=rt=0; }
        static double Move(double value,double target,double dt,int ms)
        {
            if(ms==0) return target; double step=dt*1000/ms;
            return value<target ? Math.Min(target,value+step) : Math.Max(target,value-step);
        }
        static short Axis(double value) { return (short)Math.Round(Math.Max(-1,Math.Min(1,value))*32767); }
        static bool Pressed(Configuration c,ActionId id,bool[] keys) { if(Configuration.IsExtra(id)&&!c.ExtraControls) return false; Binding b=c.Get(id); return (b.Primary>0&&keys[b.Primary]) || (b.Alternate>0&&keys[b.Alternate]); }
        static readonly ActionId[] ButtonIds={ActionId.DUp,ActionId.DDown,ActionId.DLeft,ActionId.DRight,ActionId.Start,ActionId.Back,ActionId.LClick,ActionId.RClick,ActionId.LB,ActionId.RB,ActionId.Guide,ActionId.A,ActionId.B,ActionId.X,ActionId.Y};
        static readonly ushort[] ButtonMasks={1,2,4,8,0x10,0x20,0x40,0x80,0x100,0x200,0x400,0x1000,0x2000,0x4000,0x8000};
        public Report Build(Configuration c,bool[] keys,double dt)
        {
            dt=Math.Max(0,Math.Min(0.05,dt));
            double tx=(Pressed(c,ActionId.LXRight,keys)?1:0)-(Pressed(c,ActionId.LXLeft,keys)?1:0);
            double ty=(Pressed(c,ActionId.LYUp,keys)?1:0)-(Pressed(c,ActionId.LYDown,keys)?1:0);
            lx=Move(lx,tx,dt,tx==0?c.CenterMs:c.SteerMs); ly=Move(ly,ty,dt,ty==0?c.CenterMs:c.SteerMs);
            lt=Move(lt,Pressed(c,ActionId.LT,keys)?1:0,dt,c.TriggerMs); rt=Move(rt,Pressed(c,ActionId.RT,keys)?1:0,dt,c.TriggerMs);
            double sx=lx*c.SteerLimit/100.0,sy=ly,length=Math.Sqrt(sx*sx+sy*sy);
            if(length>1) { sx/=length; sy/=length; }
            double rx=(Pressed(c,ActionId.RXRight,keys)?1:0)-(Pressed(c,ActionId.RXLeft,keys)?1:0);
            double ry=(Pressed(c,ActionId.RYUp,keys)?1:0)-(Pressed(c,ActionId.RYDown,keys)?1:0);
            length=Math.Sqrt(rx*rx+ry*ry); if(length>1) { rx/=length; ry/=length; }
            Report report=new Report {LX=Axis(sx),LY=Axis(sy),RX=Axis(rx),RY=Axis(ry),LT=(byte)Math.Round(lt*255),RT=(byte)Math.Round(rt*255)};
            for(int i=0;i<ButtonIds.Length;i++) if(Pressed(c,ButtonIds[i],keys)) report.Buttons|=ButtonMasks[i];
            if((report.Buttons&3)==3) report.Buttons&=unchecked((ushort)~3);
            if((report.Buttons&12)==12) report.Buttons&=unchecked((ushort)~12);
            return report;
        }
    }

    sealed class Mapper : IDisposable
    {
        readonly object gate=new object(); Configuration configuration; Thread thread; volatile bool stop;
        volatile bool enabled=true;
        long enabledRevision;
        readonly object reportGate=new object();
        Report lastOutput;
        public Report LastOutput { get { lock(reportGate) return lastOutput; } }
        public bool Enabled { get { return enabled; } set { if(enabled!=value) { enabled=value; Interlocked.Increment(ref enabledRevision); } } }
        public volatile bool Capturing; public volatile int PadNumber;
        public string Status="正在连接虚拟 Xbox 手柄…",Live="油门 0%  刹车 0%  转向 0%";
        public Mapper(Configuration c) { configuration=c.Copy(); }
        public void SetConfiguration(Configuration c) { lock(gate) configuration=c.Copy(); }
        public void Start() { stop=false; thread=new Thread(Run); thread.IsBackground=true; thread.Start(); }
        void Run()
        {
            if(!FH2Tools.KeyboardIsolation.IsWorker)
            {
                FH2Tools.KeyboardIsolation.RunParent(
                    delegate { Configuration c; lock(gate) c=configuration; return FH2Tools.KeyboardCommand.From(c,Enabled,Interlocked.Read(ref enabledRevision),Capturing); },
                    delegate(FH2Tools.KeyboardReply reply) { enabled=reply.Enabled; PadNumber=reply.PadNumber; Status=reply.Status; Live=reply.Live; },
                    delegate { return stop; });
                PadNumber=0; return;
            }
            Native.timeBeginPeriod(1); VirtualPad pad=null; ReportEngine engine=new ReportEngine();
            Stopwatch clock=Stopwatch.StartNew(); double lastTime=clock.Elapsed.TotalSeconds;
            int ownPid=FH2Tools.KeyboardIsolation.HostPid; uint cachedPid=0; bool gameForeground=false; long lastFocusCheck=-500; bool hotkeyWasDown=false,wasActive=false;
            try
            {
                while(!stop)
                {
                    try
                    {
                        if(pad==null) { pad=new VirtualPad(); PadNumber=(int)pad.Index+1; }
                        Configuration c; lock(gate) c=configuration;
                        uint foregroundPid; Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out foregroundPid);
                        bool ownForeground=foregroundPid==ownPid;
                        if(foregroundPid!=cachedPid || clock.ElapsedMilliseconds-lastFocusCheck>300)
                        {
                            cachedPid=foregroundPid; lastFocusCheck=clock.ElapsedMilliseconds; gameForeground=false;
                            if(foregroundPid!=0) { try { using(Process p=Process.GetProcessById((int)foregroundPid)) gameForeground=string.Equals(p.ProcessName,"azimuth",StringComparison.OrdinalIgnoreCase); } catch {} }
                        }
                        bool hotkeyDown=!Capturing&&(gameForeground||ownForeground)&&Native.Down((int)Keys.F8);
                        if(hotkeyDown&&!hotkeyWasDown) Enabled=!Enabled; hotkeyWasDown=hotkeyDown;
                        bool active=Enabled&&!Capturing&&(gameForeground||(!c.ForegroundOnly&&!ownForeground));
                        if(active!=wasActive) engine.Reset();
                        wasActive=active;
                        double now=clock.Elapsed.TotalSeconds,dt=now-lastTime; lastTime=now;
                        Report input=new Report(),output=new Report();
                        if(active)
                        {
                            bool[] keys=new bool[256];
                            foreach(Binding b in c.Bindings) { if(b.Primary>0) keys[b.Primary]=Native.Down(b.Primary); if(b.Alternate>0) keys[b.Alternate]=Native.Down(b.Alternate); }
                            input=engine.Build(c,keys,dt); if(active) output=input;
                        }
                        else engine.Reset();
                        pad.Send(output);
                        PadNumber=(int)pad.Index+1;
                        lock(reportGate) lastOutput=output;
                        Status=Capturing?"修改绑定中；虚拟输入已释放":!Enabled?"已暂停；虚拟输入已释放（F8 恢复）":active?"映射生效：键盘 → 虚拟 Xbox 手柄":"已就绪；等待游戏处于前台";
                        Live=string.Format("油门 {0:0}%  刹车 {1:0}%  转向 {2:0}%  按钮 0x{3:X4}",input.RT*100.0/255,input.LT*100.0/255,input.LX*100.0/32767,input.Buttons);
                        Thread.Sleep(8);
                    }
                    catch(Exception exception)
                    {
                        if(pad!=null) { pad.Dispose(); pad=null; } PadNumber=0; engine.Reset();
                        Status=exception is DllNotFoundException?"缺少 ViGEmClient.dll，请保持它与 EXE 在同一文件夹。":exception.Message;
                        Live="输入已释放；正在重试连接"; Thread.Sleep(1000);
                    }
                }
            }
            finally { if(pad!=null) pad.Dispose(); Native.timeEndPeriod(1); }
        }
        public void Dispose() { stop=true; if(thread!=null&&thread.IsAlive&&!thread.Join(5000)) throw new InvalidOperationException("键盘输入线程尚未停止，请稍后重试。"); }
    }

    sealed class CaptureWindow : Form
    {
        public int Key; readonly Label prompt;
        public CaptureWindow(string action)
        {
            Text="绑定键盘按键"; ClientSize=new Size(430,155); StartPosition=FormStartPosition.CenterParent;
            FormBorderStyle=FormBorderStyle.FixedDialog; MaximizeBox=false; MinimizeBox=false; Font=new Font("Microsoft YaHei UI",10);
            prompt=new Label {Text=action+"\r\n请按一个键。Esc 也可绑定；F8 用于启用 / 暂停。",Location=new Point(20,20),Size=new Size(390,75)};
            Button none=new Button {Text="清除此绑定",Location=new Point(20,105),Size=new Size(140,32)};
            none.Click+=delegate { Key=0; DialogResult=DialogResult.OK; Close(); };
            Button cancel=new Button {Text="取消",Location=new Point(270,105),Size=new Size(140,32),DialogResult=DialogResult.Cancel};
            Controls.AddRange(new Control[] {prompt,none,cancel});
        }
        protected override bool ProcessCmdKey(ref Message message,Keys keyData)
        {
            Keys key=keyData&Keys.KeyCode;
            if(key==Keys.F8) { prompt.Text="F8 已用于启用 / 暂停，请选择其他键。"; return true; }
            if(key==Keys.ShiftKey) key=Native.Down((int)Keys.RShiftKey)?Keys.RShiftKey:Keys.LShiftKey;
            if(key==Keys.ControlKey) key=Native.Down((int)Keys.RControlKey)?Keys.RControlKey:Keys.LControlKey;
            if(key==Keys.Menu) key=Native.Down((int)Keys.RMenu)?Keys.RMenu:Keys.LMenu;
            if((int)key>0&&(int)key<256) { Key=(int)key; DialogResult=DialogResult.OK; Close(); return true; }
            return base.ProcessCmdKey(ref message,keyData);
        }
    }

    sealed class Window : Form
    {
        readonly bool hosted;
        System.Windows.Forms.Timer refreshTimer;
        public bool ServicesRunning { get; private set; }
        public int PadNumber { get { return mapper.PadNumber; } }
        public static readonly string Folder=Path.GetDirectoryName(typeof(Window).Assembly.Location);
        readonly string configPath=Path.Combine(Folder,"KeyFH2.ini"); Configuration configuration; readonly Mapper mapper;
        readonly DataGridView grid=new DataGridView(); readonly Label status=new Label(),live=new Label();
        readonly CheckBox enable=new CheckBox(),foreground=new CheckBox(); readonly ComboBox presets=new ComboBox();
        readonly NumericUpDown steer=new NumericUpDown(),center=new NumericUpDown(),trigger=new NumericUpDown(),limit=new NumericUpDown(); bool loading;
        public Window(bool hosted=false)
        {
            this.hosted=hosted;
            try { configuration=Configuration.Load(configPath); } catch { configuration=Configuration.Default(0); }
            mapper=new Mapper(configuration);
            Text="键盘映射 · Xbox 手柄"; ClientSize=new Size(860,732); StartPosition=FormStartPosition.CenterScreen;
            FormBorderStyle=FormBorderStyle.FixedSingle; MaximizeBox=false; Font=new Font("Microsoft YaHei UI",9);
            Label title=new Label {Text="键盘映射 · Xbox 手柄",Location=new Point(20,15),AutoSize=true,Font=new Font(Font.FontFamily,12,FontStyle.Bold)};
            status.SetBounds(20,48,820,25); status.Text=mapper.Status;
            enable.Text="启用映射（F8）"; enable.Checked=true; enable.AutoSize=true; enable.Location=new Point(20,86);
            enable.CheckedChanged+=delegate { if(!loading) mapper.Enabled=enable.Checked; };
            foreground.Text="仅游戏在前台时生效"; foreground.AutoSize=true; foreground.Location=new Point(210,86); foreground.CheckedChanged+=delegate { Apply(); };
            Label presetLabel=new Label {Text="预设",Location=new Point(20,126),AutoSize=true};
            presets.SetBounds(65,121,290,28); presets.DropDownStyle=ComboBoxStyle.DropDownList;
            presets.Items.AddRange(new object[] {"地平线 4 常用驾驶键位","方向键驾驶","空白预设"}); presets.SelectedIndex=0;
            Button load=new Button {Text="应用预设",Location=new Point(375,119),Size=new Size(105,30)};
            load.Click+=delegate { configuration=Configuration.Default(presets.SelectedIndex); Populate(); Apply(); };
            Button save=new Button {Text="保存配置",Location=new Point(495,119),Size=new Size(105,30)}; save.Click+=delegate { Apply(); };
            Label caption=new Label {Text="默认对应游戏配置 1；双击修改绑定。手动换挡 / 离合需启用相应驾驶辅助。",Location=new Point(20,161),AutoSize=true,ForeColor=Color.DimGray};
            grid.SetBounds(20,188,820,345); grid.AllowUserToAddRows=false; grid.AllowUserToDeleteRows=false; grid.RowHeadersVisible=false;
            grid.MultiSelect=false; grid.SelectionMode=DataGridViewSelectionMode.CellSelect; grid.ReadOnly=true; grid.BackgroundColor=Color.White;
            grid.Columns.Add("action","Xbox 功能"); grid.Columns.Add("primary","主键"); grid.Columns.Add("alternate","备用键");
            grid.Columns[0].Width=420; grid.Columns[1].Width=180; grid.Columns[2].AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill;
            foreach(DataGridViewColumn column in grid.Columns) column.SortMode=DataGridViewColumnSortMode.NotSortable;
            grid.CellDoubleClick+=delegate(object sender,DataGridViewCellEventArgs e) { if(e.RowIndex>=0&&e.ColumnIndex>0) CaptureBinding(e.RowIndex,e.ColumnIndex); };
            Button primary=new Button {Text="修改主键",Location=new Point(20,546),Size=new Size(120,31)}; primary.Click+=delegate { if(grid.CurrentCell!=null) CaptureBinding(grid.CurrentCell.RowIndex,1); };
            Button alternate=new Button {Text="修改备用键",Location=new Point(155,546),Size=new Size(120,31)}; alternate.Click+=delegate { if(grid.CurrentCell!=null) CaptureBinding(grid.CurrentCell.RowIndex,2); };
            Controls.AddRange(new Control[] {title,status,enable,foreground,presetLabel,presets,load,save,caption,grid,primary,alternate});
            AddNumber("转向达到满幅 ms",steer,20,597,0,1000); AddNumber("回正 ms",center,265,597,0,1000);
            AddNumber("扳机渐进 ms",trigger,445,597,0,1000); AddNumber("最大转向 %",limit,640,597,10,100);
            Label numberHint=new Label {Text="0 ms 表示立即响应。转向参数可自行调节。",Location=new Point(20,636),AutoSize=true,ForeColor=Color.DimGray};
            live.SetBounds(20,664,610,27);
            Label author=new Label {Text="作者：栗尾玲子KurioReiko",Location=new Point(20,701),Size=new Size(820,23),TextAlign=ContentAlignment.MiddleRight,Font=new Font(Font,FontStyle.Bold)};
            Controls.AddRange(new Control[] {numberHint,live,author}); Populate();
            System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer {Interval=100}; refreshTimer=timer;
            timer.Tick+=delegate
            {
                status.Text=mapper.Status; live.Text=mapper.Live;
                if(enable.Checked!=mapper.Enabled) { loading=true; enable.Checked=mapper.Enabled; loading=false; }
            };
            Shown+=delegate { if(!this.hosted) StartServices(); };
            FormClosed+=delegate { StopServices(); timer.Dispose(); };
        }
        public void StartServices() { if(ServicesRunning) return; mapper.Start(); refreshTimer.Start(); ServicesRunning=true; }
        public void StopServices()
        {
            if(!ServicesRunning) return;
            refreshTimer.Stop(); mapper.Dispose(); ServicesRunning=false;
            status.Text="键盘模式未运行；虚拟手柄已移除"; live.Text="输出已释放";
        }
        void AddNumber(string caption,NumericUpDown number,int x,int y,int minimum,int maximum)
        {
            Label label=new Label {Text=caption,Location=new Point(x,y+3),AutoSize=true}; int width=TextRenderer.MeasureText(caption,Font).Width;
            number.SetBounds(x+width+8,y,65,27); number.Minimum=minimum; number.Maximum=maximum; number.ValueChanged+=delegate { Apply(); };
            Controls.Add(label); Controls.Add(number);
        }
        void Populate()
        {
            loading=true;
            try
            {
                grid.Rows.Clear(); foreach(Binding b in configuration.Bindings)
                {
                    if(Configuration.IsExtra(b.Action)) continue;
                    int row=grid.Rows.Add(Configuration.Label(b.Action),Configuration.KeyLabel(b.Primary),Configuration.KeyLabel(b.Alternate)); grid.Rows[row].Tag=b.Action;
                }
                steer.Value=configuration.SteerMs; center.Value=configuration.CenterMs; trigger.Value=configuration.TriggerMs; limit.Value=configuration.SteerLimit; foreground.Checked=configuration.ForegroundOnly;
            }
            finally { loading=false; }
        }
        void Apply()
        {
            if(loading) return;
            configuration.SteerMs=(int)steer.Value; configuration.CenterMs=(int)center.Value; configuration.TriggerMs=(int)trigger.Value;
            configuration.SteerLimit=(int)limit.Value; configuration.ForegroundOnly=foreground.Checked; configuration.ExtraControls=false; mapper.SetConfiguration(configuration);
            try { configuration.Save(configPath); } catch(Exception exception) { MessageBox.Show(this,exception.Message,"配置保存失败"); }
        }
        void CaptureBinding(int row,int column)
        {
            if(row<0||row>=grid.Rows.Count) return; mapper.Capturing=true;
            try
            {
                Binding b=configuration.Get((ActionId)grid.Rows[row].Tag);
                using(CaptureWindow dialog=new CaptureWindow(Configuration.Label(b.Action)))
                {
                    if(dialog.ShowDialog(hosted&&Parent!=null?Parent.FindForm():this)!=DialogResult.OK) return;
                    if(column==1) b.Primary=dialog.Key; else b.Alternate=dialog.Key;
                    configuration.Preset="自定义"; grid.Rows[row].Cells[column].Value=Configuration.KeyLabel(dialog.Key); Apply();
                }
            }
            finally { mapper.Capturing=false; }
        }
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                if(args.Length>0&&args[0]=="--self-test") { Tests.Run(); return 0; }
                if(args.Length>0&&args[0]=="--test-bindings") { Tests.MappingOnly(); return 0; }
                if(args.Length>0&&args[0]=="--render-ui")
                {
                    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                    using(Window window=new Window())
                    using(Bitmap bitmap=new Bitmap(window.Width,window.Height))
                    {
                        window.ShowInTaskbar=false; window.StartPosition=FormStartPosition.Manual; window.Location=new Point(-20000,-20000);
                        window.Show(); Application.DoEvents();
                        window.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));
                        bitmap.Save(Path.Combine(Window.Folder,"preview.png"),System.Drawing.Imaging.ImageFormat.Png);
                        window.Close();
                    }
                    return 0;
                }
                bool created;
                using(Mutex mutex=new Mutex(true,"Local\\KeyFH2-KeyboardMapper-v1",out created))
                {
                    if(!created) { MessageBox.Show("KeyFH2 已经在运行。","KeyFH2"); return 1; }
                    try { Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new Window()); }
                    finally { mutex.ReleaseMutex(); }
                }
                return 0;
            }
            catch(Exception exception) { Console.Error.WriteLine(exception); return 1; }
        }
    }
    static class Tests
    {
        static void Assert(bool ok,string message) { if(!ok) throw new Exception("FAILED: "+message); Console.WriteLine("PASS: "+message); }
        static XState ReadExpected(VirtualPad pad,Report expected)
        {
            XState state=new XState();
            // Device enumeration is asynchronous. Resend just as the live mapper
            // does, and verify the complete report rather than a connected flag.
            for(int i=0;i<100;i++) { pad.Send(expected); if(Native.XInputGetState(pad.Index,out state)==0&&state.Gamepad.Same(expected)) return state; Thread.Sleep(20); }
            throw new Exception("XInput did not acknowledge report: received buttons=0x"+state.Gamepad.Buttons.ToString("X4")+", RT="+state.Gamepad.RT);
        }
        public static void MappingOnly()
        {
            Configuration c=Configuration.Default(0); ReportEngine engine=new ReportEngine(); bool[] keys=new bool[256];
            Assert(c.Get(ActionId.LClick).Primary==0&&c.Get(ActionId.RClick).Primary==0&&!c.ExtraControls,"unused FH4 click bindings absent from default preset");
            c.Get(ActionId.LClick).Primary=(int)Keys.H; c.Get(ActionId.RClick).Primary=(int)Keys.V; c.Get(ActionId.Guide).Primary=222;
            keys[(int)Keys.H]=keys[(int)Keys.V]=keys[222]=true;
            Assert(engine.Build(c,keys,0.01).Buttons==0,"disabled extra controls cannot send hidden inputs");
            keys[(int)Keys.Q]=true;
            Assert((engine.Build(c,keys,0.01).Buttons&0x4000)!=0,"game X action remains mapped while extras are disabled");
            c.ExtraControls=true; keys[(int)Keys.Q]=false;
            Assert((engine.Build(c,keys,0.01).Buttons&0x04c0)==0x04c0,"explicitly enabled extras can be custom-mapped");
            string file=Path.Combine(Window.Folder,"config-test-"+Guid.NewGuid().ToString("N")+".ini");
            try
            {
                File.WriteAllText(file,"Preset=自定义\r\nLClick.Primary=72\r\nRClick.Primary=86\r\nGuide.Primary=222\r\nRT.Primary=84\r\n");
                Configuration migrated=Configuration.Load(file);
                Assert(migrated.Get(ActionId.LClick).Primary==0&&migrated.Get(ActionId.RClick).Primary==0,"legacy H and V defaults cleared");
                Assert(migrated.Get(ActionId.Guide).Primary==222&&migrated.Get(ActionId.RT).Primary==(int)Keys.T,"legacy user customizations preserved");
                migrated.ExtraControls=true; migrated.Get(ActionId.LClick).Primary=(int)Keys.H; migrated.Save(file);
                Configuration saved=Configuration.Load(file);
                Assert(!saved.ExtraControls&&saved.Get(ActionId.LClick).Primary==(int)Keys.H,"legacy extension bindings remain saved but disabled");
            }
            finally { if(File.Exists(file)) File.Delete(file); }
        }
        public static void Run()
        {
            if(Process.GetProcessesByName("azimuth").Length!=0) throw new Exception("Close the game before driver integration tests.");
            Assert(Marshal.SizeOf(typeof(Report))==12&&Marshal.SizeOf(typeof(XState))==16,"XUSB / XInput binary layout");
            Configuration c=Configuration.Default(0); ReportEngine engine=new ReportEngine(); bool[] keys=new bool[256]; keys[(int)Keys.D]=true;
            Report first=engine.Build(c,keys,0.01); Assert(first.LX>0&&first.LX<32767,"digital steering starts progressively");
            for(int i=0;i<30;i++) first=engine.Build(c,keys,0.01); Assert(first.LX==32767,"held steering reaches full analog range");
            keys[(int)Keys.D]=false; first=engine.Build(c,keys,0.01); Assert(first.LX>0&&first.LX<32767,"released steering centers progressively");
            engine.Reset(); keys[(int)Keys.A]=keys[(int)Keys.D]=true; Assert(engine.Build(c,keys,0.01).LX==0,"opposing steering keys cancel");
            c.SteerMs=c.CenterMs=c.TriggerMs=0;
            using(VirtualPad pad=new VirtualPad())
            {
                Console.WriteLine("Virtual XInput player number: "+(pad.Index+1));
                keys=new bool[256]; keys[(int)Keys.W]=keys[(int)Keys.Space]=keys[(int)Keys.Q]=keys[(int)Keys.Escape]=true;
                Report report=engine.Build(c,keys,0.01); XState state=ReadExpected(pad,report);
                Assert(state.Gamepad.Same(report)&&state.Gamepad.RT==255&&(state.Gamepad.Buttons&0x5010)==0x5010,"FH4 preset reaches real XInput API");
                c.Get(ActionId.RT).Primary=(int)Keys.T; engine.Reset(); keys=new bool[256]; keys[(int)Keys.T]=true;
                report=engine.Build(c,keys,0.01); Assert(ReadExpected(pad,report).Gamepad.RT==255,"custom remapping reaches XInput");
                engine.Reset(); Assert(ReadExpected(pad,new Report()).Gamepad.Same(new Report()),"neutral report releases all buttons and axes");
            }
            string file=Path.Combine(Window.Folder,"config-test-"+Guid.NewGuid().ToString("N")+".ini");
            try { c.Save(file); Configuration loaded=Configuration.Load(file); Assert(loaded.Get(ActionId.RT).Primary==(int)Keys.T&&loaded.SteerMs==0,"custom configuration persists across restarts"); }
            finally { if(File.Exists(file)) File.Delete(file); }
            Console.WriteLine("All keyboard mapper tests passed.");
        }
    }
}
