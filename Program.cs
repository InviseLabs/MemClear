/* * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * *
* MemClear by Invise Labs / Authors: Mike Lierman
* Copyright Invise Labs 2023.
* Open-source under Creative Commons Non-Commercial Use.
* For use in commercial and for-profit applications, please contact us.
* We can be reached at contact@inviselabs.com.
* * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * */

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Collections.Generic;
using Microsoft.VisualBasic.Logging;
using System.IO;
using System.Windows.Forms;

namespace MemClear
{

    #region Native Structures
    //- (SYSTEM_CACHE_INFORMATION)
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct SYSTEM_CACHE_INFORMATION
    {
        public long CurrentSize, PeakSize;
        public long PageFaultCount;
        public long MinimumWorkingSet, MaximumWorkingSet;
        public long Unused1, Unused2, Unused3, Unused4;
    }

    //- (SYSTEM_CACHE_INFORMATION_64_BIT)
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct SYSTEM_CACHE_INFORMATION_64_BIT
    {
        public long CurrentSize, PeakSize;
        public long PageFaultCount;
        public long MinimumWorkingSet, MaximumWorkingSet;
        public long Unused1, Unused2, Unused3, Unused4;
    }

    //- (TokPriv1Luid)
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct TokPriv1Luid
    {
        public int Count, Attr;
        public long Luid;

    }
    #endregion

    public class Program
    {
        #region Declarations
        //- (Declaration of constants)
        const int SE_PRIVILEGE_ENABLED = 2;
        const string SeIncreaseQuotaName = "SeIncreaseQuotaPrivilege";
        const string SeProfileSingleName = "SeProfileSingleProcessPrivilege";
        const int SystemFileCacheInformation = 0x0015;
        const int SystemMemoryListInformation = 0x0050;
        const int MemoryPurgeStandbyList = 4;
        const int MemoryEmptyWorkingSets = 2;

        //- Imports
        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool LookupPrivilegeValue(string host, string name, ref long pluid);

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool AdjustTokenPrivileges(IntPtr htok, bool disall, ref TokPriv1Luid newst, int len, IntPtr prev, IntPtr relen);

        [DllImport("ntdll.dll")]
        public static extern UInt32 NtSetSystemInformation(int InfoClass, IntPtr Info, int Length);

        [DllImport("psapi.dll")]
        static extern int EmptyWorkingSet(IntPtr hwProc);
        #endregion

        static bool output, logEnabled = false;
        static string _ = string.Empty;
        static string logPath = System.IO.Path.GetDirectoryName(Application.ExecutablePath) + "\\log.txt";

        //- Entry Point
        static void Main(string[] args)
        {
            Console.Title = "MemClear by Invise Labs"; ;

            Console.WriteLine("MemClear by Invise Labs / Authors: Mike Lierman"); //- AUTHORS LINE - Change if interns/others at Invise contribute
            Console.WriteLine("Clears Working Set Memory for all processes. Processes may not honor any changes and may try to reclaim their memory.\n");

            //- Gert user input and assing output bool
            Console.WriteLine("Press ENTER key to continue. Type O for detailed output, type L to log detailed output, type LO for both.");
            var l = Console.ReadLine().ToLower();
            output = (l.Contains("o")) ? true : false;
            logEnabled = (l.Contains("l")) ? true : false;

            //- Begin the log if enabled
            if (logEnabled) { Log("MemClear by Invise Labs\nNew Session Started"); }

            //- Clear console and get ready
            ClearConsole();

            var before = new PerformanceCounter("Memory", "Available MBytes", true).RawValue.ToString();
            var r = logEnabled ? $"/ Log & Output Enabled: {output}" : (output ? $"/ Output Enabled: {output}" : _);
            Console.WriteLine($">> Process has begun. {r}");

            EmptyWorkingSet();
            ClearFileSystemCache(true);

            //- Finished, now get memory free after and write details
            var after = new PerformanceCounter("Memory", "Available MBytes", true).RawValue.ToString(); ;
            double ram = Math.Round((double)new Microsoft.VisualBasic.Devices.ComputerInfo().TotalPhysicalMemory / 1024 / 1024 / 1024, 2);

            //- Write finish text
            string finishText = $"\nTotal Physical RAM: {ram} GB" +
            $"\nAvailable RAM Before: {before} MB / Available RAM After: {after} MB" +
            $"{(logEnabled ? "\n\nLog can be found at "+logPath : _)}" +
            $"\n\nPress any key to exit.";
            Console.WriteLine(finishText);

            //- Log finish text if log enabled
            if (logEnabled) { Log(finishText); }
            
            //- Wait for input to exit
            Console.ReadKey();
            Application.Exit();
        }

