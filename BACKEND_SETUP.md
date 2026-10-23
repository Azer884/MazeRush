# Maze Rush backend setup (all steps are MANUAL, done by you)

## Design
- Client (`Assets/_Project/Scripts/Services/Backend.cs`) only does UGS Authentication (username + password) and calls Cloud Code module `MazeRushModule` endpoints: `GetProfile`, `PurchaseCosmetic`, `EquipCosmetic`, `ReportMatch`. It never writes currency, inventory or stats.
- Purchases: chosen path = Cloud Code wraps an Economy virtual purchase (`BUY_<COSMETICID>`). Reason: a client-called Economy purchase is also server-validated, but routing it through Cloud Code lets us add the already-owned idempotency check, slot rules and a single profile response. Currency must NOT be client-writable (Economy defaults to this).
- Cloud Code module also owns match rewards (cap 40 coins, min 20 s between rewards, duplicate matchId ignored), stats and equipped state (Cloud Save protected data: `stats`, `equipped`, `matchMeta`).
- Cosmetic ids must start with `skin_` or `acc_` (slot is derived from the prefix).
- UGS username/password does NOT support e-mail login. Username 3-20 chars (letters, digits, `.-_@`), password 8-30 chars with upper, lower, digit, symbol.

## Steps
1. Unity Dashboard (cloud.unity.com): create/select a project; create environment `production`.
2. Editor: Edit > Project Settings > Services, link the project, choose the environment.
3. Dashboard > Authentication: enable Username/Password.
4. Enable Cloud Save, Economy, Cloud Code for the project.
5. Economy > Currencies: create `COIN` (id exactly `COIN`), initial 0, no max needed.
6. Economy > Inventory items: create 6 items with these ids (names free): `skin_red`, `skin_blue`, `skin_gold`, `acc_hat`, `acc_cape`, `acc_glasses`.
7. Economy > Virtual purchases: for each item create `BUY_SKIN_RED`, `BUY_SKIN_BLUE`, `BUY_SKIN_GOLD`, `BUY_ACC_HAT`, `BUY_ACC_CAPE`, `BUY_ACC_GLASSES` (id = `BUY_` + item id uppercased). Cost: COIN (e.g. 50/50/150/40/80/60); reward: 1 of the item. **Publish** the Economy config.
8. Deploy Cloud Code module (needs .NET 6+ SDK):
   - `cd CloudCode && dotnet new sln -n MazeRushModule && dotnet sln add MazeRushModule/MazeRushModule.csproj && dotnet build -c Release`
   - Unity: Assets > Create > Cloud Code C# Module Reference, set Solution Path to that .sln, then Window > Deployment > select it > Deploy. (Alternatively `ugs deploy` with the UGS CLI.)
9. Play from a built scene that calls `Backend.InitAsync()` then Sign up.

## NOT verified (no cloud project / no NuGet available here)
- The C# module was never compiled or deployed. Namespaces and exact `IGameApiClient` overloads (`ctx.AccessToken` vs `ctx.ServiceToken` for Cloud Save protected data and currency increments, `PlayerPurchaseVirtualRequest` ctor, result `.Data.Results` shapes) were written from memory of the Cloud Code docs; adjust after the first `dotnet build`.
- Client side was compiled in the Editor and uses only APIs found in the installed packages (`CloudCodeService.Instance.CallModuleEndpointAsync<T>`, `AuthenticationService.Instance.SignUp/SignInWithUsernamePasswordAsync`).
- Error mapping relies on module exception messages (INSUFFICIENT_FUNDS, NOT_OWNED, RATE_LIMITED, UNKNOWN_COSMETIC); Economy's real insufficient-funds message text should be checked.
- Purchase idempotency is "already owned => no second charge"; the key is sent but the Economy purchase request itself is not given it.
