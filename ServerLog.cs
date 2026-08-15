using System;
using System.IO;
using UnityEngine;

namespace TidalNexus.StandaloneServer
{

    public static class ServerLog
    {
        private static readonly object Gate = new object();
        private static string _path;
        private static bool _failed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyStackTracePolicy()
        {
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.ScriptOnly);
            Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.ScriptOnly);
            Application.SetStackTraceLogType(LogType.Exception, StackTraceLogType.ScriptOnly);
        }

        public static void Info(string message) => Write("INFO ", message);

        public static void Warn(string message) => Write("WARN ", message);

        public static void Error(string message) => Write("ERROR", message);

        private static void Write(string level, string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {level} {message}";

            if (level == "ERROR")
            {
                Debug.LogError(line);
            }
            else if (level == "WARN ")
            {
                Debug.LogWarning(line);
            }
            else
            {
                Debug.Log(line);
            }

            AppendToFile(line);
        }

        private static void AppendToFile(string line)
        {
            if (_failed)
            {
                return;
            }

            try
            {
                lock (Gate)
                {
                    if (_path == null)
                    {
                        string dir = Path.Combine(
                            Path.GetDirectoryName(Application.dataPath) ?? ".", "logs");
                        Directory.CreateDirectory(dir);
                        _path = Path.Combine(dir, $"server-{DateTime.Now:yyyyMMdd-HHmmss}.log");
                    }

                    File.AppendAllText(_path, line + Environment.NewLine);
                }
            }
            catch
            {

                _failed = true;
            }
        }
    }
}
