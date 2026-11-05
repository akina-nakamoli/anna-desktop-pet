using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
namespace AnnaDesktopPet {
    internal sealed class Microphone : IDisposable {
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct Caps {public ushort Mid,Pid;public uint Version;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Name;public uint Formats;public ushort Channels,Reserved;}
        [StructLayout(LayoutKind.Sequential)] private struct Format {public ushort Tag,Channels;public uint Rate,Bytes;public ushort Align,Bits,Extra;}
        [StructLayout(LayoutKind.Sequential)] private struct Header {public IntPtr Data;public uint Length,Recorded;public UIntPtr User;public uint Flags,Loops;public IntPtr Next;public UIntPtr Reserved;}
        private delegate void Callback(IntPtr input,uint message,UIntPtr user,IntPtr header,IntPtr unused);
        [DllImport("winmm.dll")] private static extern uint waveInGetNumDevs();
        [DllImport("winmm.dll",CharSet=CharSet.Unicode)] private static extern uint waveInGetDevCaps(UIntPtr id,out Caps caps,uint size);
        [DllImport("winmm.dll")] private static extern uint waveInOpen(out IntPtr input,uint id,ref Format format,Callback callback,UIntPtr user,uint flags);
        [DllImport("winmm.dll")] private static extern uint waveInPrepareHeader(IntPtr input,IntPtr header,uint size);
        [DllImport("winmm.dll")] private static extern uint waveInAddBuffer(IntPtr input,IntPtr header,uint size);
        [DllImport("winmm.dll")] private static extern uint waveInStart(IntPtr input);
        [DllImport("winmm.dll")] private static extern uint waveInReset(IntPtr input);
        [DllImport("winmm.dll")] private static extern uint waveInUnprepareHeader(IntPtr input,IntPtr header,uint size);
        [DllImport("winmm.dll")] private static extern uint waveInClose(IntPtr input);
        private readonly object gate=new object();
        private readonly MemoryStream pcm=new MemoryStream();
        private readonly List<IntPtr> headers=new List<IntPtr>(),buffers=new List<IntPtr>();
        private readonly Callback callback;
        private IntPtr input;
        private volatile bool stopping,disposed;
        private double level;
        internal double Level {get{lock(gate)return level;}}
        internal static string[] Devices(){var names=new List<string>{"系统默认麦克风"};for(uint i=0;i<waveInGetNumDevs();i++){Caps c;if(waveInGetDevCaps(new UIntPtr(i),out c,(uint)Marshal.SizeOf(typeof(Caps)))==0)names.Add(i+" · "+c.Name);}return names.ToArray();}
        private static void Check(uint code){if(code!=0)throw new IOException("麦克风操作失败（"+code+"），请检查设备及麦克风权限。");}
        internal Microphone(){callback=OnData;}
        internal static void VerifyPcmSmoke(){
            using(var mic=new Microphone()){
                mic.stopping=true;byte[] bytes=new byte[3200];for(int i=0;i<bytes.Length;i+=2){bytes[i]=0;bytes[i+1]=16;}
                IntPtr data=Marshal.AllocHGlobal(bytes.Length),header=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Header)));
                string path=Path.Combine(Program.DataDir,"microphone-buffer-test.wav");
                try{Marshal.Copy(bytes,0,data,bytes.Length);Marshal.StructureToPtr(new Header{Data=data,Length=3200,Recorded=3200},header,false);mic.OnData(IntPtr.Zero,0x3C0,UIntPtr.Zero,header,IntPtr.Zero);if(Math.Abs(mic.Level-.5)>.001)throw new InvalidDataException("Microphone meter RMS mismatch");mic.Save(path);var wave=File.ReadAllBytes(path);if(wave.Length!=3244 || BitConverter.ToInt32(wave,24)!=16000 || BitConverter.ToInt16(wave,22)!=1 || BitConverter.ToInt16(wave,34)!=16)throw new InvalidDataException("Microphone WAV format mismatch");}
                finally{Marshal.FreeHGlobal(header);Marshal.FreeHGlobal(data);if(File.Exists(path))File.Delete(path);}
            }
        }
        internal void Start(int device){
            try{var format=new Format{Tag=1,Channels=1,Rate=16000,Bytes=32000,Align=2,Bits=16};Check(waveInOpen(out input,unchecked((uint)device),ref format,callback,UIntPtr.Zero,0x30000));
                for(int i=0;i<4;i++){IntPtr data=Marshal.AllocHGlobal(3200);buffers.Add(data);IntPtr header=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Header)));headers.Add(header);Marshal.StructureToPtr(new Header{Data=data,Length=3200},header,false);Check(waveInPrepareHeader(input,header,(uint)Marshal.SizeOf(typeof(Header))));Check(waveInAddBuffer(input,header,(uint)Marshal.SizeOf(typeof(Header))));}Check(waveInStart(input));
            }catch{Dispose();throw;}
        }
        private void OnData(IntPtr device,uint message,UIntPtr user,IntPtr pointer,IntPtr unused){
            if(message!=0x3C0)return;
            try{var header=(Header)Marshal.PtrToStructure(pointer,typeof(Header));byte[] bytes=new byte[header.Recorded];Marshal.Copy(header.Data,bytes,0,bytes.Length);double sum=0;for(int i=0;i+1<bytes.Length;i+=2){double value=(short)(bytes[i]|bytes[i+1]<<8)/32768.0;sum+=value*value;}
                lock(gate){if(!disposed){if(pcm.Length<32L*32000)pcm.Write(bytes,0,bytes.Length);level=bytes.Length==0?0:Math.Min(1,Math.Sqrt(sum/(bytes.Length/2))*4);}}
                if(!stopping && !disposed)waveInAddBuffer(device,pointer,(uint)Marshal.SizeOf(typeof(Header)));
            }catch{ }
        }
        internal void Save(string path){Stop();byte[] bytes;lock(gate)bytes=pcm.ToArray();using(var writer=new BinaryWriter(File.Create(path))){writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+bytes.Length);writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(16000);writer.Write(32000);writer.Write((short)2);writer.Write((short)16);writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(bytes.Length);writer.Write(bytes);}}
        private void Stop(){stopping=true;if(input!=IntPtr.Zero){waveInReset(input);foreach(IntPtr header in headers)waveInUnprepareHeader(input,header,(uint)Marshal.SizeOf(typeof(Header)));waveInClose(input);input=IntPtr.Zero;}foreach(IntPtr p in headers)Marshal.FreeHGlobal(p);foreach(IntPtr p in buffers)Marshal.FreeHGlobal(p);headers.Clear();buffers.Clear();lock(gate)level=0;}
        public void Dispose(){if(disposed)return;Stop();lock(gate){disposed=true;pcm.Dispose();}}
    }
}
