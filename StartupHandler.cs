using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Windows.Forms;
using Ghost.Properties;


namespace Ghost {

    internal static class StartupHandler {

        internal static string GhostTitle => "Ghost " + Utils.GhostVersion;

        [STAThread]
        private static void Main(string[] args) {

            Console.WriteLine( Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)));
            Thread.Sleep(5000);

            //AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledException;
            try {

                StartGhost(args);

            } catch(Exception ex) {

                Trace.WriteLine(ex);

                MessageBox.Show(
                    "Ghost has encountered and error and couldn't start properly. " + 
                    ex,
                    GhostTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error,
                    MessageBoxDefaultButton.Button1
                );

            }

        }

        private static void StartGhost(string[] args) {

            

            if(Utils.bIsClientRunning()) {

                var result = MessageBox.Show(
                    "The Riot Client is currently running. In order to mask your online status, the Riot Client needs to be started by Ghost. " +
                    "Do you want Ghost to stop the Riot Client and games launched by it, so that it can restart with the proper configuration?",
                    GhostTitle,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1
                );

                if(result != DialogResult.Yes) return;
                Utils.KillProcesses();
                Thread.Sleep(2000); //wait for Riot Client process to die

            }

            try {

                File.WriteAllText(Path.Combine(Utils.DataDir, "debug.log"), string.Empty);
                Debug.Listeners.Add(new TextWriterTraceListener(Path.Combine(Utils.DataDir, "debug.log")));
                Debug.AutoFlush = true;
                Trace.WriteLine(GhostTitle);

            } catch {
                // ignored; just don't save logs if file is already being accessed
            }

            //Open a port for chat proxy
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint) listener.LocalEndpoint).Port;

            //Get Riot Client path
            var riotClientPath = Utils.GetRiotClientPath();

            if (riotClientPath == null) {

                MessageBox.Show(
                    "Deceive was unable to find the path to the Riot Client. ",
                    GhostTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error,
                    MessageBoxDefaultButton.Button1
                );

                return;
            }

            var proxyServer = new ConfigProxy("https://clientconfig.rpg.riotgames.com", port);

            var startArgs = new ProcessStartInfo {

                FileName = riotClientPath,
                Arguments = $"--client-config-url=\"http://127.0.0.1:{proxyServer.ConfigPort}\" --launch-product=valorant --launch-patchline=live"

            };

            var riotClient = Process.Start(startArgs);

            //Kill Ghost when Riot Client exits to prevent zombie process
            if(riotClient != null) {

                riotClient.EnableRaisingEvents = true;
                riotClient.Exited += (sender, argsv) => {

                    Trace.WriteLine("Exiting on Riot Client exit.");
                    Environment.Exit(0);

                };

            }

            string chatHost = null;
            var chatPort = 0;
            proxyServer.PatchedChatServer += (sender, argsv) => {

                chatHost = argsv.ChatHost;
                chatPort = argsv.ChatPort;

            };

            var incoming = listener.AcceptTcpClient();

            var sslIncoming = new SslStream(incoming.GetStream());
            var cert = new X509Certificate2(Resources.Certificate);
            sslIncoming.AuthenticateAsServer(cert);

            if(chatHost == null) {

                MessageBox.Show( 
                    "Ghost was unable to find Riot's chat server",
                    GhostTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error,
                    MessageBoxDefaultButton.Button1
                );

                return;
            }

            var outgoing = new TcpClient(chatHost, chatPort);
            var sslOutgoing = new SslStream(outgoing.GetStream());
            sslOutgoing.AuthenticateAsClient(chatHost);

            var mainController = new MainController();
            mainController.StartThreads(sslIncoming, sslOutgoing);
            mainController.ConnectionErrored += (sender, argsv) => {

                Trace.WriteLine("Trying to reconnect.");
                sslIncoming.Close();
                sslOutgoing.Close();
                incoming.Close();
                outgoing.Close();

                incoming = listener.AcceptTcpClient();
                sslIncoming = new SslStream(incoming.GetStream());
                sslIncoming.AuthenticateAsServer(cert);
                while (true) {

                    try {

                        outgoing = new TcpClient(chatHost, chatPort);
                        break;

                    } catch (SocketException e) {

                        Trace.WriteLine(e);
                        var result = MessageBox.Show(
                            "Unable to reconnect to the chat server. Please check your internet connection.",
                            GhostTitle,
                            MessageBoxButtons.RetryCancel,
                            MessageBoxIcon.Error,
                            MessageBoxDefaultButton.Button1
                        );
                        if (result == DialogResult.Cancel) {

                            Environment.Exit(0);

                        }

                    }

                }

                sslOutgoing = new SslStream(outgoing.GetStream());
                sslOutgoing.AuthenticateAsClient(chatHost);
                mainController.StartThreads(sslIncoming, sslOutgoing);

            };

            Application.EnableVisualStyles();
            Application.Run(mainController);

        }

        [HandleProcessCorruptedStateExceptions, SecurityCritical]
        private static void CurrentDomainOnUnhandledException(object sender, UnhandledExceptionEventArgs e) {

            //Log all unhandled exceptions
            Trace.WriteLine(e.ExceptionObject as Exception);
            Trace.WriteLine(Environment.StackTrace);

        }

    }

}
