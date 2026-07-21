using Ryujinx.Common.Logging;
using System;

namespace Ryujinx.Common.Configuration
{
    /// <summary>
    /// [Nextendo] THE single place that decides which server the emulator talks to.
    ///
    /// This is a security decision, not a formatting one: every Nextendo request carries the
    /// account's bearer token, so whoever picks this url picks who receives that token — and the
    /// token is full access to the account. It used to be re-implemented in six files, each
    /// accepting any value of the NEXTENDO_API environment variable, which meant
    /// `NEXTENDO_API=http://evil.example` quietly shipped the token, in cleartext, to whoever
    /// asked. Setting an env var already requires running code as the user, so this was never a
    /// privilege escalation — but env vars live in .bat files, shortcuts and "configs" pasted in
    /// chat, which makes "run this to get faster servers" a realistic way to harvest tokens.
    ///
    /// The override is kept, because local testing genuinely needs it, but it is now restricted to
    /// destinations that cannot be a stranger's server.
    /// </summary>
    public static class NextendoEndpoint
    {
        /// <summary>HTTPS on 443: the account server is proxied here and it is reachable everywhere.</summary>
        public const string Canonical = "https://nextendo.network";

        private const string EnvVar = "NEXTENDO_API";

        private static string _cached;
        private static bool _resolved;

        /// <summary>The Nextendo API base url, without a trailing slash.</summary>
        public static string BaseUrl()
        {
            if (_resolved)
            {
                return _cached;
            }

            _cached = Resolve();
            _resolved = true;

            return _cached;
        }

        private static string Resolve()
        {
            string raw = Environment.GetEnvironmentVariable(EnvVar);

            // Smart-quote pastes (from Discord, typically) glue a curly quote to the host, which
            // IDNA then turns into "nextendo.xn--network-b46c" and DNS fails.
            raw = (raw ?? string.Empty).Trim().Trim('"', '\'', '“', '”', '‘', '’');

            if (raw.Length == 0)
            {
                return Canonical;
            }

            if (!Uri.TryCreate(raw, UriKind.Absolute, out Uri uri))
            {
                Reject(raw, "not a valid absolute url");

                return Canonical;
            }

            // Loopback: a developer pointing at their own machine. Any scheme, any port — nothing
            // leaves the machine, so there is nothing to protect against here.
            if (uri.IsLoopback)
            {
                return raw.TrimEnd('/');
            }

            // Anything else must be Nextendo, over TLS. http:// would put the token on the wire in
            // cleartext even against the real server.
            if (uri.Scheme != Uri.UriSchemeHttps)
            {
                Reject(raw, "only https is accepted for a remote server");

                return Canonical;
            }

            if (!IsNextendoHost(uri.Host))
            {
                Reject(raw, "host is not a nextendo.network address");

                return Canonical;
            }

            return raw.TrimEnd('/');
        }

        /// <summary>
        /// nextendo.network, or a subdomain of it. Matched on the host with a leading-dot suffix
        /// check, never EndsWith("nextendo.network") — that also accepts "evilnextendo.network".
        /// </summary>
        private static bool IsNextendoHost(string host)
        {
            const string Domain = "nextendo.network";

            return host.Equals(Domain, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + Domain, StringComparison.OrdinalIgnoreCase);
        }

        private static void Reject(string value, string why)
        {
            // Loud on purpose: someone who set this deserves to know it was ignored, and someone
            // who did not set it deserves to see that something tried.
            Logger.Warning?.Print(LogClass.Application,
                $"[Nextendo] Ignoring {EnvVar}=\"{value}\" ({why}). Using {Canonical}. " +
                "Your account token is only ever sent to Nextendo.");
        }
    }
}
