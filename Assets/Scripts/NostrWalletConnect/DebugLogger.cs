using System;
using System.IO;
using System.Threading;
using UnityEngine;

namespace NostrWalletConnect
{
    public static class DebugLogger
    {
        private static string logFilePath;
        private static bool isInitialized = false;
        private static readonly object lockObject = new object();

        public static void Initialize()
        {
            if (!isInitialized)
            {
                lock (lockObject)
                {
                    if (!isInitialized) // Double-check pattern
                    {
                        try
                        {
                            string logDirectory;

                            // Check if we're on the main thread to safely access Unity APIs
                            if (Thread.CurrentThread.ManagedThreadId == 1)
                            {
                                logDirectory = Path.Combine(Application.persistentDataPath, "NWC_Logs");
                            }
                            else
                            {
                                // Fallback for background threads - use temp directory
                                var tempPath = Path.GetTempPath();
                                logDirectory = Path.Combine(tempPath, "NWC_Logs");
                            }

                            if (!Directory.Exists(logDirectory))
                            {
                                Directory.CreateDirectory(logDirectory);
                            }

                            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                            logFilePath = Path.Combine(logDirectory, $"nwc_debug_{timestamp}.log");

                            // Initialize the log file
                            File.WriteAllText(logFilePath, "=== NWC Debug Log Started ===" + Environment.NewLine);
                            File.AppendAllText(logFilePath, $"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}" + Environment.NewLine);
                            File.AppendAllText(logFilePath, $"Log file: {logFilePath}" + Environment.NewLine);
                            File.AppendAllText(logFilePath, $"Thread ID: {Thread.CurrentThread.ManagedThreadId}" + Environment.NewLine);
                            File.AppendAllText(logFilePath, "=====================================" + Environment.NewLine);

                            // Only call Unity Debug.Log from main thread
                            if (Thread.CurrentThread.ManagedThreadId == 1)
                            {
                                Debug.Log($"NWC Debug logging started. Log file: {logFilePath}");
                            }

                            isInitialized = true;
                        }
                        catch (Exception ex)
                        {
                            // Fallback to console logging if file logging fails
                            if (Thread.CurrentThread.ManagedThreadId == 1)
                            {
                                Debug.LogError($"Failed to initialize file logging: {ex.Message}");
                            }
                        }
                    }
                }
            }
        }

        public static void LogToFile(string message)
        {
            if (!isInitialized)
            {
                Initialize();
            }

            try
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                var threadId = Thread.CurrentThread.ManagedThreadId;
                var logEntry = $"[{timestamp}][T{threadId}] {message}";

                // Only call Unity Debug.Log from main thread to avoid threading issues
                if (Thread.CurrentThread.ManagedThreadId == 1)
                {
                    Debug.Log(message);
                }

                // Write to file - this is thread-safe
                lock (lockObject)
                {
                    if (!string.IsNullOrEmpty(logFilePath))
                    {
                        File.AppendAllText(logFilePath, logEntry + Environment.NewLine);
                    }
                }
            }
            catch (Exception ex)
            {
                // Only log errors from main thread
                if (Thread.CurrentThread.ManagedThreadId == 1)
                {
                    Debug.LogError($"Failed to write to log file: {ex.Message}");
                }
            }
        }

        public static void LogErrorToFile(string message)
        {
            LogToFile($"ERROR: {message}");
        }

        public static void LogWarningToFile(string message)
        {
            LogToFile($"WARNING: {message}");
        }

        public static string GetLogFilePath()
        {
            if (!isInitialized)
            {
                Initialize();
            }
            return logFilePath ?? "Log file not initialized";
        }

        public static void LogSeparator()
        {
            LogToFile("-------------------------------------");
        }

        public static void LogHexData(string label, byte[] data, int maxLength = 32)
        {
            if (data == null)
            {
                LogToFile($"{label}: null");
                return;
            }

            var hex = BitConverter.ToString(data).Replace("-", "");
            if (hex.Length > maxLength * 2)
            {
                hex = hex.Substring(0, maxLength * 2) + "...";
            }
            LogToFile($"{label} ({data.Length} bytes): {hex}");
        }
    }
}