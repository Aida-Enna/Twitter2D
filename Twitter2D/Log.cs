using System;
using System.IO;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text;
using System.Net;
using Tweetinvi.Core.Models;
using Tweetinvi;

namespace TwitterStreaming
{
    static class Log
    {
        private enum Category
        {
            INFO,
            ERROR
        }

        private static readonly object logLock = new();

        public static void WriteInfo(string format) => WriteLine(Category.INFO, format);
        public static void WriteError(string format) => WriteLine(Category.ERROR, format);

        private static void WriteLine(Category category, string format)
        {
            var logLine = $"{DateTime.Now.ToString("s").Replace('T', ' ')} [{category}] {format}{Environment.NewLine}";

            lock (logLock)
            {
                if (category == Category.ERROR)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write(logLine);
                    Console.ResetColor();

                    if(!String.IsNullOrWhiteSpace(Program.config.DebugWebhookURL))
                    {
                        WebClient client = new WebClient();
                        client.Headers.Add("Content-Type", "application/json");
                        string payload = "{\"content\": \"" + "```Twitter2D: " + logLine.Replace("\r\n","") + "```\"}";
                        client.UploadData(Program.config.DebugWebhookURL, Encoding.UTF8.GetBytes(payload));
                    }    
                }
                else
                {
                    Console.Write(logLine);
                }
            }
        }
    }
}
