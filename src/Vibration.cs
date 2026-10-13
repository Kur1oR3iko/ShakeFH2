using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace XboxTriggerBridge
{
    internal static class Native
    {
        [DllImport("combase.dll")] internal static extern int RoInitialize(uint type);
        [DllImport("combase.dll")] internal static extern void RoUninitialize();
        [DllImport("combase.dll", CharSet = CharSet.Unicode)] internal static extern int WindowsCreateString(string text, uint length, out IntPtr value);
        [DllImport("combase.dll")] internal static extern int WindowsDeleteString(IntPtr value);
        [DllImport("combase.dll")] internal static extern int RoGetActivationFactory(IntPtr name, ref Guid iid, out IntPtr value);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] bytes, UIntPtr count, out UIntPtr read);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] bytes, UIntPtr count, out UIntPtr written);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, UIntPtr size, uint allocation, uint protection);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool VirtualProtectEx(IntPtr process, IntPtr address, UIntPtr size, uint protection, out uint old);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr OpenThread(uint access, bool inherit, int id);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint SuspendThread(IntPtr thread);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint ResumeThread(IntPtr thread);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool GetThreadContext(IntPtr thread, IntPtr context);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr CreateRemoteThread(IntPtr process, IntPtr attributes, UIntPtr stack, IntPtr start, IntPtr parameter, uint flags, out uint threadId);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("kernel32.dll")] internal static extern ulong GetTickCount64();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)] internal static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool FreeLibrary(IntPtr module);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool GetModuleHandleEx(uint flags, IntPtr address, out IntPtr module);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern uint GetModuleFileName(IntPtr module, StringBuilder filename, int size);
        [DllImport("winmm.dll")] internal static extern uint timeBeginPeriod(uint period);
        [DllImport("winmm.dll")] internal static extern uint timeEndPeriod(uint period);
        internal static void Check(bool ok, string operation) { if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error(), operation); }
        internal static void HResult(int result) { if (result < 0) Marshal.ThrowExceptionForHR(result); }
        internal static T Method<T>(IntPtr obj, int slot) where T : class
        {
            return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * IntPtr.Size), typeof(T)) as T;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Vibration
    {
        internal double LeftMotor, RightMotor, LeftTrigger, RightTrigger;
        internal Vibration(double lm, double rm, double lt, double rt) { LeftMotor = lm; RightMotor = rm; LeftTrigger = lt; RightTrigger = rt; }
    }

    internal sealed class Wgi : IDisposable
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetVector(IntPtr self, out IntPtr vector);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetSize(IntPtr self, out uint count);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetAt(IntPtr self, uint index, out IntPtr gamepad);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int PutVibration(IntPtr self, Vibration vibration);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetVibration(IntPtr self, out Vibration vibration);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FromController(IntPtr self, IntPtr controller, out IntPtr raw);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetWord(IntPtr self, out ushort value);
        IntPtr factory, rawFactory;
        bool initialized;
        internal Wgi()
        {
            int result = Native.RoInitialize(1);
            if (result >= 0) initialized = true;
            else if (result != unchecked((int)0x80010106)) Native.HResult(result);
            IntPtr name;
            string className = "Windows.Gaming.Input.Gamepad";
            Native.HResult(Native.WindowsCreateString(className, (uint)className.Length, out name));
            try { Guid iid = new Guid("8bbce529-d49c-39e9-9560-e47dde96b7c8"); Native.HResult(Native.RoGetActivationFactory(name, ref iid, out factory)); }
            finally { Native.WindowsDeleteString(name); }
            className = "Windows.Gaming.Input.RawGameController";
            Native.HResult(Native.WindowsCreateString(className, (uint)className.Length, out name));
            try { Guid iid = new Guid("eb8d0792-e95a-4b19-afc7-0a59f8bf759e"); Native.HResult(Native.RoGetActivationFactory(name, ref iid, out rawFactory)); }
            finally { Native.WindowsDeleteString(name); }
        }
        IntPtr Vector() { IntPtr vector; Native.HResult(Native.Method<GetVector>(factory, 10)(factory, out vector)); return vector; }
        internal int Count
        {
            get
            {
                IntPtr vector = Vector();
                try { uint count; Native.HResult(Native.Method<GetSize>(vector,7)(vector,out count)); int physical=0; for(uint i=0;i<count;i++) { IntPtr item; Native.HResult(Native.Method<GetAt>(vector,6)(vector,i,out item)); try { if(!IsVirtual(item)) physical++; } finally { Marshal.Release(item); } } return physical; }
                finally { Marshal.Release(vector); }
            }
        }
        bool IsVirtual(IntPtr item)
        {
            IntPtr controller=IntPtr.Zero,raw=IntPtr.Zero;
            try
            {
                Guid iid=new Guid("1baf6522-5f64-42c5-8267-b9fe2215bfbd"); Native.HResult(Marshal.QueryInterface(item,ref iid,out controller));
                Native.HResult(Native.Method<FromController>(rawFactory,11)(rawFactory,controller,out raw));
                if(raw==IntPtr.Zero) return false;
                ushort product,vendor; Native.HResult(Native.Method<GetWord>(raw,9)(raw,out product)); Native.HResult(Native.Method<GetWord>(raw,10)(raw,out vendor));
                return vendor==0x045e&&product==0x028e;
            }
            finally { if(raw!=IntPtr.Zero) Marshal.Release(raw); if(controller!=IntPtr.Zero) Marshal.Release(controller); }
        }
        internal IntPtr Open(int index)
        {
            IntPtr vector = Vector(), obj = IntPtr.Zero;
            try
            {
                uint count; Native.HResult(Native.Method<GetSize>(vector,7)(vector,out count)); int physical=0;
                for(uint i=0;i<count;i++) { Native.HResult(Native.Method<GetAt>(vector,6)(vector,i,out obj)); if(!IsVirtual(obj)&&physical++==index) break; Marshal.Release(obj); obj=IntPtr.Zero; }
                if(obj==IntPtr.Zero) throw new InvalidOperationException("所选实体手柄不可用。");
                Guid iid = new Guid("bc7bb43c-0a69-3903-9e9d-a50f86a45de5"); IntPtr gamepad;
                Native.HResult(Marshal.QueryInterface(obj, ref iid, out gamepad)); return gamepad;
            }
            finally { if (obj != IntPtr.Zero) Marshal.Release(obj); Marshal.Release(vector); }
        }
        internal void Put(IntPtr gamepad, Vibration value) { Native.HResult(Native.Method<PutVibration>(gamepad, 7)(gamepad, value)); }
        internal Vibration Get(IntPtr gamepad) { Vibration value; Native.HResult(Native.Method<GetVibration>(gamepad, 6)(gamepad, out value)); return value; }
        public void Dispose() { if(rawFactory!=IntPtr.Zero) { Marshal.Release(rawFactory); rawFactory=IntPtr.Zero; } if (factory != IntPtr.Zero) { Marshal.Release(factory); factory = IntPtr.Zero; } if (initialized) { Native.RoUninitialize(); initialized = false; } }
    }

    internal sealed class ThreadPause : IDisposable
    {
        readonly List<IntPtr> threads = new List<IntPtr>();
        internal ThreadPause(Process process, long patch, int length)
        {
            try
            {
                process.Refresh();
                foreach (ProcessThread thread in process.Threads)
                {
                    IntPtr handle = Native.OpenThread(0x0002 | 0x0008 | 0x0040, false, thread.Id);
                    Native.Check(handle != IntPtr.Zero, "OpenThread");
                    if (Native.SuspendThread(handle) == uint.MaxValue) { Native.CloseHandle(handle); Native.Check(false, "SuspendThread"); }
                    threads.Add(handle);
                }
                // CONTEXT on x64 must be aligned to 16 bytes. Refuse to patch a prologue
                // while a thread is executing inside the bytes being replaced.
                IntPtr allocation = Marshal.AllocHGlobal(1248);
                try
                {
                    IntPtr context = new IntPtr((allocation.ToInt64() + 15) & ~15L);
                    foreach (IntPtr thread in threads)
                    {
                        Marshal.Copy(new byte[1232], 0, context, 1232);
                        Marshal.WriteInt32(context, 48, 0x00100001);
                        Native.Check(Native.GetThreadContext(thread, context), "GetThreadContext");
                        long rip = Marshal.ReadInt64(context, 248);
                        if (rip >= patch && rip < patch + length) throw new InvalidOperationException("震动函数正在执行，稍后重试连接。");
                    }
                }
                finally { Marshal.FreeHGlobal(allocation); }
            }
            catch { Dispose(); throw; }
        }
        public void Dispose() { foreach (IntPtr thread in threads) { Native.ResumeThread(thread); Native.CloseHandle(thread); } threads.Clear(); }
    }

    internal sealed class RemoteHook : IDisposable
    {
        internal const string Signature = "48 8B C4 48 89 58 08 57 48 81 EC B0 00 00 00 0F 29 70 E8 0F 29 78 D8 44 0F 29 40 C8 44 0F 29 48 B8 44 0F 29 50 A8 44 0F 29 58 98 44 0F 29 60 88 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 44 24 38 48 8B F9 F3 44 0F 10 0A F3 44 0F 11 4C 24 28 F3 44 0F 10 52 04 F3 44 0F 11 54 24 2C F3 44 0F 10 5A 08 F3 44 0F 11 5C 24 30 F3 44 0F 10 62 0C";
        internal static readonly byte[] Magic = Encoding.ASCII.GetBytes("XBTRv002");
        const int Stolen = 15, StubOffset = 4096;
        internal readonly Process Process;
        IntPtr handle;
        bool nativeStarted;
        internal long Buffer, Target;
        internal bool Reused;
        internal RemoteHook(Process process, long fixtureTarget)
        {
            Process = process;
            // Only query/read/write/VM operation; no process creation or full-access permission.
            handle = Native.OpenProcess(0x0400 | 0x0010 | 0x0020 | 0x0008 | 0x0002, false, process.Id);
            Native.Check(handle != IntPtr.Zero, "无法连接游戏进程");
            try
            {
                if (fixtureTarget != 0) Target = fixtureTarget;
                else
                {
                    ProcessModule module = process.MainModule;
                    byte[] image = ReadImage(module.BaseAddress.ToInt64(), module.ModuleMemorySize);
                    int match = Find(image, false);
                    if (match >= 0) Target = module.BaseAddress.ToInt64() + match;
                    else
                    {
                        match = Find(image, true);
                        if (match < 0) throw new InvalidOperationException("运行器版本不匹配，或震动函数已被其他工具修改；未安装补丁。");
                        Target = module.BaseAddress.ToInt64() + match;
                        Buffer = BitConverter.ToInt64(image, match + 6) - StubOffset;
                        if (!Equal(Read(Buffer + 96, Magic.Length), Magic) && !Equal(Read(Buffer + 64, 8), Encoding.ASCII.GetBytes("XBTRv001"))) throw new InvalidOperationException("震动函数已被其他工具占用；未安装补丁。");
                        Write(Buffer + 96, Magic);
                        // An existing live bridge owns the buffer. A named mutex prevents local duplicates.
                        Reused = true; return;
                    }
                }
                byte[] original = Read(Target, Stolen);
                if (!Equal(original, Hex(Signature.Substring(0, 44)))) throw new InvalidOperationException("函数入口校验失败。");
                IntPtr allocation = Native.VirtualAllocEx(handle, IntPtr.Zero, (UIntPtr)8192, 0x3000, 0x04);
                Native.Check(allocation != IntPtr.Zero, "VirtualAllocEx"); Buffer = allocation.ToInt64();
                byte[] code = MakeStub(Buffer, Target, original);
                Write(Buffer + 96, Magic); Write(Buffer + StubOffset, code);
                uint old;
                Native.Check(Native.VirtualProtectEx(handle, new IntPtr(Buffer + StubOffset), (UIntPtr)4096, 0x20, out old), "Protect trampoline");
                Native.Check(Native.FlushInstructionCache(handle, new IntPtr(Buffer + StubOffset), (UIntPtr)code.Length), "Flush trampoline");
                byte[] jump = AbsoluteJump(Buffer + StubOffset); Array.Resize(ref jump, Stolen); jump[14] = 0x90;
                using (new ThreadPause(process, Target, Stolen))
                {
                    if (!Equal(Read(Target, Stolen), original)) throw new InvalidOperationException("游戏函数在连接期间发生变化，连接已取消。");
                    Native.Check(Native.VirtualProtectEx(handle, new IntPtr(Target), (UIntPtr)Stolen, 0x40, out old), "Protect handler");
                    try
                    {
                        try { Write(Target, jump); Native.Check(Native.FlushInstructionCache(handle, new IntPtr(Target), (UIntPtr)Stolen), "Flush handler"); }
                        catch { Write(Target, original); Native.FlushInstructionCache(handle, new IntPtr(Target), (UIntPtr)Stolen); throw; }
                    }
                    finally { uint ignored; Native.VirtualProtectEx(handle, new IntPtr(Target), (UIntPtr)Stolen, old, out ignored); }
                }
            }
            catch { Dispose(); throw; }
        }
        internal static byte[] MakeStub(long buffer, long target, byte[] original)
        {
            List<byte> code = new List<byte>();
            // Windows x64: RCX=this, RDX=&four floats. Touch only volatile integer
            // registers. Never modify the game's input structure or its stack.
            code.AddRange(Hex("4C 8B 12 4C 8B 5A 08 48 B8")); code.AddRange(BitConverter.GetBytes(buffer));
            // Serialize concurrent callers before updating the sequence and payload.
            // r8/r9 are volatile and this setter has only two parameters.
            code.AddRange(Hex("41 B8 01 00 00 00 44 87 40 18 45 85 C0 74 04 F3 90 EB ED"));
            code.AddRange(Hex("F0 FF 40 10 4C 89 10 4C 89 58 08 44 8B 51 20 44 89 50 20 F0 FF 40 10 C7 40 18 00 00 00 00"));
            // Renewed by the host every 8ms. If the host stops, fall back to the
            // original XInput handler after at most 120 vibration calls.
            code.AddRange(Hex("83 78 14 00 7E 08 F0 FF 48 14 31 C0 C3 90"));
            code.AddRange(original); code.AddRange(AbsoluteJump(target + Stolen));
            return code.ToArray();
        }
        internal static byte[] AbsoluteJump(long address) { List<byte> code = new List<byte>(Hex("FF 25 00 00 00 00")); code.AddRange(BitConverter.GetBytes(address)); return code.ToArray(); }
        internal static byte[] Hex(string value) { string[] parts = value.Split(new char[] {' '}, StringSplitOptions.RemoveEmptyEntries); byte[] bytes = new byte[parts.Length]; for (int i = 0; i < parts.Length; i++) bytes[i] = parts[i] == "??" ? (byte)0 : Convert.ToByte(parts[i], 16); return bytes; }
        internal static bool Equal(byte[] a, byte[] b) { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
        internal static int Find(byte[] image, bool patched)
        {
            string[] tokens = Signature.Split(' '); byte[] sig = Hex(Signature); int found = -1;
            for (int i = 0; i <= image.Length - sig.Length; i++)
            {
                if (image[i + 15] != sig[15] || image[i + 16] != sig[16]) continue;
                bool match = true;
                if (patched) { byte[] jump = Hex("FF 25 00 00 00 00"); for (int j = 0; j < jump.Length; j++) if (image[i + j] != jump[j]) match = false; if (image[i + 14] != 0x90) match = false; }
                for (int j = patched ? Stolen : 0; match && j < sig.Length; j++) if (tokens[j] != "??" && image[i + j] != sig[j]) match = false;
                if (match) { if (found >= 0) throw new InvalidOperationException("函数定位结果不唯一；连接已取消。"); found = i; }
            }
            return found;
        }
        internal byte[] Read(long address, int length)
        {
            byte[] bytes = new byte[length]; UIntPtr count;
            Native.Check(Native.ReadProcessMemory(handle, new IntPtr(address), bytes, (UIntPtr)length, out count) && count.ToUInt64() == (ulong)length, "ReadProcessMemory"); return bytes;
        }
        byte[] ReadImage(long address, int length)
        {
            byte[] image = new byte[length];
            for (int offset = 0; offset < length; offset += 4096)
            {
                int size = Math.Min(4096, length - offset); byte[] page = new byte[size]; UIntPtr read;
                if (Native.ReadProcessMemory(handle, new IntPtr(address + offset), page, (UIntPtr)size, out read) && read.ToUInt64() == (ulong)size)
                    Array.Copy(page, 0, image, offset, size);
            }
            return image;
        }
        internal void Write(long address, byte[] bytes)
        {
            UIntPtr count;
            Native.Check(Native.WriteProcessMemory(handle, new IntPtr(address), bytes, (UIntPtr)bytes.Length, out count) && count.ToUInt64() == (ulong)bytes.Length, "WriteProcessMemory");
        }
        internal void Lease(bool active) { Write(Buffer + 20, BitConverter.GetBytes(active ? 120 : 0)); }
        internal void Configure(bool active, int strength, int selection)
        {
            if(!active)
            {
                Write(Buffer+68,new byte[4]); Write(Buffer+40,new byte[4]); Lease(false);
                if(nativeStarted) for(int i=0;i<50;i++) { if(BitConverter.ToInt32(Read(Buffer+68,4),0)==1||NativeState<0) break; Thread.Sleep(10); }
                return;
            }
            Write(Buffer + 36, BitConverter.GetBytes(strength));
            Write(Buffer + 40, BitConverter.GetBytes(active ? System.Diagnostics.Process.GetCurrentProcess().Id : 0));
            Write(Buffer + 48, BitConverter.GetBytes(Native.GetTickCount64()));
            Write(Buffer + 56, BitConverter.GetBytes(selection));
            Lease(active);
        }
        internal int NativeState { get { return BitConverter.ToInt32(Read(Buffer + 44, 4), 0); } }
        internal void StartNativeOutput()
        {
            string file = Path.Combine(Log.Folder, "XboxTriggerOutput.dll");
            if (!File.Exists(file)) throw new FileNotFoundException("缺少进程内震动输出模块", file);
            Write(Buffer + 44, new byte[4]);
            ProcessModule remote = null;
            foreach (ProcessModule module in Process.Modules) if (string.Equals(module.ModuleName, "XboxTriggerOutput.dll", StringComparison.OrdinalIgnoreCase)) remote = module;
            if (remote == null)
            {
                byte[] path = Encoding.Unicode.GetBytes(file + "\0");
                IntPtr memory = Native.VirtualAllocEx(handle, IntPtr.Zero, (UIntPtr)path.Length, 0x3000, 0x04);
                Native.Check(memory != IntPtr.Zero, "Allocate DLL path"); Write(memory.ToInt64(), path);
                IntPtr loader = Native.GetProcAddress(Native.GetModuleHandle("kernel32.dll"), "LoadLibraryW");
                Native.Check(loader != IntPtr.Zero, "Resolve LoadLibraryW");
                IntPtr owner; Native.Check(Native.GetModuleHandleEx(4 | 2, loader, out owner), "Resolve loader module");
                StringBuilder ownerName = new StringBuilder(1024); Native.GetModuleFileName(owner, ownerName, ownerName.Capacity);
                string basename = Path.GetFileName(ownerName.ToString()); long ownerBase = 0;
                foreach (ProcessModule module in Process.Modules) if (string.Equals(module.ModuleName, basename, StringComparison.OrdinalIgnoreCase)) ownerBase = module.BaseAddress.ToInt64();
                if (ownerBase == 0) throw new InvalidOperationException("找不到游戏进程的系统加载器。");
                RunRemote(ownerBase + loader.ToInt64() - owner.ToInt64(), memory.ToInt64());
                Process.Refresh();
                foreach (ProcessModule module in Process.Modules) if (string.Equals(module.ModuleName, "XboxTriggerOutput.dll", StringComparison.OrdinalIgnoreCase)) remote = module;
                if (remote == null) throw new InvalidOperationException("进程内震动模块加载失败。");
            }
            if (!string.Equals(Path.GetFullPath(remote.FileName), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("游戏已加载另一个版本的震动输出模块，请重启游戏。");
            IntPtr local = Native.LoadLibraryEx(file, IntPtr.Zero, 1);
            Native.Check(local != IntPtr.Zero, "Read native DLL exports");
            try
            {
                IntPtr entry = Native.GetProcAddress(local, "StartOutput"); Native.Check(entry != IntPtr.Zero, "Resolve StartOutput");
                RunRemote(remote.BaseAddress.ToInt64() + entry.ToInt64() - local.ToInt64(), Buffer);
            }
            finally { Native.FreeLibrary(local); }
            Log.Write("In-game Windows.Gaming.Input output started, DLL base=0x" + remote.BaseAddress.ToInt64().ToString("X"));
            nativeStarted=true;
        }
        void RunRemote(long entry, long parameter)
        {
            uint id; IntPtr thread = Native.CreateRemoteThread(handle, IntPtr.Zero, UIntPtr.Zero, new IntPtr(entry), new IntPtr(parameter), 0, out id);
            Native.Check(thread != IntPtr.Zero, "Start native output");
            try { if (Native.WaitForSingleObject(thread, 5000) != 0) throw new InvalidOperationException("进程内震动初始化超时。"); }
            finally { Native.CloseHandle(thread); }
        }
        internal bool Sample(out float[] levels, out uint sequence)
        {
            for (int retry = 0; retry < 4; retry++)
            {
                byte[] before = Read(Buffer + 16, 4); uint seq = BitConverter.ToUInt32(before, 0);
                if ((seq & 1) != 0) continue;
                byte[] sample = Read(Buffer, 20);
                uint after = BitConverter.ToUInt32(Read(Buffer + 16, 4), 0);
                if (seq != after || seq != BitConverter.ToUInt32(sample, 16)) continue;
                levels = new float[4]; for (int i = 0; i < 4; i++) levels[i] = BitConverter.ToSingle(sample, i * 4);
                sequence = seq; return true;
            }
            levels = null; sequence = 0; return false;
        }
        public void Dispose()
        {
            if (handle != IntPtr.Zero)
            {
                if (Buffer != 0) { try { Configure(false, 100, 0); } catch {} }
                Native.CloseHandle(handle); handle = IntPtr.Zero;
                // Keep the inert trampoline until the game exits: other game threads
                // may be executing inside it. Lease=0 always runs the original code.
            }
        }
    }

    internal sealed class Bridge : IDisposable
    {
        struct DisplaySample
        {
            internal long Time;
            internal Vibration Value;
            internal DisplaySample(long time, Vibration value) { Time = time; Value = value; }
        }
        Thread worker; volatile bool stop;
        internal const int ControllerIndex = 0;
        internal volatile int TriggerPercent = 100;
        internal volatile bool Enabled = true;
        internal string Status = "等待游戏和手柄";
        internal string Levels = "实时震动：LT 0.00  RT 0.00  握把 L 0.00  R 0.00";
        internal string Peaks = "近 3 秒震动峰值：LT 0.00  RT 0.00  握把 L 0.00  R 0.00";
        internal volatile bool Connected;
        internal volatile bool Busy;
        internal volatile bool TestRequested;
        internal void Start() { stop = false; worker = new Thread(Run); worker.IsBackground = true; worker.SetApartmentState(ApartmentState.MTA); worker.Start(); }
        static double Clamp(float value) { return float.IsNaN(value) || float.IsInfinity(value) ? 0 : Math.Max(0, Math.Min(1, value)); }
        void Run()
        {
            Native.timeBeginPeriod(1);
            try
            {
                using (Wgi wgi = new Wgi())
                {
                    RemoteHook hook = null; IntPtr gamepad = IntPtr.Zero; int selected = -1;
                    uint sequence = 0; long lastSample = 0, lastReport = 0, lastDeviceCheck = 0, lastDisplay = -100; Stopwatch clock = Stopwatch.StartNew();
                    Queue<DisplaySample> displayHistory = new Queue<DisplaySample>();
                    try
                    {
                        while (!stop)
                        {
                            try
                            {
                                if (hook != null && hook.Process.HasExited) { hook.Dispose(); hook = null; Connected = false; Status = "游戏已退出；等待下次启动"; }
                                if (gamepad == IntPtr.Zero || selected != ControllerIndex)
                                {
                                    if (gamepad != IntPtr.Zero) { try { wgi.Put(gamepad, new Vibration()); } catch {} Marshal.Release(gamepad); gamepad = IntPtr.Zero; }
                                    if (hook != null) hook.Lease(false);
                                    int count = wgi.Count;
                                    if (count == 0) { Status = "未检测到手柄；请通过 USB 或蓝牙连接 Xbox 手柄"; Thread.Sleep(500); continue; }
                                    if (ControllerIndex >= count) { Status = "所选手柄不可用"; Thread.Sleep(500); continue; }
                                    gamepad = wgi.Open(ControllerIndex); selected = ControllerIndex;
                                    wgi.Put(gamepad, new Vibration());
                                    Log.Write("Windows.Gaming.Input controller opened, index=" + selected + ", count=" + count);
                                }
                                if (clock.ElapsedMilliseconds - lastDeviceCheck > 1000)
                                {
                                    lastDeviceCheck = clock.ElapsedMilliseconds;
                                    if (wgi.Count <= selected) throw new InvalidOperationException("手柄已断开；等待重新连接");
                                    IntPtr currentPad = wgi.Open(selected);
                                    bool changed = currentPad != gamepad; Marshal.Release(currentPad);
                                    if (changed) throw new InvalidOperationException("手柄连接发生变化；正在重新连接");
                                }
                                if (TestRequested)
                                {
                                    TestRequested = false;
                                    if (hook != null) hook.Lease(false);
                                    Status = "测试：左扳机 → 右扳机 → 握把";
                                    Vibration[] pulses = new Vibration[] { new Vibration(0, 0, 0.45, 0), new Vibration(0, 0, 0, 0.45), new Vibration(0.15, 0.15, 0, 0) };
                                    foreach (Vibration pulse in pulses) { if (stop) break; wgi.Put(gamepad, pulse); Thread.Sleep(500); wgi.Put(gamepad, new Vibration()); Thread.Sleep(300); }
                                    wgi.Put(gamepad, new Vibration());
                                }
                                if (hook == null)
                                {
                                    Process[] games = Process.GetProcessesByName("azimuth");
                                    if (games.Length == 0) { Status = "手柄已就绪；等待地平线 2 启动"; Thread.Sleep(400); continue; }
                                    if (games.Length != 1) throw new InvalidOperationException("检测到多个 Azimuth 进程，请只保留一个。");
                                    Busy = true;
                                    try { hook = new RemoteHook(games[0], 0); }
                                    finally { Busy = false; }
                                    sequence = 0; lastSample = clock.ElapsedMilliseconds; displayHistory.Clear();
                                    Log.Write(string.Format("Hook installed: PID={0}, target=0x{1:X}, reused={2}", games[0].Id, hook.Target, hook.Reused));
                                    hook.Configure(false, TriggerPercent, ControllerIndex);
                                    hook.StartNativeOutput();
                                }
                                float[] levels; uint current;
                                if (hook.Sample(out levels, out current))
                                {
                                    if (current != sequence) { sequence = current; lastSample = clock.ElapsedMilliseconds; }
                                    bool fresh = current != 0 && clock.ElapsedMilliseconds - lastSample < 750;
                                    uint foregroundPid; KeyFH2.Native.GetWindowThreadProcessId(KeyFH2.Native.GetForegroundWindow(),out foregroundPid);
                                    bool gameForeground=foregroundPid==(uint)hook.Process.Id;
                                    if (Enabled && gameForeground)
                                    {
                                        // The native output thread runs inside the foreground game.
                                        double strength = TriggerPercent / 100.0;
                                        Vibration output = fresh ? new Vibration(Clamp(levels[0]), Clamp(levels[1]), Clamp(levels[2]) * strength, Clamp(levels[3]) * strength) : new Vibration();
                                        int nativeState = hook.NativeState;
                                        if (nativeState < 0) throw new COMException("游戏进程内的手柄输出不可用", nativeState);
                                        if (nativeState == 0) { Status = "等待游戏进程内震动接口就绪"; Thread.Sleep(100); continue; }
                                        hook.Configure(true, TriggerPercent, ControllerIndex);
                                        long now = clock.ElapsedMilliseconds;
                                        displayHistory.Enqueue(new DisplaySample(now, output));
                                        while (displayHistory.Count > 0 && now - displayHistory.Peek().Time >= 3000) displayHistory.Dequeue();
                                        Levels = string.Format("实时震动：LT {0:0.00}  RT {1:0.00}  握把 L {2:0.00}  R {3:0.00}", output.LeftTrigger, output.RightTrigger, output.LeftMotor, output.RightMotor);
                                        if (now - lastDisplay >= 100)
                                        {
                                            Vibration peak = new Vibration();
                                            foreach (DisplaySample sample in displayHistory)
                                            {
                                                peak.LeftMotor = Math.Max(peak.LeftMotor, sample.Value.LeftMotor);
                                                peak.RightMotor = Math.Max(peak.RightMotor, sample.Value.RightMotor);
                                                peak.LeftTrigger = Math.Max(peak.LeftTrigger, sample.Value.LeftTrigger);
                                                peak.RightTrigger = Math.Max(peak.RightTrigger, sample.Value.RightTrigger);
                                            }
                                            Peaks = string.Format("近 3 秒震动峰值：LT {0:0.00}  RT {1:0.00}  握把 L {2:0.00}  R {3:0.00}", peak.LeftTrigger, peak.RightTrigger, peak.LeftMotor, peak.RightMotor);
                                            lastDisplay = now;
                                        }
                                        Status = fresh ? "已连接：正在转发游戏原生四路震动" : "已连接：等待游戏震动信号";
                                    }
                                    else
                                    {
                                        // Release ownership once, so zeros do not overwrite normal rumble.
                                        if (Connected) { hook.Configure(false, TriggerPercent, ControllerIndex); wgi.Put(gamepad,new Vibration()); }
                                        Levels = "震动输出已清零"; Peaks = ""; displayHistory.Clear(); Status = Enabled ? "游戏不在前台；震动暂时暂停" : "已暂停";
                                    }
                                    Connected = Enabled && gameForeground;
                                    if (clock.ElapsedMilliseconds - lastReport > 5000)
                                    {
                                        lastReport = clock.ElapsedMilliseconds;
                                        Log.Write(string.Format("Signal: samples={0}, fresh={1}, enabled={2}, raw={3:0.000},{4:0.000},{5:0.000},{6:0.000}", current / 2, fresh, Enabled, levels[0], levels[1], levels[2], levels[3]));
                                    }
                                }
                                Thread.Sleep(8);
                            }
                            catch (Exception exception)
                            {
                                Connected = false; Busy = false;
                                if (gamepad != IntPtr.Zero) { try { wgi.Put(gamepad, new Vibration()); } catch {} Marshal.Release(gamepad); gamepad = IntPtr.Zero; }
                                if (hook != null) { hook.Dispose(); hook = null; }
                                Status = exception.Message; Log.Write(exception.ToString()); Thread.Sleep(1000);
                            }
                        }
                    }
                    finally
                    {
                        if (hook != null) hook.Dispose();
                        if (gamepad != IntPtr.Zero) { try { wgi.Put(gamepad, new Vibration()); } catch {} Marshal.Release(gamepad); }
                    }
                }
            }
            catch (Exception exception) { Status = exception.Message; Log.Write(exception.ToString()); }
            finally { Native.timeEndPeriod(1); }
        }
        public void Dispose() { stop = true; if (worker != null && worker.IsAlive && !worker.Join(6000)) throw new InvalidOperationException("震动线程尚未停止，请稍后重试。"); }
    }

    internal static class Log
    {
        internal static readonly string Folder = Path.GetDirectoryName(typeof(Log).Assembly.Location);
        internal static void Write(string value) { try { File.AppendAllText(Path.Combine(Folder, "bridge.log"), DateTime.Now.ToString("s") + " " + value + Environment.NewLine, Encoding.UTF8); } catch {} }
    }

    internal sealed class Window : Form
    {
        readonly bool hosted;
        System.Windows.Forms.Timer refreshTimer;
        internal bool ServicesRunning { get; private set; }
        readonly Bridge bridge = new Bridge();
        readonly Label status = new Label(), levels = new Label(), peaks = new Label();
        readonly Label gameState = new Label();
        internal Window(bool hosted = false)
        {
            this.hosted = hosted;
            Text = "FH2 Xbox 扳机震动"; ClientSize = new Size(620, 370); FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false; Font = new Font("Microsoft YaHei UI", 10); StartPosition = FormStartPosition.CenterScreen;
            Label title = new Label { Text = "手柄震动", Location = new Point(20, 18), AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
            status.SetBounds(20, 55, 580, 45); levels.SetBounds(20, 105, 580, 30);
            peaks.SetBounds(20, 137, 580, 30);
            CheckBox enable = new CheckBox { Text = "启用扳机桥接", Checked = true, Location = new Point(20, 148), AutoSize = true };
            enable.CheckedChanged += delegate { bridge.Enabled = enable.Checked; };
            Label strengthLabel = new Label { Text = "扳机强度", Location = new Point(205, 150), AutoSize = true };
            NumericUpDown strength = new NumericUpDown { Minimum = 0, Maximum = 100, Value = 100, Location = new Point(285, 145), Width = 70 };
            strength.ValueChanged += delegate { bridge.TriggerPercent = (int)strength.Value; };
            gameState.SetBounds(20, 197, 140, 35);
            gameState.TextAlign = ContentAlignment.MiddleLeft;
            UpdateGameState(Process.GetProcessesByName("azimuth").Length > 0);
            Button test = new Button { Text = "测试扳机", Location = new Point(170, 197), Size = new Size(130, 35) };
            test.Click += delegate { bridge.TestRequested = true; };
            Button exit = new Button { Text = "停止并退出", Location = new Point(320, 197), Size = new Size(130, 35) };
            exit.Click += delegate { if (this.hosted && Parent != null) Parent.FindForm().Close(); else Close(); };
            Label hint = new Label { Text = "保持本窗口运行。关闭后恢复原有震动；不修改游戏文件。", Location = new Point(20, 246), AutoSize = true, ForeColor = Color.DimGray };
            Label author = new Label { Text = "作者：栗尾玲子KurioReiko", Location = new Point(20, 336), Size = new Size(580, 25), TextAlign = ContentAlignment.MiddleRight, Font = new Font(Font, FontStyle.Bold) };
            foreach (Control control in new Control[] {enable, strengthLabel, strength, gameState, test, exit, hint}) control.Top += 60;
            Controls.AddRange(new Control[] {title, status, levels, peaks, enable, strengthLabel, strength, gameState, test, exit, hint, author});
            System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 100 }; refreshTimer = timer;
            timer.Tick += delegate { status.Text = bridge.Status; levels.Text = bridge.Levels; peaks.Text = bridge.Peaks; bool gameRunning = Process.GetProcessesByName("azimuth").Length > 0; UpdateGameState(gameRunning); test.Enabled = !gameRunning; };
            Shown += delegate { if (!this.hosted) StartServices(); };
            FormClosed += delegate { StopServices(); timer.Dispose(); };
        }
        internal void StartServices() { if (ServicesRunning) return; bridge.Start(); refreshTimer.Start(); ServicesRunning = true; }
        internal void StopServices()
        {
            if (!ServicesRunning) return;
            refreshTimer.Stop(); bridge.Dispose(); ServicesRunning = false;
            status.Text = "震动模式未运行；原有震动处理已恢复";
            levels.Text = "实时震动：LT 0.00  RT 0.00  握把 L 0.00  R 0.00"; peaks.Text = "";
        }
        void UpdateGameState(bool running)
        {
            gameState.Text = running ? "● 游戏：已启动" : "● 游戏：未启动";
            gameState.ForeColor = running ? Color.ForestGreen : Color.DimGray;
        }
    }

    internal static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                if (args.Length > 0 && args[0] == "--fixture") return Tests.Fixture(args[1]);
                if (args.Length > 0 && args[0] == "--self-test") { Tests.SelfTest(); return 0; }
                if (args.Length > 0 && (args[0] == "--probe" || args[0] == "--test-motors")) { Tests.Probe(args[0] == "--test-motors"); return 0; }
                bool created;
                using (Mutex mutex = new Mutex(true, "Local\\FH2XboxTriggerBridge-v1", out created))
                {
                    if (!created) { MessageBox.Show("扳机震动工具已经在运行。", "FH2 Xbox 扳机震动"); return 1; }
                    try
                    {
                        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                        Window window = new Window();
                        if (args.Length > 0 && args[0] == "--start-game") window.Shown += delegate { LaunchGame(); };
                        Application.Run(window);
                    }
                    finally { mutex.ReleaseMutex(); }
                }
                return 0;
            }
            catch (Exception exception) { Console.Error.WriteLine(exception); Log.Write(exception.ToString()); return 1; }
        }
        static void LaunchGame()
        {
            if (Process.GetProcessesByName("azimuth").Length != 0) return;
            string folder = Path.GetFullPath(Path.Combine(Log.Folder, "..", "azimuth-windows"));
            string launcher = Path.Combine(folder, "FH2 DLC Launcher.exe"); if (!File.Exists(launcher)) launcher = Path.Combine(folder, "azimuth.exe");
            Process.Start(new ProcessStartInfo(launcher) { WorkingDirectory = folder, UseShellExecute = true });
        }
    }

    internal static class Tests
    {
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int Setter(IntPtr self, IntPtr levels);
        internal static int Fixture(string metadata)
        {
            IntPtr process = Native.OpenProcess(0x0400 | 0x0010 | 0x0020 | 0x0008, false, Process.GetCurrentProcess().Id);
            IntPtr memory = Native.VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)4096, 0x3000, 0x40);
            Native.Check(memory != IntPtr.Zero, "fixture allocation"); long baseAddress = memory.ToInt64();
            long counter = baseAddress + 1024, result = baseAddress + 1028, obj = baseAddress + 1100, input = baseAddress + 1200;
            List<byte> code = new List<byte>(RemoteHook.Hex(RemoteHook.Signature));
            code.AddRange(RemoteHook.Hex("48 B8")); code.AddRange(BitConverter.GetBytes(counter)); code.AddRange(RemoteHook.Hex("F0 FF 00 B8 D2 04 00 00"));
            code.AddRange(RemoteHook.Hex("4C 8D 9C 24 B0 00 00 00 49 8B 5B 10 41 0F 28 73 F0 41 0F 28 7B E0 45 0F 28 43 D0 45 0F 28 4B C0 45 0F 28 53 B0 45 0F 28 5B A0 45 0F 28 63 90 49 8B E3 5F C3"));
            UIntPtr written; Native.Check(Native.WriteProcessMemory(process, memory, code.ToArray(), (UIntPtr)code.Count, out written), "fixture code");
            Marshal.WriteInt32(new IntPtr(obj + 32), 7);
            float[] values = new float[] {0.21f, 0.32f, 0.43f, 0.54f}; byte[] data = new byte[16]; Buffer.BlockCopy(values, 0, data, 0, 16); Marshal.Copy(data, 0, new IntPtr(input), 16);
            Setter call = Marshal.GetDelegateForFunctionPointer(memory, typeof(Setter)) as Setter;
            File.WriteAllText(metadata, string.Format("{0},{1},{2},{3},{4}", Process.GetCurrentProcess().Id, baseAddress, counter, result, input));
            while (true) { Marshal.WriteInt32(new IntPtr(result), call(new IntPtr(obj), new IntPtr(input))); Thread.Sleep(5); }
        }
        static void Assert(bool condition, string message) { if (!condition) throw new Exception("FAILED: " + message); Console.WriteLine("PASS: " + message); }
        internal static void SelfTest()
        {
            string metadata = Path.Combine(Log.Folder, "fixture-" + Guid.NewGuid().ToString("N") + ".txt");
            Process child = Process.Start(new ProcessStartInfo(typeof(Tests).Assembly.Location, "--fixture \"" + metadata + "\"") { UseShellExecute = false, CreateNoWindow = true });
            try
            {
                for (int i = 0; i < 100 && !File.Exists(metadata); i++) { if (child.HasExited) throw new Exception("Fixture exited"); Thread.Sleep(50); }
                string[] info = File.ReadAllText(metadata).Split(','); long target = long.Parse(info[1]), counter = long.Parse(info[2]), result = long.Parse(info[3]), input = long.Parse(info[4]);
                byte[] originalInput;
                using (RemoteHook hook = new RemoteHook(child, target))
                {
                    originalInput = hook.Read(input, 16); Thread.Sleep(80);
                    Assert(BitConverter.ToInt32(hook.Read(result, 4), 0) == 1234, "inactive hook preserves original handler return value");
                    float[] values; uint sequence; Assert(hook.Sample(out values, out sequence) && sequence > 0 && Math.Abs(values[2] - 0.43f) < 0.0001f && Math.Abs(values[3] - 0.54f) < 0.0001f, "captures genuine four-channel float layout");
                    hook.Lease(true); Thread.Sleep(50);
                    int count = BitConverter.ToInt32(hook.Read(counter, 4), 0); Thread.Sleep(50);
                    Assert(BitConverter.ToInt32(hook.Read(result, 4), 0) == 0 && BitConverter.ToInt32(hook.Read(counter, 4), 0) == count, "active hook suppresses original XInput and returns S_OK");
                    Assert(RemoteHook.Equal(originalInput, hook.Read(input, 16)), "game input structure remains unchanged");
                    for (int wait = 0; wait < 100 && BitConverter.ToInt32(hook.Read(result, 4), 0) != 1234; wait++) Thread.Sleep(50);
                    Assert(BitConverter.ToInt32(hook.Read(result, 4), 0) == 1234, "expired host lease automatically restores original handler");
                    hook.Lease(true); Thread.Sleep(30); hook.Lease(false); Thread.Sleep(50);
                    Assert(BitConverter.ToInt32(hook.Read(result, 4), 0) == 1234, "stop immediately restores original handler");
                    byte[] patched = hook.Read(target, RemoteHook.Hex(RemoteHook.Signature).Length);
                    Assert(RemoteHook.Find(patched, true) == 0, "resident hook can be recognized safely on reconnect");
                }
                Console.WriteLine("All hook self-tests passed.");
            }
            finally { if (!child.HasExited) { child.Kill(); child.WaitForExit(); } child.Dispose(); if (File.Exists(metadata)) File.Delete(metadata); }
        }
        internal static void Probe(bool pulse)
        {
            using (Wgi wgi = new Wgi())
            {
                // Device enumeration can be asynchronous after initializing WGI.
                int count = 0; for (int retry = 0; retry < 30; retry++) { count = wgi.Count; if (count > 0) break; Thread.Sleep(100); }
                Console.WriteLine("Windows.Gaming.Input gamepads: " + count);
                if (count == 0) throw new Exception("No gamepad detected");
                if (pulse && Process.GetProcessesByName("azimuth").Length > 0) throw new Exception("Close the game before independent motor tests");
                for (int i = 0; i < count; i++)
                {
                    IntPtr gamepad = wgi.Open(i);
                    try
                    {
                        Vibration initial = wgi.Get(gamepad); Console.WriteLine(string.Format("Controller {0}: LM={1} RM={2} LT={3} RT={4}", i + 1, initial.LeftMotor, initial.RightMotor, initial.LeftTrigger, initial.RightTrigger));
                        if (pulse)
                        {
                            Vibration[] pulses = new Vibration[] { new Vibration(0, 0, 0.4, 0), new Vibration(0, 0, 0, 0.4), new Vibration(0.15, 0.15, 0, 0) };
                            string[] names = new string[] { "Left trigger", "Right trigger", "Main motors" };
                            try { for (int j = 0; j < pulses.Length; j++) { wgi.Put(gamepad, pulses[j]); Vibration actual = wgi.Get(gamepad); Console.WriteLine(names[j] + " command accepted: " + actual.LeftTrigger + "," + actual.RightTrigger); Thread.Sleep(350); wgi.Put(gamepad, new Vibration()); Thread.Sleep(350); } }
                            finally { wgi.Put(gamepad, new Vibration()); }
                        }
                    }
                    finally { Marshal.Release(gamepad); }
                }
            }
        }
    }
}
