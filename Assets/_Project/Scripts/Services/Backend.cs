using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.CloudCode;
using Unity.Services.Core;

namespace MazeRush.Services
{
    // Owner: backend. UI and gameplay only call these members.
    // All currency / inventory / stats mutation is done by the Cloud Code module "MazeRushModule"
    // (see /CloudCode). The client sends only ids and never writes currency or inventory itself.
    // Every failure surfaces as BackendException with a UI-ready message.
    public class BackendException : Exception
    {
        public BackendException(string message, Exception inner = null) : base(message, inner) { }
    }

    public static class Backend
    {
        const string Module = "MazeRushModule";

        public static bool SignedIn { get; private set; }
        public static PlayerProfile Profile { get; private set; } = new PlayerProfile();
        public static event Action<PlayerProfile> ProfileChanged;

        // true = coins, shop and stats live in a local JSON save (Services/LocalSave.cs).
        // false = use the Cloud Code module (needs the dashboard setup in BACKEND_SETUP.md).
        public static bool UseLocalSave = true;

        static bool s_Initialized;
        static string s_Username = "";

        // Mirrors the JSON returned by the module (property names are matched case-insensitively).
        class ProfileDto
        {
            public int coins;
            public List<string> owned;
            public string equippedSkin;
            public string equippedAccessory;
            public int wins;
            public int gamesPlayed;
        }

        public static async Task InitAsync()
        {
            if (s_Initialized) return;
            try
            {
                if (UnityServices.State == ServicesInitializationState.Uninitialized)
                    await UnityServices.InitializeAsync();
                s_Initialized = true;
                SignedIn = AuthenticationService.Instance.IsSignedIn;
            }
            catch (Exception e) { throw Wrap(e, "Could not connect to online services."); }
        }

        public static async Task SignUpAsync(string username, string password)
        {
            // NOTE: UGS username/password auth does NOT support e-mail addresses as username.
            // Username: 3-20 chars [A-Za-z0-9.-_@]; password: 8-30 chars with upper, lower, digit, symbol.
            await InitAsync();
            try
            {
                if (AuthenticationService.Instance.IsSignedIn) AuthenticationService.Instance.SignOut(true); // e.g. stale anonymous session
                await AuthenticationService.Instance.SignUpWithUsernamePasswordAsync(username, password);
                s_Username = username;
                SignedIn = true;
                await LoadProfileOrOfflineAsync();
            }
            catch (Exception e) { throw Wrap(e, "Sign up failed."); }
        }

        public static async Task SignInAsync(string username, string password)
        {
            await InitAsync();
            try
            {
                if (AuthenticationService.Instance.IsSignedIn) AuthenticationService.Instance.SignOut(true); // e.g. stale anonymous session
                await AuthenticationService.Instance.SignInWithUsernamePasswordAsync(username, password);
                s_Username = username;
                SignedIn = true;
                await LoadProfileOrOfflineAsync();
            }
            catch (Exception e) { throw Wrap(e, "Sign in failed."); }
        }

