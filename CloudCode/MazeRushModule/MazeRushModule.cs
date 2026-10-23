using Newtonsoft.Json;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudSave.Model;   // SetItemBody (from Unity.Services.CloudCode.Apis)
using Unity.Services.Economy.Model;     // PlayerPurchaseVirtualRequest, CurrencyModifyBalanceRequest

// NOTE: written against the public Cloud Code module API (Unity.Services.CloudCode.Core / .Apis) from
// memory of the official docs. It could NOT be compiled or deployed from this machine (no NuGet restore,
// no cloud project). Expect to adjust namespaces/overloads after the first `dotnet build`; see BACKEND_SETUP.md.
namespace MazeRushModule;

public class ProfileDto
{
    public long coins;
    public List<string> owned = new();
    public string equippedSkin = "";
    public string equippedAccessory = "";
    public int wins;
    public int gamesPlayed;
}

public class MazeRushEndpoints
{
    const string Currency = "COIN";
    const int MaxCoinsPerMatch = 40;
    const int MinSecondsBetweenRewards = 20;

    readonly IGameApiClient _api;
    readonly ILogger<MazeRushEndpoints> _log;

    public MazeRushEndpoints(IGameApiClient api, ILogger<MazeRushEndpoints> log) { _api = api; _log = log; }

    // Slot is derived from the id prefix: skin_* or acc_*. Items themselves are defined in Economy.
    static string SlotOf(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 40) return null;
        if (id.StartsWith("skin_")) return "skin";
        if (id.StartsWith("acc_")) return "accessory";
        return null;
    }

    [CloudCodeFunction("GetProfile")]
    public Task<ProfileDto> GetProfile(IExecutionContext ctx) => BuildProfile(ctx);

    [CloudCodeFunction("PurchaseCosmetic")]
    public async Task<ProfileDto> PurchaseCosmetic(IExecutionContext ctx, string cosmeticId, string idempotencyKey)
    {
        if (SlotOf(cosmeticId) == null) throw new Exception("UNKNOWN_COSMETIC");
        var owned = await GetOwned(ctx);
        if (!owned.Contains(cosmeticId))   // already owned => success, no second charge (retry safe)
        {
            try
            {
                // Virtual purchase "BUY_<ID>" is configured in the Economy dashboard; it costs COIN and grants the item.
                await _api.EconomyPurchases.MakeVirtualPurchaseAsync(ctx, ctx.AccessToken, ctx.ProjectId, ctx.PlayerId,
                    new PlayerPurchaseVirtualRequest("BUY_" + cosmeticId.ToUpperInvariant()));
            }
            catch (Exception e)
            {
                _log.LogWarning(e, "Purchase failed for {Id}", cosmeticId);
                if (e.Message.Contains("nsufficient") || e.Message.Contains("INSUFFICIENT")) throw new Exception("INSUFFICIENT_FUNDS");
                throw new Exception("PURCHASE_FAILED");
            }
        }
        return await BuildProfile(ctx);
    }

    [CloudCodeFunction("EquipCosmetic")]
    public async Task<ProfileDto> EquipCosmetic(IExecutionContext ctx, string cosmeticId)
    {
        string slot = SlotOf(cosmeticId);
        if (slot == null) throw new Exception("UNKNOWN_COSMETIC");
        var owned = await GetOwned(ctx);
        if (!owned.Contains(cosmeticId)) throw new Exception("NOT_OWNED");

        var eq = await Load<Dictionary<string, string>>(ctx, "equipped") ?? new();
        eq[slot] = cosmeticId;
        await Save(ctx, "equipped", eq);
        return await BuildProfile(ctx);
    }

    [CloudCodeFunction("ReportMatch")]
    public async Task<ProfileDto> ReportMatch(IExecutionContext ctx, bool won, int coinsCollected, string matchId)
    {
        var meta = await Load<MatchMeta>(ctx, "matchMeta") ?? new MatchMeta();
        if (!string.IsNullOrEmpty(matchId) && meta.lastMatchId == matchId)
            return await BuildProfile(ctx);   // duplicate retry: already recorded, never pay twice

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (now - meta.lastRewardUnix < MinSecondsBetweenRewards) throw new Exception("RATE_LIMITED");

        int coins = Math.Clamp(coinsCollected, 0, MaxCoinsPerMatch);
        if (coins > 0)
            await _api.EconomyCurrencies.IncrementPlayerCurrencyBalanceAsync(ctx, ctx.ServiceToken, ctx.ProjectId, ctx.PlayerId,
                Currency, new CurrencyModifyBalanceRequest(coins));

        var stats = await Load<Stats>(ctx, "stats") ?? new Stats();
        stats.gamesPlayed++;
        if (won) stats.wins++;
        stats.totalCoins += coins;
        await Save(ctx, "stats", stats);

        meta.lastMatchId = matchId;
        meta.lastRewardUnix = now;
        await Save(ctx, "matchMeta", meta);
        return await BuildProfile(ctx);
    }

    // ---- helpers ----
    class MatchMeta { public string lastMatchId = ""; public long lastRewardUnix; }
    class Stats { public int wins; public int gamesPlayed; public long totalCoins; }

    async Task<ProfileDto> BuildProfile(IExecutionContext ctx)
    {
        var bal = await _api.EconomyCurrencies.GetPlayerCurrencyBalancesAsync(ctx, ctx.AccessToken, ctx.ProjectId, ctx.PlayerId);
        long coins = bal.Data.Results.FirstOrDefault(b => b.CurrencyId == Currency)?.Balance ?? 0;
        var eq = await Load<Dictionary<string, string>>(ctx, "equipped") ?? new();
        var stats = await Load<Stats>(ctx, "stats") ?? new Stats();
        return new ProfileDto
        {
            coins = coins,
            owned = (await GetOwned(ctx)).ToList(),
            equippedSkin = eq.GetValueOrDefault("skin", ""),
            equippedAccessory = eq.GetValueOrDefault("accessory", ""),
            wins = stats.wins,
            gamesPlayed = stats.gamesPlayed
        };
    }

    async Task<HashSet<string>> GetOwned(IExecutionContext ctx)
    {
        var inv = await _api.EconomyInventory.GetPlayerInventoryAsync(ctx, ctx.AccessToken, ctx.ProjectId, ctx.PlayerId);
        return inv.Data.Results.Select(i => i.InventoryItemId).ToHashSet();
    }

    async Task<T> Load<T>(IExecutionContext ctx, string key) where T : class
    {
        var r = await _api.CloudSaveData.GetProtectedItemsAsync(ctx, ctx.ServiceToken, ctx.ProjectId, ctx.PlayerId, new List<string> { key });
        var item = r.Data.Results.FirstOrDefault();
        return item == null ? null : JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(item.Value));
    }

    Task Save<T>(IExecutionContext ctx, string key, T value)
        => _api.CloudSaveData.SetProtectedItemAsync(ctx, ctx.ServiceToken, ctx.ProjectId, ctx.PlayerId, new SetItemBody(key, value));
}
