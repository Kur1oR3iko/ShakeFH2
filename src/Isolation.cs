using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace FH2Tools
{
    public sealed class KeyboardCommand
    {
        public bool Stop,Enabled,Capturing,ForegroundOnly,ExtraControls;
        public long EnabledRevision;
        public int SteerMs,CenterMs,TriggerMs,SteerLimit;
        public string Preset;
        public List<int[]> Bindings=new List<int[]>();
        internal static KeyboardCommand From(KeyFH2.Configuration c,bool enabled,long revision,bool capturing)
        {
            KeyboardCommand command=new KeyboardCommand {Enabled=enabled,EnabledRevision=revision,Capturing=capturing,ForegroundOnly=c.ForegroundOnly,ExtraControls=c.ExtraControls,SteerMs=c.SteerMs,CenterMs=c.CenterMs,TriggerMs=c.TriggerMs,SteerLimit=c.SteerLimit,Preset=c.Preset};
            foreach(KeyFH2.Binding binding in c.Bindings) command.Bindings.Add(new int[] {(int)binding.Action,binding.Primary,binding.Alternate});
            return command;
        }
        internal KeyFH2.Configuration Configuration()
        {
            KeyFH2.Configuration c=KeyFH2.Configuration.Default(2);
            c.ForegroundOnly=ForegroundOnly; c.ExtraControls=ExtraControls; c.SteerMs=SteerMs; c.CenterMs=CenterMs; c.TriggerMs=TriggerMs; c.SteerLimit=SteerLimit; c.Preset=Preset;
            foreach(int[] row in Bindings)
            {
                if(row.Length!=3||row[1]<0||row[1]>255||row[2]<0||row[2]>255) throw new InvalidDataException("Invalid keyboard binding packet");
                KeyFH2.Binding binding=c.Get((KeyFH2.ActionId)row[0]); if(binding==null) throw new InvalidDataException("Invalid action");
                binding.Primary=row[1]; binding.Alternate=row[2];
            }
            return c;
        }
    }
    public sealed class KeyboardReply
    {
        public bool Enabled;
        public int PadNumber;
        public string Status,Live;
        public int[] Report;
    }
    internal static class KeyboardIsolation
    {
        internal static bool IsWorker;
        internal static int HostPid;
        internal static void RunParent(Func<KeyboardCommand> command,Action<KeyboardReply> update,Func<bool> stopping)
        {
            string name="ShakeFH2-keyboard-"+Guid.NewGuid().ToString("N"); Process worker=null; InputRoute route=new InputRoute();
            JavaScriptSerializer json=new JavaScriptSerializer();
            try
            {
                using(NamedPipeServerStream pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous))
                {
                    string executable=Assembly.GetExecutingAssembly().Location;
                    worker=Process.Start(new ProcessStartInfo(executable,"--keyboard-worker "+name+" "+Process.GetCurrentProcess().Id) {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Path.GetDirectoryName(executable)});
                    IAsyncResult connection=pipe.BeginWaitForConnection(null,null);
                    if(!connection.AsyncWaitHandle.WaitOne(5000)) throw new TimeoutException("键盘后端连接超时。");
                    pipe.EndWaitForConnection(connection);
                    using(StreamReader reader=new StreamReader(pipe,Encoding.UTF8,false,4096,true))
                    using(StreamWriter writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true))
                    {
                        writer.AutoFlush=true;
                        while(!stopping())
                        {
                            writer.WriteLine(json.Serialize(command()));
                            var response=reader.ReadLineAsync();
                            if(!response.Wait(2500)||response.Result==null) throw new IOException("键盘后端没有响应，输入已停止。");
                            KeyboardReply reply=json.Deserialize<KeyboardReply>(response.Result);
                            string routing=route.Update(reply);
                            if(routing!=null) reply.Status=reply.Status+" · "+routing;
                            update(reply); Thread.Sleep(8);
                        }
                        writer.WriteLine(json.Serialize(new KeyboardCommand {Stop=true}));
                        var stopped=reader.ReadLineAsync(); stopped.Wait(1000);
                    }
                }
            }
            catch(Exception exception)
            {
                update(new KeyboardReply {Enabled=false,PadNumber=0,Status=exception.Message,Live="键盘输入已释放"});
                XboxTriggerBridge.Log.Write("Keyboard isolation: "+exception);
            }
            finally
            {
                if(worker!=null)
                {
                    if(!worker.HasExited&&!worker.WaitForExit(1500)) { worker.Kill(); worker.WaitForExit(); }
                    worker.Dispose();
                }
                route.Dispose();
            }
        }
        internal static int RunWorker(string name,int hostPid)
        {
            IsWorker=true; HostPid=hostPid; JavaScriptSerializer json=new JavaScriptSerializer();
            using(NamedPipeClientStream pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous))
            {
                pipe.Connect(5000);
                using(StreamReader reader=new StreamReader(pipe,Encoding.UTF8,false,4096,true))
                using(StreamWriter writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true))
                {
                    writer.AutoFlush=true; KeyFH2.Mapper mapper=null; long revision=long.MinValue;
                    try
                    {
                        while(true)
                        {
                            string line=reader.ReadLine(); if(line==null) break;
                            KeyboardCommand packet=json.Deserialize<KeyboardCommand>(line);
                            if(packet.Stop) { if(mapper!=null) { mapper.Dispose(); mapper=null; } writer.WriteLine("{}"); break; }
                            if(mapper==null) { mapper=new KeyFH2.Mapper(packet.Configuration()); mapper.Start(); }
                            else mapper.SetConfiguration(packet.Configuration());
                            if(revision!=packet.EnabledRevision) { mapper.Enabled=packet.Enabled; revision=packet.EnabledRevision; }
                            mapper.Capturing=packet.Capturing;
                            KeyFH2.Report report=mapper.LastOutput;
                            writer.WriteLine(json.Serialize(new KeyboardReply {Enabled=mapper.Enabled,PadNumber=mapper.PadNumber,Status=mapper.Status,Live=mapper.Live,Report=new int[] {report.Buttons,report.LT,report.RT,report.LX,report.LY,report.RX,report.RY}}));
                        }
                    }
                    finally { if(mapper!=null) mapper.Dispose(); }
                }
            }
            return 0;
        }
    }
}
