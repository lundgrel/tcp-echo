using System;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TcpEcho.Shared
{
    /// <summary>Runs an async main loop as a Windows service; the token is cancelled when the service is stopped.</summary>
    public sealed class EchoService : ServiceBase
    {
        private readonly Func<CancellationToken, Task<int>> _run;
        private CancellationTokenSource _cts;
        private Task _runTask;

        public EchoService(string serviceName, Func<CancellationToken, Task<int>> run)
        {
            ServiceName = serviceName;
            CanStop = true;
            _run = run;
        }

        protected override void OnStart(string[] args)
        {
            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            _runTask = Task.Run(async () =>
            {
                int exitCode = 1;
                try
                {
                    exitCode = await _run(token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    exitCode = 1;
                }

                // The loop ended on its own (e.g. could not listen): stop the service so the SCM shows it.
                if (!token.IsCancellationRequested)
                {
                    ExitCode = exitCode;
                    Stop();
                }
            });
        }

        protected override void OnStop()
        {
            if (_cts == null)
                return;

            RequestAdditionalTime(15000);
            _cts.Cancel();
            try
            {
                _runTask.Wait(TimeSpan.FromSeconds(15));
            }
            catch (AggregateException)
            {
            }
        }
    }

    /// <summary>The install / uninstall / start / stop command line verbs.</summary>
    public static class ServiceCommands
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        /// <summary>Returns the process exit code when args[0] is a service verb, otherwise null.</summary>
        public static int? TryHandle(string[] args, string serviceName, string displayName, string description)
        {
            if (args.Length == 0)
                return null;

            try
            {
                switch (args[0].ToLowerInvariant())
                {
                    case "install":
                        return Install(serviceName, displayName, description, args.Skip(1));
                    case "uninstall":
                        return Uninstall(serviceName);
                    case "start":
                        return Control(serviceName, start: true);
                    case "stop":
                        return Control(serviceName, start: false);
                    default:
                        return null;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("{0} failed: {1}", args[0], ex.InnerException != null ? ex.InnerException.Message : ex.Message);
                return 1;
            }
        }

        private static int Install(string serviceName, string displayName, string description, System.Collections.Generic.IEnumerable<string> extraArgs)
        {
            var binPath = new StringBuilder();
            binPath.Append('"').Append(Process.GetCurrentProcess().MainModule.FileName).Append('"');
            foreach (string arg in extraArgs)
                binPath.Append(' ').Append(arg.Contains(" ") ? "\"" + arg + "\"" : arg);

            int rc = Sc("create \"" + serviceName + "\" binPath= \"" + binPath.ToString().Replace("\"", "\\\"") + "\" start= auto DisplayName= \"" + displayName + "\"");
            if (rc != 0)
                return rc;

            Sc("description \"" + serviceName + "\" \"" + description + "\"");
            Console.WriteLine("Installed service '{0}'. Run '{1} start' to start it.", serviceName, Process.GetCurrentProcess().ProcessName);
            return 0;
        }

        private static int Uninstall(string serviceName)
        {
            // Best effort: a running service is marked for deletion but lingers until it stops.
            try
            {
                Control(serviceName, start: false);
            }
            catch (Exception)
            {
            }

            return Sc("delete \"" + serviceName + "\"");
        }

        private static int Control(string serviceName, bool start)
        {
            using (var controller = new ServiceController(serviceName))
            {
                ServiceControllerStatus target = start ? ServiceControllerStatus.Running : ServiceControllerStatus.Stopped;
                if (controller.Status == target)
                {
                    Console.WriteLine("Service '{0}' is already {1}.", serviceName, target);
                    return 0;
                }

                if (start)
                    controller.Start();
                else
                    controller.Stop();

                controller.WaitForStatus(target, Timeout);
                Console.WriteLine("Service '{0}' {1}.", serviceName, start ? "started" : "stopped");
                return 0;
            }
        }

        private static int Sc(string arguments)
        {
            var info = new ProcessStartInfo("sc.exe", arguments) { UseShellExecute = false };
            using (Process process = Process.Start(info))
            {
                process.WaitForExit();
                return process.ExitCode;
            }
        }
    }
}
