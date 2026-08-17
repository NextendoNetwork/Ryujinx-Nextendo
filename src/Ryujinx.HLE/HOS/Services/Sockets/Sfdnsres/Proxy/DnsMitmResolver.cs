using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Services.Sockets.Nsd;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Net;
using System.Threading;

namespace Ryujinx.HLE.HOS.Services.Sockets.Sfdnsres.Proxy
{
    class DnsMitmResolver
    {
        private const string HostsFilePath = "/atmosphere/hosts/default.txt";

        private static DnsMitmResolver _instance;
        public static DnsMitmResolver Instance => _instance ??= new DnsMitmResolver();

        private readonly Dictionary<string, IPAddress> _mitmHostEntries = new();

        // [Nextendo] Reverse map IP -> last hostname we redirected to it. Used so the
        // emulated SSL can send the correct SNI when the game opens a TLS
        // connection by IP without setting a hostname — otherwise our reverse-proxy (which
        // routes by SNI) can't reach the right backend and the connection is dropped. Some titles hit
        // this: they connect to the redirected IP with an EMPTY SNI.
        public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> LastHostForIp = new();

        // [Nextendo] MEMOIRE PAR PORT de la derniere redirection.
        //
        // Certains clients en ligne perdent l'adresse en desassemblant l'addrinfo qu'on leur rend : il ne
        // leur reste que le port, et la connexion part vers 0.0.0.0 (voir le repli de ManagedSocket.Connect).
        // Retenir « la derniere redirection resolue », toutes destinations confondues, ne suffit pas : cette
        // valeur unique est ecrasee par la resolution suivante, y compris celle d'un tout autre service.
        // Mesure du 2026-08-15, journal d'un testeur, une seule session : sur vingt-deux connexions vers le
        // port du service de session, douze partaient vers le serveur d'un AUTRE jeu et dix vers le bon —
        // d'ou une jonction en partie privee qui aboutissait une fois sur deux, sans que rien ne varie cote
        // serveur.
        //
        // Le port, lui, identifie sans ambiguite le service vise. On retient donc la redirection PAR PORT, et
        // le repli ne peut plus se tromper de destination.
        public static readonly System.Collections.Concurrent.ConcurrentDictionary<int, IPAddress> RedirectionParPort = new();

        public static void NoterRedirection(int port, IPAddress ip)
        {
            if (ip != null && port > 0)
            {
                RedirectionParPort[port] = ip;
            }
        }

        /// <summary>
        /// [Nextendo] Repli d'adresse pour un port DONNE — et seulement s'il en existe un.
        ///
        /// ⚠️ Rend null quand ce port n'a jamais ete resolu. C'est ESSENTIEL pour le jeu en pair-a-pair : un
        /// socket P2P vise le port d'une AUTRE CONSOLE, pour lequel aucune redirection DNS n'existe. En
        /// retombant sur « la derniere redirection connue », on connectait ce socket a un de nos serveurs au
        /// lieu du pair — mesure du 2026-08-15 : 3 897 envois UDP sans destination utile, zero paquet vers le
        /// relais STUN, et le jeu concluait EstablishP2PSessionFailed puis quittait la partie (ErrorLevel
        /// NeedLeaveSession).
        ///
        /// Le repli n'existe que pour rattraper les connexions gRPC vers des services que l'on redirige, dont
        /// l'adresse se perd a la deserialisation de l'addrinfo. Hors de ces ports, ne rien substituer : mieux
        /// vaut laisser l'adresse telle quelle que d'envoyer le trafic d'un pair vers une machine qui n'a rien
        /// a voir.
        /// </summary>
        public static IPAddress RedirectionPour(int port)
        {
            return RedirectionParPort.TryGetValue(port, out IPAddress ip) ? ip : null;
        }

        // [Nextendo] Retenue de la PREMIERE resolution d'un hote npln, contre un blocage au demarrage.
        //
        // Le jeu se connecte tout seul a son lobby en ligne ~1 min 30 apres le lancement, c'est-a-dire en
        // plein dans la rafale de compilation JIT la plus lourde du demarrage. La mise en place de la
        // connexion de grpc-core se bloque quand elle chevauche cette contention (son scrutateur se met en
        // attente et son executeur cale au milieu de la preparation du socket, sans jamais emettre le
        // connect) — alors que la meme initialisation, quelques minutes plus tard sur un systeme calme,
        // aboutit. On retient donc la premiere resolution d'un hote npln le temps que la rafale retombe.
        private static bool _nplnDelayDone;
        private static readonly object _nplnDelayLock = new();