        static void ClearConsole()
        {
            Console.Clear();
            Console.WriteLine("MemClear by Invise Labs / Authors: Mike Lierman"); //- AUTHORS LINE - Change if interns/others at Invise contribute
            Console.WriteLine("Clears Working Set Memory for all processes.\n");
            //Console.WriteLine("");
        }

        static void ClearCurrentConsoleLine()
        {
            int currentLineCursor = Console.CursorTop;
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write(new string(' ', Console.WindowWidth));
            Console.SetCursorPosition(0, currentLineCursor);
        }

        static void Log(string text)
        {
            try
            {
                using (var logFile = File.Open(logPath, FileMode.OpenOrCreate))
                {
                    logFile.Seek(0, SeekOrigin.End);
                    using (var s = new StreamWriter(logFile))
                    { s.WriteLine($"{DateTime.Now.ToString()}: {text}"); }
                }
            }
            catch { /* Error when writing to error log, that sucks. Oh well. */ }
        }

        //- A method to empty the working set of all processes
        public static void EmptyWorkingSet()
        {
            //- Declare variables for process name, success list, and fail list
            var pName = _;
            var successLst = new List<string>();
            var failLst = new List<string>();

            //- Get all processes and print their count
            var procs = Process.GetProcesses();
            Console.SetCursorPosition(0, 4);
            ClearCurrentConsoleLine();
            Console.Write($">> Processes: {procs.Length}");

            // Loop through all processes using foreach instead of for
            foreach (var p in procs)
            {
                //- Check if the process is not null
                if (p != null)
                {
                    //- Try to empty the working set of the process and add to the success list
                    try
                    {
                        //- Get the process name and print it
                        pName = p.ProcessName;
                        ClearCurrentConsoleLine();
                        Console.Write($">> {pName}");

                        //- Empty the working set for the process and wait 5ms
                        EmptyWorkingSet(p.Handle);
                        Thread.Sleep(10);

                        successLst.Add(pName);
                    }

                    catch (Exception ex)
                    {
                        //- Use string interpolation and conditional operator to format the fail list item, _ means discard
                        failLst.Add($"{pName}{(output ? $": {ex.Message}" : _)}");
                        if (logEnabled) { Log($"{pName}: {ex.Message}"); }
                    }
                }
            }

            //- Print the final status and the success and fail lists
            ClearCurrentConsoleLine();
            Console.WriteLine($">> FINISHED. Cleared memory from {successLst.Count} processes, failed {failLst.Count}");
            if (output || logEnabled)
            {
                string text="\n=====Begin Output====="+
                $"\nMemory Freed From: {successLst.Count}"+
                "\n_________________________" +
                //- Use string.Join instead of a loop to print the list items
                string.Join(Environment.NewLine, successLst)+
                $"\n\nnFailed: {failLst.Count}"+
                "\n_________________________" +
                $"\n{string.Join(Environment.NewLine, failLst)}";

                if (output) { Console.WriteLine(text); }
                if(logEnabled) { Log(text); }
            }
        }

