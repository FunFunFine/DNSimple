using System;
using System.IO;
using System.Runtime.InteropServices;

namespace DNSimple
{
    internal class Program
    {
        private static ConsoleEventDelegate handler; // Keeps it from getting garbage collected

        private static Server server;

        private static void OnProcessExit()
        {
            server.Dispose();
            var path = @"D:\OnProcessExitRecording.txt";
            if (!File.Exists(path))
            {
                File.Create(path);
                TextWriter tw = new StreamWriter(path);
                tw.WriteLine("This program has exited at " + DateTime.Now);
                tw.Close();
            }
            else if (File.Exists(path))
            {
                TextWriter tw = new StreamWriter(path, true);
                tw.WriteLine("This program has exited at " + DateTime.Now);
                tw.Close();
            }
        }

        private static bool ConsoleEventCallback(int eventType)
        {
            if (eventType == 2)
                OnProcessExit();
            return false;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleCtrlHandler(ConsoleEventDelegate callback, bool add);

        private static void Main(string[] args)
        {
            server = new Server();
            server.Run();
        }

        // Pinvoke
        private delegate bool ConsoleEventDelegate(int eventType);
    }
}