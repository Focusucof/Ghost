using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EmbedIO;
using EmbedIO.Actions;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Ghost {

    internal class ConfigProxy {

        private readonly HttpClient _client = new HttpClient();
        internal int ConfigPort {get;}

        internal event EventHandler<ChatServerEventArgs> PatchedChatServer;

        internal class ChatServerEventArgs : EventArgs {

            internal string ChatHost{get; set;}
            internal int ChatPort {get; set;}

        }

        /**
         * Starts a new client configuration proxy at a random port. The proxy will modify any responses
         * to point the chat servers to our local setup. This function returns the random port that the HTTP
         * server is listening on.
         */
        internal ConfigProxy(string configUrl, int chatPort) {

            //Find a free port
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint) l.LocalEndpoint).Port;
            l.Stop();

            var server = new WebServer(o => o
                .WithUrlPrefix("http://127.0.0.1:" + port)
                .WithMode(HttpListenerMode.EmbedIO))
                .WithModule(new ActionModule("/", HttpVerbs.Get,
                 ctx => ProxyAndRewriteResponse(configUrl, chatPort, ctx))
            );

            var thread = new Thread(() => {server.RunAsync().Wait();}) {IsBackground = true};
            thread.Start();

            ConfigPort = port;

        }

        private async Task ProxyAndRewriteResponse(string configUrl, int chatPort, IHttpContext ctx) {

            var url = configUrl + ctx.Request.RawUrl;
            var message = new HttpRequestMessage(HttpMethod.Get, url);

            message.Headers.TryAddWithoutValidation("User-Agent", ctx.Request.Headers["user-agent"]);

            //Add auth for player config
            if(ctx.Request.Headers["x-riot-entitlements-jwt"] != null) {

                message.Headers.TryAddWithoutValidation("X-Riot-Entitlements-JWT", ctx.Request.Headers["x-riot-entitlements-jwt"]);

            }

            if (ctx.Request.Headers["authorization"] != null) {

                message.Headers.TryAddWithoutValidation("Authorization", ctx.Request.Headers["authorization"]);

            }

            var result = await _client.SendAsync(message);
            var content = await result.Content.ReadAsStringAsync();
            var modifiedContent = content;
            Trace.WriteLine("ORIGINAL CLIENTCONFIG: " + content);

            try {

                var configObject = (JsonObject) SimpleJson.DeserializeObject(content);

                string riotChatHost = null;
                var riotChatPort = 0;

                // Set fallback host to localhost.
                if (configObject.ContainsKey("chat.host")) {

                    // Save fallback host
                    riotChatHost = configObject["chat.host"].ToString();
                    configObject["chat.host"] = "127.0.0.1";

                }

                 // Set chat port.
                if (configObject.ContainsKey("chat.port")) {

                    riotChatPort = int.Parse(configObject["chat.port"].ToString());
                    configObject["chat.port"] = chatPort;

                }

                if (configObject.ContainsKey("chat.affinities")) {

                    var affinities = (JsonObject) configObject["chat.affinities"];
                    if ((bool) configObject["chat.affinity.enabled"]) {

                        var pasRequest = new HttpRequestMessage(HttpMethod.Get, "https://riot-geo.pas.si.riotgames.com/pas/v1/service/chat");
                        pasRequest.Headers.TryAddWithoutValidation("Authorization", ctx.Request.Headers["authorization"]);
                        var pasJwt = await (await _client.SendAsync(pasRequest)).Content.ReadAsStringAsync();
                        Trace.WriteLine("PAS TOKEN:" + pasJwt);
                        var affinity = new JsonWebToken(pasJwt).GetPayloadValue<string>("affinity");
                        // replace fallback host with host by player affinity
                        riotChatHost = affinities[affinity] as string;

                    }

                    foreach (var key in new List<string>(affinities.Keys)) {

                        affinities[key] = "127.0.0.1";

                    }

                }

                //Allow an invalid certificate
                if (configObject.ContainsKey("chat.allow_bad_cert.enabled")) {

                    configObject["chat.allow_bad_cert.enabled"] = true;

                }

                modifiedContent = SimpleJson.SerializeObject(configObject);
                Trace.WriteLine("MODIFIED CLIENTCONFIG: " + modifiedContent);

                if (riotChatHost != null && riotChatPort != 0) {

                    PatchedChatServer?.Invoke(this, new ChatServerEventArgs {ChatHost = riotChatHost, ChatPort = riotChatPort});

                }

            } catch(Exception ex) {

                Trace.WriteLine(ex);

                MessageBox.Show(
                    "Ghost was unable to access a Riot config. This normally happens because Riot changed something on their end. " +
                    ex,
                    StartupHandler.GhostTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error,
                    MessageBoxDefaultButton.Button1
                );

                Application.Exit();

            }

            var responseBytes = Encoding.UTF8.GetBytes(modifiedContent);

            ctx.Response.StatusCode = (int) result.StatusCode;
            ctx.Response.SendChunked = false;
            ctx.Response.ContentLength64 = responseBytes.Length;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.OutputStream.WriteAsync(responseBytes, 0, responseBytes.Length);
            ctx.Response.OutputStream.Close();

        }

    }

}