        //- A method to clear the file system cache and optionally the standby list
        public static void ClearFileSystemCache(bool clearStandby)
        {
            try
            {
                //- Try to increase the quota privilege
                if (SetIncreasePrivilege(SeIncreaseQuotaName))
                {
                    uint result; //- Result of the system call
                    int cacheSize; //- Size of the cache information structure
                    GCHandle handle; //- Handle to the pinned object

                    //- Check OS bit size and create the cacheInfo variable

                    object cacheInfo = Environment.Is64BitOperatingSystem switch
                    {
                        true => new SYSTEM_CACHE_INFORMATION
                        {
                            MinimumWorkingSet = uint.MaxValue,
                            MaximumWorkingSet = uint.MaxValue
                        },

                        false => new SYSTEM_CACHE_INFORMATION_64_BIT
                        {
                            MinimumWorkingSet = -1L,
                            MaximumWorkingSet = -1L
                        }
                    };

                    cacheSize = Marshal.SizeOf(cacheInfo); //- Cache structure size
                    handle = GCHandle.Alloc(cacheInfo, GCHandleType.Pinned); //- Pin structure and get a handle
                    result = NtSetSystemInformation(SystemFileCacheInformation, handle.AddrOfPinnedObject(), cacheSize);
                    handle.Free(); //- Free the handle

                    // Check the result and throw an exception if it is not zero
                    if (result != 0)
                    { throw new Exception("NtSetSystemInformation(SystemFileCacheInformation) error: ", new Win32Exception(Marshal.GetLastWin32Error())); }
                }

                // If the clear standby list parameter is true and the profile privilege can be increased, then clear the standby list
                if (clearStandby && SetIncreasePrivilege(SeProfileSingleName))
                {
                    int memStandbySize = Marshal.SizeOf(MemoryPurgeStandbyList); //- Size of memory standby list
                    GCHandle handle = GCHandle.Alloc(MemoryPurgeStandbyList, GCHandleType.Pinned); //- Pin structure and get a handle
                    uint result = NtSetSystemInformation(SystemMemoryListInformation, handle.AddrOfPinnedObject(), memStandbySize);
                    handle.Free(); //- Free the handle

                    if (result != 0) //- Result should be zero
                    { throw new Exception("NtSetSystemInformation(SystemMemoryListInformation) Exception: ", new Win32Exception(Marshal.GetLastWin32Error())); }
                }
            }
            catch (Exception ex)
            {
                string outText = "Failed to clear File System Cache.";
                Console.WriteLine(outText);
                //- If the output variable is true, print the exception details
                if (output) { Console.WriteLine($"\n{outText}\n{ex.ToString()}"); }
                if (logEnabled) { Log($"{outText}\n{ex.ToString()}"); }
            }
        }

        //- Set Increase Priv, bool return success or fail
        private static bool SetIncreasePrivilege(string privName)
        {
            try
            {
                using (WindowsIdentity winID = WindowsIdentity.GetCurrent(TokenAccessLevels.Query | TokenAccessLevels.AdjustPrivileges))
                {
                    TokPriv1Luid tok = new TokPriv1Luid();
                    tok.Count = 1;
                    tok.Luid = 0L;
                    tok.Attr = SE_PRIVILEGE_ENABLED;

                    //- Looks up the LUID of the privilege
                    if (!LookupPrivilegeValue(null, privName, ref tok.Luid))
                    { throw new Exception("LookupPrivilegeValue error: ", new Win32Exception(Marshal.GetLastWin32Error())); }

                    //- Bool variable indicates whether adjusting the privilege failed or not
                    if (!AdjustTokenPrivileges(winID.Token, false, ref tok, 0, IntPtr.Zero, IntPtr.Zero))
                    { throw new Exception("AdjustTokenPrivileges error: ", new Win32Exception(Marshal.GetLastWin32Error())); }
                    else { return true; }
                }
            }
            catch (Exception ex) { Console.WriteLine($"{(output ? $"Fatal error occured, cannot continue.\n{ex.Message}" : "Fatal SePrivilege error occurred, cannot continue. Press any key to exit.")}"); }
            return false;
        }
    }
}