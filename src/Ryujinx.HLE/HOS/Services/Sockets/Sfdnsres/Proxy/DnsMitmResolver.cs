using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Services.Sockets.Nsd;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

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
            // Le serveur NEX de Mario Kart 8 Deluxe vit sur la meme machine que le
            // repondeur NAT ; il partage donc sa variable. Pose AVANT les jokers, sinon
            // *.nintendo.net l attraperait en premier.
            ("g2b309e01-lp1.s.n.srv.nintendo.net", ResolveConfiguredIp("NEXTENDO_NAT_IP")),
            // Splatoon 2's Pia rendezvous endpoint is served by the NAT responder;
            // keep it out of the general NEX/backend redirect below.
            ("g2df33d01-lp1.p.srv.nintendo.net", ResolveConfiguredIp("NEXTENDO_NAT_IP")),
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


        /// <summary>
        /// [Nextendo] Mode « serveur personnalise » : detourne la redirection vers le serveur du
        /// joueur au lieu du notre.
        ///
        /// Le controle NAT exige DEUX adresses publiques distinctes ; la regle nncs recoit donc
        /// la seconde adresse saisie, les autres la premiere. Si une adresse manque, on garde
        /// celle du build plutot que de dupliquer l autre : deux repondeurs a la meme adresse
        /// font echouer le controle NAT en silence.
        /// </summary>
        private static IPAddress Substituer(string pattern, IPAddress configuree)
        {
            if (!NextendoServerOverride.IsActive)
            {
                return configuree;
            }

            if (pattern.StartsWith("nncs", StringComparison.OrdinalIgnoreCase))
            {
                return NextendoServerOverride.NatAddress ?? configuree;
            }

            return NextendoServerOverride.ServerAddress ?? configuree;
        }
        private static bool TryMatchBuiltin(string host, out IPAddress address)
        {
            foreach ((string pattern, IPAddress addr) in _builtinRedirects)
            {
                if (FileSystemName.MatchesSimpleExpression(pattern, host))
                {
                    address = Substituer(pattern, addr);

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

            // [Nextendo] Mario Tennis Aces (NEX server id g23932a00) resolves through a real
            // GeoDNS hostname instead of the fixed NEXTENDO_SERVER_IP, so players get routed to
            // whichever of the two live Tennis nodes is actually closest to them. Every other
            // title is unaffected -- the second node doesn't run their servers. Gated on
            // !NextendoServerOverride.HorsNextendo, not IsActive: IsActive also requires the
            // typed address to parse, so a fat-fingered custom-server IP would leave IsActive
            // false and this branch would still fire, sending them to our nameserver and onto
            // our nodes. HorsNextendo is true as soon as the box is ticked regardless of what
            // was typed -- exactly the "don't silently put them back on our servers" case its
            // own doc comment describes, and what every other Nextendo redirect gates on
            // (caught by Kazu in review). NEXTENDO_TENNIS_GEODNS=0 disables this and falls
            // through to the normal built-in redirect below. The lookup is bounded to 2s: the
            // GeoDNS delegation has only one nameserver behind it, and Dns.GetHostEntry has no
            // native timeout, so an unbounded call here could hang indefinitely if that
            // nameserver is unreachable. Falls through the same way on any resolution failure or
            // timeout rather than failing the connection outright.
            if (!NextendoServerOverride.HorsNextendo &&
                host.Contains("g23932a00", StringComparison.OrdinalIgnoreCase) &&
                Environment.GetEnvironmentVariable("NEXTENDO_TENNIS_GEODNS") != "0")
            {
                try
                {
                    Task<IPHostEntry> lookup = Task.Run(() => Dns.GetHostEntry("tennis-geo.nextendo.network"));

                    if (lookup.Wait(TimeSpan.FromSeconds(2)))
                    {
                        IPHostEntry geoEntry = lookup.Result;

                        Logger.Debug?.PrintMsg(LogClass.ServiceBsd, $"Redirecting '{host}' to: {geoEntry.AddressList[0]} (Tennis GeoDNS)");
                        LastHostForIp[geoEntry.AddressList[0].ToString()] = host;

                        return new IPHostEntry
                        {
                            AddressList = geoEntry.AddressList,
                            HostName = host,
                            Aliases = [],
                        };
                    }

                    Logger.Warning?.PrintMsg(LogClass.ServiceBsd, "Tennis GeoDNS lookup timed out after 2s, falling back to fixed IP");
                }
                catch (Exception ex)
                {
                    Logger.Warning?.PrintMsg(LogClass.ServiceBsd, $"Tennis GeoDNS lookup failed, falling back to fixed IP: {ex.Message}");
                }
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
