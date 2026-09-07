#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
namespace HowToSuck.Diagnostics
{
    public sealed class AudioCaptureData
    {public float[] Samples;public int Count,Callbacks,Rate,Channels;public string Fault;}
    // Actual callback store; no Unity, file or audio-output operations while holding its bounded copy lock.
    public sealed class AudioCaptureBuffer
    {
        private readonly object gate=new object();private float[] samples;private int count,callbacks,rate;private bool armed;private string fault;
        public bool Armed{get{lock(gate)return armed;}}
        public long Frames{get{lock(gate)return count/2;}}
        public void Begin(int sampleRate,int seconds)
        {
            if(sampleRate!=48000||seconds<1||seconds>60)throw new ArgumentException("Bounded 48kHz stereo capture required.");
            lock(gate){if(samples!=null)throw new InvalidOperationException("Previous capture must drain before another Begin.");samples=new float[checked(sampleRate*2*seconds)];count=callbacks=0;rate=sampleRate;fault=null;armed=true;}
        }
        public void Append(float[] input,int channels)
        {
            lock(gate)
            {
                if(!armed)return;
                if(channels!=2||input==null||input.Length%2!=0){fault="Actual listener output is not complete stereo frames";armed=false;return;}
                if(count+input.Length>samples.Length){fault="Bounded DSP capture overflow";armed=false;return;}
                Array.Copy(input,0,samples,count,input.Length);count+=input.Length;callbacks++;
            }
        }
        public void Cancel(string reason){lock(gate){if(armed){fault=reason;armed=false;}}}
        public AudioCaptureData Drain()
        {
            lock(gate)
            {
                // Acquiring this lock after armed=false is the complete callback drain handshake.
                armed=false;var value=new AudioCaptureData{Samples=samples,Count=count,Callbacks=callbacks,Rate=rate,Channels=2,Fault=fault};
                samples=null;count=callbacks=0;fault=null;return value;
            }
        }
    }
    public sealed class AudioDspCapture:MonoBehaviour
    {
        public readonly AudioCaptureBuffer Buffer=new AudioCaptureBuffer();
        private void OnAudioFilterRead(float[] data,int channels)=>Buffer.Append(data,channels);
        private void OnDisable()=>Buffer.Cancel("Listener/tap disabled before explicit stop handshake");
        private void OnDestroy()=>Buffer.Cancel("Listener/tap destroyed before explicit stop handshake");
    }
    [Serializable] public sealed class AudioCaptureReceipt
    {
        public string path,sha256,fault;public int rate,channels,callbacks,nonfinite;public long samples,frames,clipped,bytes;
        public double seconds,peak,rms;public bool drained;
    }
    public static class AudioCaptureFile
    {
        // Runs on the diagnostic main thread only after Drain. IEEE float preserves peaks; no normalization/clamping.
        public static AudioCaptureReceipt Write(string path,AudioCaptureData data)
        {
            if(data.Samples==null||data.Count<=0||data.Rate!=48000)throw new InvalidOperationException("No actual listener DSP samples.");
            double sum=0,peak=0;long clipped=0;int invalid=0;
            for(int i=0;i<data.Count;i++)
            {float sample=data.Samples[i];if(float.IsNaN(sample)||float.IsInfinity(sample)){invalid++;continue;}double value=Math.Abs((double)sample);sum+=value*value;peak=Math.Max(peak,value);if(value>=1)clipped++;}
            using(var writer=new BinaryWriter(File.Create(path)))
            {
                int bytes=checked(data.Count*4);writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+bytes);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)3);writer.Write((short)2);
                writer.Write(data.Rate);writer.Write(data.Rate*8);writer.Write((short)8);writer.Write((short)32);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(bytes);for(int i=0;i<data.Count;i++)writer.Write(data.Samples[i]);
            }
            string hash;using(var algorithm=SHA256.Create())using(var stream=File.OpenRead(path))hash=BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
            return new AudioCaptureReceipt{path=path,sha256=hash,fault=data.Fault,rate=data.Rate,channels=data.Channels,callbacks=data.Callbacks,
                nonfinite=invalid,samples=data.Count,frames=data.Count/2,clipped=clipped,bytes=new FileInfo(path).Length,seconds=data.Count/(double)(data.Rate*2),peak=peak,rms=Math.Sqrt(sum/data.Count),drained=true};
        }
    }
}
#endif
