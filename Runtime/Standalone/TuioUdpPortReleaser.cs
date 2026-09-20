using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace BeyondFutureOne.TuioClient
{
    /// <summary>
    /// TuioNet's UDP receiver waits on <see cref="UdpClient.ReceiveAsync"/> inside a background task.
    /// Cancelling that task does not abort the receive, so the bound port stays occupied until a datagram
    /// arrives. Sending a localhost packet unblocks the loop and lets the client dispose.
    /// </summary>
    internal static class TuioUdpPortReleaser
    {
        private static readonly byte[] WakePayload = { 0 };

        public static void Release(int port, int timeoutMilliseconds = 250)
        {
            if (port < 1 || port > 65535)
            {
                return;
            }

            WakeReceiver(port);

            var timeout = Math.Max(0, timeoutMilliseconds);
            var stopwatch = Stopwatch.StartNew();
            while (!IsPortFree(port))
            {
                if (stopwatch.ElapsedMilliseconds >= timeout)
                {
                    break;
                }

                WakeReceiver(port);
                Thread.Sleep(10);
            }
        }

        private static void WakeReceiver(int port)
        {
            try
            {
                using (var client = new UdpClient())
                {
                    client.Send(WakePayload, WakePayload.Length, IPAddress.Loopback.ToString(), port);
                }
            }
            catch (Exception)
            {
                // The receiver may already be gone; probing the port is what matters.
            }
        }

        private static bool IsPortFree(int port)
        {
            UdpClient probe = null;
            try
            {
                probe = new UdpClient(port);
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            finally
            {
                if (probe != null)
                {
                    probe.Close();
                    probe.Dispose();
                }
            }
        }
    }
}