        // Auth worked but the backend module may be missing/undeployed: keep the player in with an empty profile.
        static async Task LoadProfileOrOfflineAsync()
        {
            try { await LoadProfileAsync(); }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"[Backend] profile load failed, using empty profile: {e}");
                Profile = new PlayerProfile { username = s_Username };
                ProfileChanged?.Invoke(Profile);
            }
        }

        public static void SignOut()
        {
            try { if (s_Initialized) AuthenticationService.Instance.SignOut(true); } catch { }
            SignedIn = false;
            s_Username = "";
            Profile = new PlayerProfile();
            ProfileChanged?.Invoke(Profile);
        }

        static Task<PlayerProfile> SetLocal(PlayerProfile p)
        {
            Profile = p;
            ProfileChanged?.Invoke(Profile);
            return Task.FromResult(Profile);
        }

        static string LocalName => string.IsNullOrEmpty(s_Username) ? "player" : s_Username;

        public static Task<PlayerProfile> LoadProfileAsync()
            => UseLocalSave ? SetLocal(LocalSave.Load(LocalName)) : CallProfile("GetProfile", null, "Could not load profile.");

        public static Task<PlayerProfile> PurchaseAsync(string cosmeticId)
        {
            if (UseLocalSave)
            {
                try { return SetLocal(LocalSave.Purchase(LocalName, cosmeticId)); }
                catch (BackendException e) { return Task.FromException<PlayerProfile>(e); }
            }
            // One purchase attempt = one idempotency key, reused across internal retries.
            // The server also treats "already owned" as success, so a lost response never double charges.
            var args = new Dictionary<string, object>
            {
                { "cosmeticId", cosmeticId },
                { "idempotencyKey", Guid.NewGuid().ToString("N") }
            };
            return CallProfile("PurchaseCosmetic", args, "Purchase failed.");
        }

        public static Task<PlayerProfile> EquipAsync(string cosmeticId)
        {
            if (UseLocalSave)
            {
                try { return SetLocal(LocalSave.Equip(LocalName, cosmeticId)); }
                catch (BackendException e) { return Task.FromException<PlayerProfile>(e); }
            }
            return CallProfile("EquipCosmetic", new Dictionary<string, object> { { "cosmeticId", cosmeticId } }, "Could not equip item.");
        }

        // Called once per match by the match host. coinsCollected is capped server-side.
        public static async Task ReportMatchAsync(bool won, int coinsCollected)
        {
            if (UseLocalSave) { await SetLocal(LocalSave.ReportMatch(LocalName, won, coinsCollected)); return; }
            var args = new Dictionary<string, object>
            {
                { "won", won },
                { "coinsCollected", Math.Max(0, coinsCollected) },
                { "matchId", Guid.NewGuid().ToString("N") } // server de-duplicates retries of the same match
            };
            await CallProfile("ReportMatch", args, "Could not submit match result.");
        }

        static async Task<PlayerProfile> CallProfile(string function, Dictionary<string, object> args, string fallback)
        {
            if (!s_Initialized || !SignedIn) throw new BackendException("You are not signed in.");
            Exception last = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var dto = await CloudCodeService.Instance.CallModuleEndpointAsync<ProfileDto>(Module, function, args);
                    Apply(dto);
                    return Profile;
                }
                catch (Exception e)
                {
                    last = e;
                    if (!IsTransient(e)) break;
                    await Task.Delay(500 * (attempt + 1)); // same args (same idempotency key) are re-sent
                }
            }
            throw Wrap(last, fallback);
        }

        static void Apply(ProfileDto d)
        {
            var p = new PlayerProfile { username = s_Username };
            if (d != null)
            {
                p.coins = d.coins;
                p.owned = d.owned ?? new List<string>();
                p.equippedSkin = d.equippedSkin ?? "";
                p.equippedAccessory = d.equippedAccessory ?? "";
                p.wins = d.wins;
                p.gamesPlayed = d.gamesPlayed;
            }
            Profile = p;
            ProfileChanged?.Invoke(Profile);
        }

        static bool IsTransient(Exception e)
        {
            if (e is CloudCodeRateLimitedException) return false;
            if (e is CloudCodeException cc)
                return cc.Reason == CloudCodeExceptionReason.NoInternetConnection
                    || cc.Reason == CloudCodeExceptionReason.ServiceUnavailable;
            return false;
        }

        static BackendException Wrap(Exception e, string fallback)
        {
            if (e is BackendException be) return be;
            string m = e != null ? e.Message ?? "" : "";
            string msg;
            if (e is AuthenticationException ae)
            {
                UnityEngine.Debug.LogWarning($"[Backend] auth error {ae.ErrorCode}: {m}");
                if (m.Contains("ENTITY_EXISTS") || m.Contains("already exist"))
                    msg = "That username is already taken.";
                else if (ae.ErrorCode == AuthenticationErrorCodes.ClientInvalidUserState)
                    msg = "Already signed in. Sign out first. (" + m + ")";
                else if (ae.ErrorCode == AuthenticationErrorCodes.InvalidParameters)
                    msg = "Invalid username or password. Username 3-20 chars [A-Za-z0-9.-_@]; password 8-30 chars with upper, lower, digit and symbol. (" + m + ")";
                else
                    msg = "Authentication failed: " + m;
            }
            else if (e is CloudCodeRateLimitedException) msg = "Too many requests. Please wait a moment.";
            else if (e is CloudCodeException cc && cc.Reason == CloudCodeExceptionReason.NoInternetConnection) msg = "No internet connection.";
            else if (m.Contains("INSUFFICIENT_FUNDS")) msg = "Not enough coins.";
            else if (m.Contains("NOT_OWNED")) msg = "You do not own that item.";
            else if (m.Contains("UNKNOWN_COSMETIC")) msg = "Unknown item.";
            else if (m.Contains("RATE_LIMITED")) msg = "Please wait before reporting another match.";
            else if (e is RequestFailedException) { UnityEngine.Debug.LogWarning($"[Backend] {fallback} {e}"); msg = fallback + " " + m; }
            else msg = fallback;
            return new BackendException(msg, e);
        }
    }
}
