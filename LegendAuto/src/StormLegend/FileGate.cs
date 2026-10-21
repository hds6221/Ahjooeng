using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace StormLegend
{
    // 同一 사용자 세션의 AutoCAD 프로세스 사이에서도 읽기-수정-쓰기를 직렬화합니다.
    internal sealed class FileGate : IDisposable
    {
        private readonly Mutex mutex;
        private FileGate(string path)
        {
            string key;
            using (var sha = SHA256.Create())
                key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(path).TrimEnd('\\').ToUpperInvariant()))).Replace("-", "");
            mutex = new Mutex(false, "Local\\StormLegend-" + key);
            try
            {
                try { if (!mutex.WaitOne(TimeSpan.FromSeconds(30))) throw new IOException("다른 AutoCAD에서 파일을 처리 중입니다. 잠시 뒤 다시 실행하세요."); }
                catch (AbandonedMutexException) { } // 종료된 프로세스의 잠금을 인수한 뒤 파일을 다시 검증합니다.
            }
            catch { mutex.Dispose(); throw; }
        }
        public static FileGate Enter(string path) { return new FileGate(path); }
        public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
    }
}
