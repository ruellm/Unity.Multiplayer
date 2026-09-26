using System;
using System.Threading;

namespace Multiplayer.Server
{
    public static class Program
    {
        public static int Main()
        {
            using (ManualResetEventSlim stopRequested = new ManualResetEventSlim(false))
            {
                Console.CancelKeyPress += (sender, args) =>
                {
                    args.Cancel = true;
                    stopRequested.Set();
                };

                GameServer server = new GameServer();
                if (!server.Start())
                    return 1;

                server.Run(stopRequested);
                server.Stop();
            }

            return 0;
        }
    }
}
