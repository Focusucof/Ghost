using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Windows.Forms;

namespace Ghost {

    internal static class Utils {

        internal static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ghost");

        internal static string GhostVersion {

            get {

                var version = Assembly.GetEntryAssembly().GetName().Version;
                return "v" + version.Major + "." + version.Minor + "." + version.Build;

            }

        }

        private static IEnumerable<Process> GetProcesses() {

            var riotCandidates = Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName).Where(process => process.Id != Process.GetCurrentProcess().Id).ToList();
            riotCandidates.AddRange(Process.GetProcessesByName("LeagueClient"));
            riotCandidates.AddRange(Process.GetProcessesByName("LoR"));
            riotCandidates.AddRange(Process.GetProcessesByName("VALORANT-Win64-Shipping"));
            riotCandidates.AddRange(Process.GetProcessesByName("RiotClientServices"));
            return riotCandidates;

        }

        public static bool bIsClientRunning() {

            return GetProcesses().Any();

        }

        public static void KillProcesses() {

            foreach(var process in GetProcesses()) {

                process.Refresh();
                if(process.HasExited) continue;
                process.Kill();
                process.WaitForExit();

            }

        }

        public static string GetRiotClientPath() {

            var installPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Riot Games/RiotClientInstalls.json");
            if (!File.Exists(installPath)) {
                
                return null;

            }

            var data = (JsonObject) SimpleJson.DeserializeObject(File.ReadAllText(installPath));
            var rcPaths = new List<string>();

            if (data.ContainsKey("rc_default")) rcPaths.Add(data["rc_default"].ToString());
            if (data.ContainsKey("rc_live")) rcPaths.Add(data["rc_live"].ToString());
            if (data.ContainsKey("rc_beta")) rcPaths.Add(data["rc_beta"].ToString());

            return rcPaths.FirstOrDefault(File.Exists);

        }

    }

}
