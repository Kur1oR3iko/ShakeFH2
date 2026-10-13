using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace FH2Tools
{
    internal sealed class InputRoute : IDisposable
    {
        IntPtr handle;
        Process game;
        long memory;
        int sequence;
        long lastScan;
        readonly Stopwatch clock=Stopwatch.StartNew();
        internal string Update(KeyboardReply reply)
        {
            try
            {
                if(game!=null&&game.HasExited) Dispose();
                if(handle==IntPtr.Zero&&clock.ElapsedMilliseconds-lastScan>400)
                {
                    lastScan=clock.ElapsedMilliseconds;
                    Process[] games=Process.GetProcessesByName("azimuth");
                    if(games.Length==1) Attach(games[0]);
                }
                if(handle==IntPtr.Zero) return null;
                byte[] prefix=new byte[24];
                Buffer.BlockCopy(BitConverter.GetBytes(Process.GetCurrentProcess().Id),0,prefix,0,4);
                Buffer.BlockCopy(BitConverter.GetBytes(reply.PadNumber>0?1:0),0,prefix,4,4);
                Buffer.BlockCopy(BitConverter.GetBytes(reply.PadNumber>0?reply.PadNumber-1:-1),0,prefix,8,4);
                Buffer.BlockCopy(BitConverter.GetBytes(++sequence*2+1),0,prefix,12,4);
                Buffer.BlockCopy(BitConverter.GetBytes(XboxTriggerBridge.Native.GetTickCount64()),0,prefix,16,8);
                Write(memory,prefix);
                byte[] report=new byte[12]; int[] values=reply.Report;
                if(values!=null&&values.Length==7)
                {
                    Buffer.BlockCopy(BitConverter.GetBytes((ushort)values[0]),0,report,0,2); report[2]=(byte)values[1]; report[3]=(byte)values[2];
                    for(int i=0;i<4;i++) Buffer.BlockCopy(BitConverter.GetBytes((short)values[i+3]),0,report,4+i*2,2);
                }
                Write(memory+24,report); Write(memory+12,BitConverter.GetBytes(sequence*2+2));
                int status=BitConverter.ToInt32(Read(memory+36,4),0);
                return status==1?"已合并实体手柄与键盘输入":"等待游戏输入接口就绪";
            }
            catch(Exception exception)
            {
                XboxTriggerBridge.Log.Write("Input route: "+exception); Dispose(); return "输入路由重试："+exception.Message;
            }
        }
        void Attach(Process process)
        {
            game=process;
            handle=XboxTriggerBridge.Native.OpenProcess(0x0400|0x0010|0x0020|0x0008|0x0002,false,process.Id);
            XboxTriggerBridge.Native.Check(handle!=IntPtr.Zero,"Open input route process");
            IntPtr allocation=XboxTriggerBridge.Native.VirtualAllocEx(handle,IntPtr.Zero,(UIntPtr)4096,0x3000,0x04);
            XboxTriggerBridge.Native.Check(allocation!=IntPtr.Zero,"Allocate input route"); memory=allocation.ToInt64();
            string file=Path.Combine(XboxTriggerBridge.Log.Folder,"XboxTriggerOutput.dll"); ProcessModule remote=null;
            foreach(ProcessModule m in process.Modules) if(m.ModuleName.Equals("XboxTriggerOutput.dll",StringComparison.OrdinalIgnoreCase)) remote=m;
            if(remote==null)
            {
                byte[] path=Encoding.Unicode.GetBytes(file+"\0"); IntPtr pathAddress=XboxTriggerBridge.Native.VirtualAllocEx(handle,IntPtr.Zero,(UIntPtr)path.Length,0x3000,0x04); Write(pathAddress.ToInt64(),path);
                IntPtr loader=XboxTriggerBridge.Native.GetProcAddress(XboxTriggerBridge.Native.GetModuleHandle("kernel32.dll"),"LoadLibraryW"),owner;
                XboxTriggerBridge.Native.Check(XboxTriggerBridge.Native.GetModuleHandleEx(6,loader,out owner),"Resolve native loader");
                StringBuilder ownerName=new StringBuilder(1024); XboxTriggerBridge.Native.GetModuleFileName(owner,ownerName,1024); string name=Path.GetFileName(ownerName.ToString());
                long remoteOwner=0; foreach(ProcessModule m in process.Modules) if(m.ModuleName.Equals(name,StringComparison.OrdinalIgnoreCase)) remoteOwner=m.BaseAddress.ToInt64();
                if(remoteOwner==0) throw new Exception("Native loader not found");
                Call(remoteOwner+loader.ToInt64()-owner.ToInt64(),pathAddress.ToInt64()); process.Refresh();
                foreach(ProcessModule m in process.Modules) if(m.ModuleName.Equals("XboxTriggerOutput.dll",StringComparison.OrdinalIgnoreCase)) remote=m;
            }
            if(remote==null||!Path.GetFullPath(remote.FileName).Equals(Path.GetFullPath(file),StringComparison.OrdinalIgnoreCase)) throw new Exception("请重启游戏，以加载新版输入模块。");
            IntPtr local=XboxTriggerBridge.Native.LoadLibraryEx(file,IntPtr.Zero,1);
            XboxTriggerBridge.Native.Check(local!=IntPtr.Zero,"Read input module exports");
            try
            {
                IntPtr entry=XboxTriggerBridge.Native.GetProcAddress(local,"StartInputRoute");
                if(entry==IntPtr.Zero) throw new Exception("输入模块版本不匹配。");
                Call(remote.BaseAddress.ToInt64()+entry.ToInt64()-local.ToInt64(),memory);
            }
            finally { XboxTriggerBridge.Native.FreeLibrary(local); }
            XboxTriggerBridge.Log.Write("Input route attached, PID="+process.Id);
        }
        void Call(long entry,long parameter)
        {
            uint id; IntPtr thread=XboxTriggerBridge.Native.CreateRemoteThread(handle,IntPtr.Zero,UIntPtr.Zero,new IntPtr(entry),new IntPtr(parameter),0,out id);
            XboxTriggerBridge.Native.Check(thread!=IntPtr.Zero,"Start input route");
            try { if(XboxTriggerBridge.Native.WaitForSingleObject(thread,5000)!=0) throw new TimeoutException("Input route initialization timeout"); }
            finally { XboxTriggerBridge.Native.CloseHandle(thread); }
        }
        void Write(long address,byte[] bytes)
        {
            UIntPtr written; XboxTriggerBridge.Native.Check(XboxTriggerBridge.Native.WriteProcessMemory(handle,new IntPtr(address),bytes,(UIntPtr)bytes.Length,out written)&&written.ToUInt64()==(ulong)bytes.Length,"Write keyboard report");
        }
        byte[] Read(long address,int count)
        {
            byte[] bytes=new byte[count]; UIntPtr read; XboxTriggerBridge.Native.Check(XboxTriggerBridge.Native.ReadProcessMemory(handle,new IntPtr(address),bytes,(UIntPtr)count,out read)&&read.ToUInt64()==(ulong)count,"Read input routing state"); return bytes;
        }
        public void Dispose()
        {
            if(handle!=IntPtr.Zero) { try { if(memory!=0) Write(memory+4,new byte[4]); } catch {} XboxTriggerBridge.Native.CloseHandle(handle); }
            handle=IntPtr.Zero; memory=0; if(game!=null) game.Dispose(); game=null;
        }
    }
}
