using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Services.Sockets.Nsd;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Net;

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

        private static bool TryMatchBuiltin(string host, out IPAddress address)
        {
            foreach ((string pattern, IPAddress addr) in _builtinRedirects)
            {
                if (FileSystemName.MatchesSimpleExpression(pattern, host))
                {
                    address = addr;

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
