using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Kernel.Threading;
using Ryujinx.HLE.HOS.Services.Account.Acc.AsyncContext;
using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.HLE.HOS.Services.Account.Acc.AccountService
{
    class ManagerServer
    {
        // [Nextendo] When a Nextendo Network account is linked (via the in-app
        // "Connexion Nextendo Network" dialog), present its PERSISTENT principal id
        // as the NetworkServiceAccountId. The game uses this as the NEX LoginEx
        // username, so our auth server maps it to the real account (same PID, same
        // friend code, same friends across launches). Unlinked => the 0xcafe stub.
        //
        // ONLINE is allowed ONLY on the profile bound to the Nextendo account. Any other
        // local profile presents the 0xcafe stub -> the gated server refuses it -> that
        // profile stays OFFLINE. (Instance property: uses _userId = the profile being queried.)
        private long NetworkServiceAccountId =>
            NextendoAccount.IsLinked && !NextendoAccount.OnlineBlocked && IsBoundNextendoProfile
                ? (long)NextendoAccount.Pid
                : 0xcafe;

        // [Nextendo] Le NSA rendu a l'application en cours. Animal Crossing: New Horizons compare
        // le NSA annonce par chaque joueur a la liste d'amis nn::friends : il y recoit le
        // baasUserID du compte, celui d'une vraie Switch (NextendoBaasNsa), pour que Switch et
        // emulateur se reconnaissent comme amis. Le serveur ACNH reconnait encore l'emulateur au
        // jeton nx2 de l'id_token. Les autres jeux gardent le PID.
        private long NetworkServiceAccountIdFor(ServiceCtx context)
        {
            long id = NetworkServiceAccountId;
            if (id == 0xcafe)
            {
                return id;
            }

            ulong programId = context.Device.Processes.ActiveApplication?.ProgramId ?? 0;
            if (!NextendoBaasNsa.TitleUsesBaasNsa(programId))
            {
                return id;
            }

            return (long)NextendoBaasNsa.Choose(programId, (ulong)id, NextendoBaasNsa.Get());
        }

        // True if this manager's profile is the one linked to the Nextendo account (or none bound).
        private bool IsBoundNextendoProfile
        {
            get
            {
                string bound = NextendoAccount.ProfileUserId;
                if (string.IsNullOrEmpty(bound))
                {
                    return true;
                }

                return string.Equals(_userId.ToString(), bound, StringComparison.OrdinalIgnoreCase);
            }
        }

#pragma warning disable IDE0052 // Remove unread private member
        private readonly UserId _userId;
#pragma warning restore IDE0052

        private byte[] _cachedTokenData;
        private DateTime _cachedTokenExpiry;
        private string _cachedTokenVersion;
        private ulong _cachedTokenProgramId;
        private bool _cachedOnlineIdentity;
        private bool _cachedOnlineBlocked;
        private string _cachedNexToken;

        public ManagerServer(UserId userId)
        {
            _userId = userId;
        }

        // [Nextendo] BAAS id_token signing key. Splatoon 2 (unlike MK8) LOCALLY verifies the
        // BAAS id_token: it fetches the jku JWKS and checks the RS256 signature, so the emulator
        // must sign with the key whose PUBLIC half the server publishes at the jku URL.
        //
        // This open-source tree does NOT bundle the key: official builds inject it via the
        // NEXTENDO_BAAS_SIGNING_KEY environment variable (or a nextendo_baas.pem file next to the
        // executable). Without it the emulator signs with a throwaway key, so Splatoon 2 online
        // membership will not verify — everything offline still works.
        private static readonly RSA _nextendoIdTokenRsa = CreateNextendoIdTokenRsa();

        private static RSA CreateNextendoIdTokenRsa()
        {
            RSA rsa = RSA.Create(2048);

            string pem = Environment.GetEnvironmentVariable("NEXTENDO_BAAS_SIGNING_KEY");
            if (string.IsNullOrWhiteSpace(pem))
            {
                try
                {
                    if (File.Exists("nextendo_baas.pem"))
                    {
                        pem = File.ReadAllText("nextendo_baas.pem");
                    }
                }
                catch { /* fall back to a throwaway key */ }
            }

            if (!string.IsNullOrWhiteSpace(pem) && pem.Contains("BEGIN"))
            {
                try
                {
                    rsa.ImportFromPem(pem);
                }
                catch (Exception ex)
                {
                    Logger.Warning?.Print(LogClass.ServiceAcc, $"[Nextendo] BAAS signing key invalid: {ex.Message}");
                }
            }

            return rsa;
        }

        private static bool IsNsoClassics(ulong programId)
        {
            return programId is
                0x0100D870045B6000 or // NES
                0x01008D300C50C000 or // SNES
                0x0100C62011050000 or // Game Boy
                0x010012F017576000 or // Game Boy Advance
                0x0100C9A00ECE6000 or // Nintendo 64
                0x0100B3C014BDA000;   // Mega Drive / Genesis
        }

        private string GenerateIdToken(string installedVersion, ulong programId)
        {
            RSAParameters parameters = _nextendoIdTokenRsa.ExportParameters(true);

            RsaSecurityKey secKey = new(parameters);

            SigningCredentials credentials = new(secKey, SecurityAlgorithms.RsaSha256);

            credentials.Key.KeyId = "nextendo-baas-key-1";

            byte[] rawUserId = new byte[0x10];
            RandomNumberGenerator.Fill(rawUserId);

            byte[] deviceId = new byte[0x10];
            RandomNumberGenerator.Fill(deviceId);

            byte[] deviceAccountId = new byte[0x10];
            RandomNumberGenerator.Fill(deviceAccountId);

            Dictionary<string, object> claims = new()
            {
                { "jku", "https://e0d67c509fb203858ebcb2fe3f88c2aa.baas.nintendo.com/1.0.0/certificates" },
                { "jti", Guid.NewGuid().ToString() },
                { "di", Convert.ToHexString(deviceId).ToLower() },
                { "sn", "XAW10000000000" },
                { "bs:did", Convert.ToHexString(deviceAccountId).ToLower() },
                // NSO membership flag — Splatoon 2 reads this LOCALLY to gate online entry.
                { "hm", !NextendoAccount.OnlineBlocked },
            };

            // Scarlet shares Violet's NPLN tenant, and Monster Hunter Rise JP/HK shares the
            // US/EU one, but each needs its own app_id. Every other game's BAAS token remains
            // byte-compatible with the existing claim shape.
            // The NSO Classics share one endpoint that routes each login by catalogue, so they send
            // their application ID too. It is a routing scope only: the server still authenticates
            // the enclosed nx2 proof.
            if (IsNsoClassics(programId) || programId == 0x0100A3D008C5C000 || programId == 0x0100559011740000)
            {
                claims["app_id"] = programId.ToString("X16");
            }

            // [Nextendo] Cryptographic account binding for the NEX login. The game forwards
            // this id_token inside its NEX login extraData, but the auth server can't trust
            // the bare account PID the game ALSO sends as its login username — a custom build
            // could send anyone's (sequential PIDs => trivial impersonation). So we ride the
            // signed nx2 token (an HMAC over "pid.username.expiry" keyed by a secret only the
            // server holds) along in a custom "nnex" claim. The auth server validates that
            // HMAC and rejects any PID the token doesn't prove. Present only when a Nextendo
            // account is linked (online); absent otherwise, so offline is completely unaffected.
            string nexToken = NextendoAccount.IsLinked && !NextendoAccount.OnlineBlocked && IsBoundNextendoProfile
                ? NextendoAccount.NexToken : "";
            if (!string.IsNullOrEmpty(nexToken))
            {
                claims["nnex"] = nexToken;
            }

            if (!string.IsNullOrEmpty(installedVersion))
            {
                claims["tv"] = installedVersion;
            }

            SecurityTokenDescriptor descriptor = new()
            {
                Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, Convert.ToHexString(rawUserId).ToLower())]),
                SigningCredentials = credentials,
                Audience = "ed9e2f05d286f7b8",
                Issuer = "https://e0d67c509fb203858ebcb2fe3f88c2aa.baas.nintendo.com",
                TokenType = "id_token",
                IssuedAt = DateTime.UtcNow,
                Expires = DateTime.UtcNow + TimeSpan.FromHours(3),
                Claims = claims,
            };

            return new JsonWebTokenHandler().CreateToken(descriptor);
        }

        public ResultCode CheckAvailability(ServiceCtx context)
        {
            // NOTE: This opens the file at "su/baas/USERID_IN_UUID_STRING.dat" where USERID_IN_UUID_STRING is formatted as "%08x-%04x-%04x-%02x%02x-%08x%04x".
            //       Then it searches the Availability of Online Services related to the UserId in this file and returns it.

            Logger.Stub?.PrintStub(LogClass.ServiceAcc);

            // NOTE: Even if we try to return different error codes here, the guest still needs other calls.
            return ResultCode.Success;
        }

        public ResultCode GetAccountId(ServiceCtx context)
        {
            // NOTE: This opens the file at "su/baas/USERID_IN_UUID_STRING.dat" (where USERID_IN_UUID_STRING is formatted
            //       as "%08x-%04x-%04x-%02x%02x-%08x%04x") in the account:/ savedata.
            //       Then it searches the NetworkServiceAccountId related to the UserId in this file and returns it.

            Logger.Stub?.PrintStub(LogClass.ServiceAcc);

            context.ResponseData.Write(NetworkServiceAccountIdFor(context));

            return ResultCode.Success;
        }

        public ResultCode EnsureIdTokenCacheAsync(ServiceCtx context, out IAsyncContext asyncContext)
        {
            KEvent asyncEvent = new(context.Device.System.KernelContext);
            AsyncExecution asyncExecution = new(asyncEvent);

            asyncExecution.Initialize(1000, EnsureIdTokenCacheAsyncImpl);

            asyncContext = new IAsyncContext(asyncExecution);

            // return ResultCode.NullObject if the IAsyncContext pointer is null. Doesn't occur in our case.

            return ResultCode.Success;
        }

        private async Task EnsureIdTokenCacheAsyncImpl(CancellationToken token)
        {
            // NOTE: This open the file at "su/baas/USERID_IN_UUID_STRING.dat" (where USERID_IN_UUID_STRING is formatted as "%08x-%04x-%04x-%02x%02x-%08x%04x")
            //       in the "account:/" savedata.
            //       Then its read data, use dauth API with this data to get the Token Id and probably store the dauth response
            //       in "su/cache/USERID_IN_UUID_STRING.dat" (where USERID_IN_UUID_STRING is formatted as "%08x-%04x-%04x-%02x%02x-%08x%04x") in the "account:/" savedata.
            //       Since we don't support online services, we can stub it.

            Logger.Stub?.PrintStub(LogClass.ServiceAcc);

            // Le jeu passe en ligne : le pays du compte sera demandé juste après (informations du
            // compte Nintendo), on le demande au site dès maintenant, sans attendre.
            if (IsBoundNextendoProfile)
            {
                NextendoCountry.Prepare();
            }

            // TODO: Use a real function instead, with the CancellationToken.
            await Task.CompletedTask;
        }

        public ResultCode LoadIdTokenCache(ServiceCtx context)
        {
            ulong bufferPosition = context.Request.ReceiveBuff[0].Position;
            ulong bufferSize = context.Request.ReceiveBuff[0].Size;

            // NOTE: This opens the file at "su/cache/USERID_IN_UUID_STRING.dat" (where USERID_IN_UUID_STRING is formatted as "%08x-%04x-%04x-%02x%02x-%08x%04x")
            //       in the "account:/" savedata and writes some data in the buffer.
            //       Since we don't support online services, we can stub it.

            Logger.Stub?.PrintStub(LogClass.ServiceAcc);

            /*
            if (internal_object != null)
            {
                if (bufferSize > 0xC00)
                {
                    return ResultCode.InvalidIdTokenCacheBufferSize;
                }
            }
            */

            byte[] tokenData = GetIdToken(context);

            // Titles read the token as a C string, so zero the rest of their buffer instead of leaving stale bytes after it.
            byte[] output = new byte[Math.Max((ulong)tokenData.Length, bufferSize)];
            tokenData.CopyTo(output, 0);

            context.Memory.Write(bufferPosition, output);
            context.ResponseData.Write(tokenData.Length);

            return ResultCode.Success;
        }

        public byte[] GetIdToken(ServiceCtx context)
        {
            string installedVersion = context.Device.Processes.ActiveApplication?.DisplayVersion;
            ulong programId = context.Device.Processes.ActiveApplication?.ProgramId ?? 0;

            return GetIdTokenData(installedVersion, programId);
        }

        public ResultCode CreateAuthorizationRequest(ServiceCtx context, out IAuthorizationRequest authorizationRequest)
        {
            // In: u32 transfer-memory size, transfer-memory handle, 0x200-byte parameters buffer.
            (ulong parametersPosition, ulong parametersSize) = context.Request.GetBufferType0x21();

            byte[] parameters = new byte[parametersSize];
            context.Memory.Read(parametersPosition, parameters);

            authorizationRequest = new IAuthorizationRequest(this, parameters, parametersPosition);

            return ResultCode.Success;
        }

        private byte[] GetIdTokenData(string installedVersion, ulong programId)
        {
            bool onlineIdentity = NextendoAccount.IsLinked && !NextendoAccount.OnlineBlocked && IsBoundNextendoProfile;
            if (_cachedTokenData == null || DateTime.UtcNow > _cachedTokenExpiry ||
                installedVersion != _cachedTokenVersion || programId != _cachedTokenProgramId ||
                onlineIdentity != _cachedOnlineIdentity ||
                NextendoAccount.OnlineBlocked != _cachedOnlineBlocked ||
                NextendoAccount.NexToken != _cachedNexToken)
            {
                _cachedTokenExpiry = DateTime.UtcNow + TimeSpan.FromHours(3);
                _cachedTokenVersion = installedVersion;
                _cachedTokenProgramId = programId;
                _cachedOnlineIdentity = onlineIdentity;
                _cachedOnlineBlocked = NextendoAccount.OnlineBlocked;
                _cachedNexToken = NextendoAccount.NexToken;
                _cachedTokenData = Encoding.ASCII.GetBytes(GenerateIdToken(installedVersion, programId));
            }

            return _cachedTokenData;

        }

        public ResultCode GetNintendoAccountUserResourceCacheForApplication(ServiceCtx context)
        {
            // NasUserBase is a fixed 0x1a output pointer (C/X), not the B resource buffer.
            ResultCode result = CachedAccountInfo.PrepareResponse(context.Request, context.Response,
                context.OutputPointerBufferAddress, context.OutputPointerBufferSize);
            if (result != ResultCode.Success)
            {
                Logger.Warning?.Print(LogClass.ServiceAcc, "[Nextendo account cache] Invalid output buffers.");
                return result;
            }

            // Pays du compte Nextendo, seulement pour le profil lié au compte (les autres profils
            // locaux restent sans pays, comme leur identité reste le bouchon 0xcafe).
            string country = NextendoAccount.IsLinked && IsBoundNextendoProfile ? NextendoCountry.Get() : "";

            context.Memory.Write(context.OutputPointerBufferAddress, CachedAccountInfo.Create(country));
            if (context.Request.ReceiveBuff.Count > 0)
            {
                var resource = context.Request.ReceiveBuff[0];
                context.Memory.Fill(resource.Position, resource.Size, 0);
            }

            context.ResponseData.Write(NetworkServiceAccountIdFor(context));
            Logger.Info?.Print(LogClass.ServiceAcc,
                $"[Nextendo account cache] Returned 0x68-byte user base; shared birthday=2000-01-01; country {(country.Length == 2 ? "set" : "empty")}.");
            return ResultCode.Success;
        }

        public ResultCode StoreOpenContext(ServiceCtx context)
        {
            context.Device.System.AccountManager.StoreOpenedUsers();

            return ResultCode.Success;
        }

        public ResultCode LoadNetworkServiceLicenseKindAsync(ServiceCtx context, out IAsyncNetworkServiceLicenseKindContext asyncContext)
        {
            KEvent asyncEvent = new(context.Device.System.KernelContext);
            AsyncExecution asyncExecution = new(asyncEvent);

            Logger.Stub?.PrintStub(LogClass.ServiceAcc);

            // NOTE: This is an extension of the data retrieved from the id token cache.
            asyncExecution.Initialize(1000, EnsureIdTokenCacheAsyncImpl);

            asyncContext = new IAsyncNetworkServiceLicenseKindContext(asyncExecution, NetworkServiceLicenseKind.Subscribed);

            // return ResultCode.NullObject if the IAsyncNetworkServiceLicenseKindContext pointer is null. Doesn't occur in our case.

            return ResultCode.Success;
        }
    }
}
