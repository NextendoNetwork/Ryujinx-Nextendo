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

        private static string GenerateIdToken()
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

            SecurityTokenDescriptor descriptor = new()
            {
                Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, Convert.ToHexString(rawUserId).ToLower())]),
                SigningCredentials = credentials,
                Audience = "ed9e2f05d286f7b8",
                Issuer = "https://e0d67c509fb203858ebcb2fe3f88c2aa.baas.nintendo.com",
                TokenType = "id_token",
                IssuedAt = DateTime.UtcNow,
                Expires = DateTime.UtcNow + TimeSpan.FromHours(3),
                Claims = new Dictionary<string, object>
                {
                    { "jku", "https://e0d67c509fb203858ebcb2fe3f88c2aa.baas.nintendo.com/1.0.0/certificates" },
                    { "jti", Guid.NewGuid().ToString() },
                    { "di", Convert.ToHexString(deviceId).ToLower() },
                    { "sn", "XAW10000000000" },
                    { "bs:did", Convert.ToHexString(deviceAccountId).ToLower() },
                    // NSO membership flag — Splatoon 2 reads this LOCALLY to gate online entry.
                    { "hm", true },
                }
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

            Logger.Stub?.PrintStub(LogClass.ServiceAcc, new { NetworkServiceAccountId });

            context.ResponseData.Write(NetworkServiceAccountId);

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

            // TODO: Use a real function instead, with the CancellationToken.
            await Task.CompletedTask;
        }

        public ResultCode LoadIdTokenCache(ServiceCtx context)
        {
            ulong bufferPosition = context.Request.ReceiveBuff[0].Position;
#pragma warning disable IDE0059 // Remove unnecessary value assignment
            ulong bufferSize = context.Request.ReceiveBuff[0].Size;
#pragma warning restore IDE0059

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

            if (_cachedTokenData == null || DateTime.UtcNow > _cachedTokenExpiry)
            {
                _cachedTokenExpiry = DateTime.UtcNow + TimeSpan.FromHours(3);
                _cachedTokenData = Encoding.ASCII.GetBytes(GenerateIdToken());
            }

            byte[] tokenData = _cachedTokenData;

            context.Memory.Write(bufferPosition, tokenData);
            context.ResponseData.Write(tokenData.Length);

            return ResultCode.Success;
        }

        public ResultCode GetNintendoAccountUserResourceCacheForApplication(ServiceCtx context)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceAcc, new { NetworkServiceAccountId });

            context.ResponseData.Write(NetworkServiceAccountId);

            // TODO: determine and fill the output IPC buffer.

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
