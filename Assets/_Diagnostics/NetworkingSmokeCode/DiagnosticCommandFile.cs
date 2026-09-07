#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Text;
using System.Threading;
namespace HowToSuck.Diagnostics
{
    // Diagnostic command polling only. No saves/profile access, command execution, retry sleeps or game state.
    public static class DiagnosticCommandFile
    {
        private static long missing,busy;
        public static long MissingPolls=>Interlocked.Read(ref missing);
        public static long BusyPolls=>Interlocked.Read(ref busy);
        public static bool TryRead(string path,int maximumBytes,out string json)
        {
            json=null;if(maximumBytes<1)throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            try
            {
                using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
                {
                    // Check the actual opened version, removing the old FileInfo/open replacement race.
                    if(stream.Length>maximumBytes)throw new InvalidOperationException("Diagnostic command exceeds bounded size: "+maximumBytes);
                    using(var reader=new StreamReader(stream,Encoding.UTF8,true))json=reader.ReadToEnd();
                }
                return true;
            }
            catch(FileNotFoundException){Interlocked.Increment(ref missing);return false;}
            catch(DirectoryNotFoundException){Interlocked.Increment(ref missing);return false;}
            catch(IOException error) when((error.HResult&0xffff)==32||(error.HResult&0xffff)==33)
            {Interlocked.Increment(ref busy);return false;}
            // Unauthorized access, other IO, oversized payload and JSON errors are not swallowed.
            // A missing/busy poll does not advance a sequence; existing launcher deadlines still fail a stuck command.
        }
    }
}
#endif
