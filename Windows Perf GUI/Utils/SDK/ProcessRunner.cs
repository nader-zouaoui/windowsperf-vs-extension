﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.InteropServices;

namespace Windows_Perf_GUI.Utils.SDK
{
    class ProcessRunner
    {
        #region Process Control
        private enum CtrlTypes : uint
        {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT,
            CTRL_CLOSE_EVENT,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AttachConsole(uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        static extern bool FreeConsole();

        [DllImport("kernel32.dll")]
        static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate HandlerRoutine, bool Add);
        delegate Boolean ConsoleCtrlDelegate(CtrlTypes CtrlType);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GenerateConsoleCtrlEvent(CtrlTypes dwCtrlEvent, uint dwProcessGroupId);
        #endregion Process Control

        #region Properties
        public OutputHandler StdOutput;

        public OutputHandler StdError;

        private Task _BackgroundProcessTask;

        private Process _BackgroundProcess;

        private readonly string _Path;

        private readonly IntPtr _ProcessorAffinity;

        private CancellationTokenSource _BackgroundProcessCancelationToken;
        #endregion Properties

        public ProcessRunner(string path, IntPtr processorAffinity)
        {
            _Path = path;
            StdOutput = new OutputHandler();
            StdError = new OutputHandler();
            _ProcessorAffinity = processorAffinity;
        }

        public Task StartProcessAsync(params string[] args)
        {
            _BackgroundProcessCancelationToken = new CancellationTokenSource();
            _BackgroundProcessTask = Task.Run(() => StartProcess(args), _BackgroundProcessCancelationToken.Token);
            return _BackgroundProcessTask;
        }

        public void StopProcess(bool force = false)
        {
            const int waitForExitTimeout = 2000;
            if (force) { ForceKillProcess(); return; }

            _BackgroundProcessCancelationToken.Cancel(true);
            if (!AttachConsole((uint)_BackgroundProcess.Id))
            {
                return;
            }

            // Disable Ctrl-C handling for our program
            SetConsoleCtrlHandler(null, true);

            // Sent Ctrl-C to the attached console
            GenerateConsoleCtrlEvent(CtrlTypes.CTRL_C_EVENT, 0);
            // Wait for the graceful end of the process.
            // Must wait here. If we don't wait and re-enable Ctrl-C handling below too fast, we might terminate ourselves.
            _BackgroundProcess.WaitForExit(waitForExitTimeout);

            // if the process did not exit by the end of the 'waitForExitTimeout' we force kill the process
            ForceKillProcess();

            FreeConsole();

            SetConsoleCtrlHandler(null, false);

        }

        private void StartProcess(string[] args)
        {
            _BackgroundProcess = new Process()
            {
                StartInfo = {
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                FileName = _Path,
                Arguments = string.Join(" ", args)
                },
                EnableRaisingEvents = true,
            };
            _BackgroundProcess.OutputDataReceived += new DataReceivedEventHandler(StdOutput.OutputhHandler);
            _BackgroundProcess.ErrorDataReceived += new DataReceivedEventHandler(StdError.OutputhHandler);
            _BackgroundProcess.Start();
            if (_ProcessorAffinity != null)
            {
                _BackgroundProcess.ProcessorAffinity = _ProcessorAffinity;
            }
            _BackgroundProcess.BeginOutputReadLine();
            _BackgroundProcess.BeginErrorReadLine();
            _BackgroundProcess.WaitForExit();
        }


        private void ForceKillProcess()
        {
            _BackgroundProcessCancelationToken.Cancel(true);
            if (_BackgroundProcess != null && !_BackgroundProcess.HasExited)
            {
                _BackgroundProcess.CancelOutputRead();
                _BackgroundProcess.CancelErrorRead();
                _BackgroundProcess.Kill();
            }
        }

        ~ProcessRunner()
        {
            // kill the running process before garbage collection
            ForceKillProcess();
        }
    }
}
