using System;
using System.Threading;

namespace Multiplayer.Server
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            bool tickStats = Array.IndexOf(args, "--tick-stats") >= 0;

            using (ManualResetEventSlim stopRequested = new ManualResetEventSlim(false))
            {
                Console.CancelKeyPress += (sender, eventArgs) =>
                {
                    eventArgs.Cancel = true;
                    stopRequested.Set();
                };

                GameServer server = new GameServer(tickStats);
                if (!server.Start())
                    return 1;

                server.Run(stopRequested);
                server.Stop();
            }

            return 0;
        }
    }
}