        private static void MaybeDelayNplnInit(string host)
        {
            if (_nplnDelayDone || host == null || !host.Contains("npln", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            lock (_nplnDelayLock)
            {
                if (_nplnDelayDone)
                {
                    return;
                }

                _nplnDelayDone = true;

                // Attente ADAPTATIVE (et non une pause de duree fixe : celle-ci gaspille du temps ou retombe
                // en pleine rafale, au hasard). On sonde le compteur global d'activite du JIT et on repart
                // des que le nombre de nouvelles traductions par fenetre passe sous un seuil « calme » — la
                // connexion demarre donc apres la rafale quelle qu'en soit la duree (le cache PPTC la
                // raccourcit beaucoup sur un demarrage deja chauffe). NEXTENDO_NPLN_DELAY_MS plafonne
                // l'attente (0 supprime la retenue).
                int maxWaitMs = 120000;
                string env = Environment.GetEnvironmentVariable("NEXTENDO_NPLN_DELAY_MS");
                if (env != null && int.TryParse(env, out int parsed) && parsed >= 0)
                {
                    maxWaitMs = parsed;
                }

                if (maxWaitMs > 0)
                {
                    const int MinWaitMs = 3000;    // toujours laisser un peu d'air a la connexion
                    const int WindowMs = 500;
                    const long CalmThreshold = 20; // moins de traductions que ca par fenetre => la rafale est passee

                    Logger.Info?.Print(LogClass.ServiceBsd, $"[Nextendo] Retenue de la premiere resolution npln jusqu'a ce que la rafale JIT retombe (au plus {maxWaitMs} ms).");

                    int initialWait = Math.Min(MinWaitMs, maxWaitMs);
                    Thread.Sleep(initialWait);
                    int waited = initialWait;

                    while (waited < maxWaitMs)
                    {
                        long before = ARMeilleure.Translation.Translator.JitActivityCounter;
                        Thread.Sleep(WindowMs);
                        waited += WindowMs;

                        if (ARMeilleure.Translation.Translator.JitActivityCounter - before < CalmThreshold)
                        {
                            break;
                        }
                    }

                    Logger.Info?.Print(LogClass.ServiceBsd, $"[Nextendo] Retenue npln terminee apres {waited} ms (JIT calme).");
                }
            }
        }

        // [Nextendo] Built-in redirect rules: Nintendo online hostnames are pointed at the
        // Nextendo server so the client needs no hosts-file configuration. Checked in order
        // (most specific first). Server addresses are NOT baked into this open-source tree: they
        // come from environment variables (NEXTENDO_SERVER_IP, NEXTENDO_NAT_IP). Official builds
        // set them; unconfigured builds fall back to loopback, so no address is published here.
        private static readonly (string Pattern, IPAddress Address)[] _builtinRedirects =
        {
            ("nncs2-*.n.n.srv.nintendo.net", ResolveConfiguredIp("NEXTENDO_NAT_IP")),
            ("*.nintendo.net",     ResolveConfiguredIp("NEXTENDO_SERVER_IP")),
            ("*.nintendo.com",     ResolveConfiguredIp("NEXTENDO_SERVER_IP")),
            ("*.nintendowifi.net", ResolveConfiguredIp("NEXTENDO_SERVER_IP")),
            ("*.nintendo.co.jp",   ResolveConfiguredIp("NEXTENDO_SERVER_IP")),
        };

        // Reads a server address from an environment variable; falls back to loopback so no
        // infrastructure address is hardcoded in this open-source tree.
        private static IPAddress ResolveConfiguredIp(string envVar)
        {
            string value = Environment.GetEnvironmentVariable(envVar);

            if (!IPAddress.TryParse(value, out IPAddress address))
            {
                Logger.Warning?.PrintMsg(LogClass.ServiceBsd, $"DnsMitmResolver: {envVar} not set, falling back to loopback — Nintendo hosts will resolve to 127.0.0.1");

                return IPAddress.Loopback;
            }

            return address;
        }

        private static bool _loopbackRedirectReported;

        private static bool TryMatchBuiltin(string host, out IPAddress address)
        {
            foreach ((string pattern, IPAddress addr) in _builtinRedirects)
            {
                if (FileSystemName.MatchesSimpleExpression(pattern, host))
                {
                    address = addr;

                    // [Nextendo] The warning at start-up is easy to miss, and the consequence only shows up
                    // much later as an unexplained network error from the game. Say it again, once, at the
                    // moment a hostname the game needs is actually pointed at loopback.
                    if (IPAddress.IsLoopback(addr) && !_loopbackRedirectReported)
                    {
                        _loopbackRedirectReported = true;

                        Logger.Error?.PrintMsg(LogClass.ServiceBsd,
                            $"[Nextendo] \"{host}\" is being redirected to {addr} because the server address is not configured. " +
                            "Online play cannot work: set NEXTENDO_SERVER_IP (and NEXTENDO_NAT_IP) before launching, " +
                            "or bake them into the build with distribution/nextendo/bake_release.py.");
                    }

                    return true;
                }
            }

            address = null;

            return false;
        }

        // Specificity for tie-breaking hosts-file matches: fewer wildcards wins, then the longer
        // pattern wins. Avoids relying on (unspecified) Dictionary enumeration order.
        private static int CountWildcards(string s)
        {
            int count = 0;

            foreach (char c in s)
            {
                if (c == '*' || c == '?')
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsMoreSpecific(string candidate, string current)
        {
            int candidateWildcards = CountWildcards(candidate);
            int currentWildcards = CountWildcards(current);

            if (candidateWildcards != currentWildcards)
            {
                return candidateWildcards < currentWildcards;
            }

            return candidate.Length > current.Length;
        }

        public void ReloadEntries(ServiceCtx context)
        {
            string sdPath = FileSystem.VirtualFileSystem.GetSdCardPath();
            string filePath = FileSystem.VirtualFileSystem.GetFullPath(sdPath, HostsFilePath);

            _mitmHostEntries.Clear();

            if (File.Exists(filePath))
            {
                using FileStream fileStream = File.Open(filePath, FileMode.Open, FileAccess.Read);
                using StreamReader reader = new(fileStream);

                while (!reader.EndOfStream)
                {
                    string line = reader.ReadLine();

                    if (line == null)
                    {
                        break;
                    }

                    // Ignore comments and empty lines
                    if (line.StartsWith('#') || line.Trim().Length == 0)
                    {
                        continue;
                    }

                    string[] entry = line.Split([' ', '\t'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

                    // Hosts file example entry:
                    // 127.0.0.1  localhost loopback

                    // 0. Check the size of the array
                    if (entry.Length < 2)
                    {
                        Logger.Warning?.PrintMsg(LogClass.ServiceBsd, $"Invalid entry in hosts file: {line}");

                        continue;
                    }

                    // 1. Parse the address
                    if (!IPAddress.TryParse(entry[0], out IPAddress address))
                    {
                        Logger.Warning?.PrintMsg(LogClass.ServiceBsd, $"Failed to parse IP address in hosts file: {entry[0]}");

                        continue;
                    }

                    // 2. Check for AMS hosts file extension: "%"
                    for (int i = 1; i < entry.Length; i++)
                    {
                        entry[i] = entry[i].Replace("%", IManager.NsdSettings.Environment);
                    }

                    // 3. Add hostname to entry dictionary (updating duplicate entries)
                    foreach (string hostname in entry[1..])
                    {
                        _mitmHostEntries[hostname] = address;
                    }
                }
            }
        }

        // [Nextendo] True if this host is one we deliberately redirect (built-in rule or
        // hosts file, wildcards included). Used so an explicit redirect wins over the anti-ban DNS
        // blacklist: a host we point at our own server must not be blocked.
        public bool IsHostMitmd(string host)
        {
            if (TryMatchBuiltin(host, out _))
            {
                return true;
            }

            foreach (string pattern in _mitmHostEntries.Keys)
            {
                if (FileSystemName.MatchesSimpleExpression(pattern, host))
                {
                    return true;
                }
            }

            return false;
        }

        public IPHostEntry ResolveAddress(string host)
        {
            // [Nextendo] Retenir la toute premiere resolution npln le temps que la rafale JIT du demarrage
            // retombe (voir MaybeDelayNplnInit) : la connexion gRPC se bloque si elle demarre en pleine rafale.
            MaybeDelayNplnInit(host);

            // [Nextendo] ADRESSE IP LITTERALE : il n'y a rien a resoudre, et surtout rien a renommer.
            //
            // Un jeu peut recevoir l'hote de sa partie dans le ticket de matchmaking sous forme d'adresse, et
            // la passer telle quelle a getaddrinfo. Sans cette branche, la resolution tombait jusqu'a
            // Dns.GetHostEntry, et le nom canonique rendu au jeu n'etait pas l'adresse demandee mais celui
            // d'un hote SANS RAPPORT — en pratique le nom interroge le plus souvent, puisque tous les noms
            // rediriges partagent la meme adresse.
            //
            // Mesure du 2026-08-13 : sur les trois points de terminaison ouverts par le jeu, le seul qui
            // fonctionnait etait le seul resolu par son vrai nom. Les autres montaient bien TCP+TLS+HTTP/2
            // puis n'emettaient JAMAIS de trame HEADERS et refermaient au bout d'~1 s.
            if (IPAddress.TryParse(host, out IPAddress litteral))
            {
                Logger.Debug?.PrintMsg(LogClass.ServiceBsd, $"Adresse litterale '{host}' : rendue telle quelle");

                // Volontairement PAS de LastHostForIp ici : une IP litterale n'apporte aucun nom, et en
                // inscrire un fausserait la table inverse utilisee ailleurs.
                return new IPHostEntry
                {
                    AddressList = [litteral],
                    HostName = host,
                    Aliases = [],
                };
            }

            // [Nextendo] An EXACT hosts-file entry (full hostname, no wildcard) overrides the built-in
            // wildcards — lets a private test server (e.g. a separate game instance on another host) be routed
            // by adding a single hosts line, without touching the built-in rules or rebuilding. Only
            // exact keys match here (Dictionary lookup), so wildcards still fall through to the ordered
            // built-in rules below and can never swallow the nncs2 NAT-check host.
            if (_mitmHostEntries.TryGetValue(host, out IPAddress exactHostsAddress))
            {
                Logger.Debug?.PrintMsg(LogClass.ServiceBsd, $"Redirecting '{host}' to: {exactHostsAddress} (hosts-file exact override)");
                LastHostForIp[exactHostsAddress.ToString()] = host;

                return new IPHostEntry
                {
                    AddressList = [exactHostsAddress],
                    HostName = host,
                    Aliases = [],
                };
            }

            // [Nextendo] Built-in ordered rules take precedence (deterministic; most
            // specific first) so the wildcard can never swallow the nncs2 NAT-check host.
            if (TryMatchBuiltin(host, out IPAddress builtinAddress))
            {
                // [Nextendo beta] Debug-level so the redirect targets don't appear in normal logs.
                Logger.Debug?.PrintMsg(LogClass.ServiceBsd, $"Redirecting '{host}' to: {builtinAddress} (built-in)");
                LastHostForIp[builtinAddress.ToString()] = host;

                return new IPHostEntry
                {
                    AddressList = [builtinAddress],
                    HostName = host,
                    Aliases = [],
                };
            }

            // Hosts-file entries: pick the MOST SPECIFIC matching pattern, not just the first one the
            // Dictionary happens to enumerate (enumeration order is not guaranteed, so a broad wildcard
            // could otherwise win over a specific host).
            string bestPattern = null;
            IPAddress bestAddress = null;

            foreach (KeyValuePair<string, IPAddress> hostEntry in _mitmHostEntries)
            {
                // Check for AMS hosts file extension: "*"
                // NOTE: MatchesSimpleExpression also allows "?" as a wildcard
                if (FileSystemName.MatchesSimpleExpression(hostEntry.Key, host))
                {
                    if (bestPattern == null || IsMoreSpecific(hostEntry.Key, bestPattern))
                    {
                        bestPattern = hostEntry.Key;
                        bestAddress = hostEntry.Value;
                    }
                }
            }

            if (bestPattern != null)
            {
                Logger.Debug?.PrintMsg(LogClass.ServiceBsd, $"Redirecting '{host}' to: {bestAddress}");
                LastHostForIp[bestAddress.ToString()] = host;

                return new IPHostEntry
                {
                    AddressList = [bestAddress],
                    HostName = host,
                    Aliases = [],
                };
            }

            // No match has been found, resolve the host using regular dns
            return Dns.GetHostEntry(host);
        }
    }
}
