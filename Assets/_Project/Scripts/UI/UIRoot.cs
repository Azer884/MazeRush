using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using MazeRush.Services;
using MazeRush.Shop;

namespace MazeRush.UI
{
    public class UIRoot : MonoBehaviour
    {
        public static UIRoot Instance { get; private set; }

        static readonly Color Bg = new Color(0.07f, 0.08f, 0.11f, 0.96f);
        static readonly Color Panel = new Color(0.13f, 0.15f, 0.2f, 1f);
        static readonly Color Accent = new Color(0.25f, 0.55f, 1f, 1f);
        static readonly Color Disabled = new Color(0.25f, 0.27f, 0.32f, 1f);

        Font font;
        Transform canvasT;
        readonly Dictionary<string, GameObject> screens = new Dictionary<string, GameObject>();

        InputField userIn, passIn; Text loginErr; Button loginBtn, regBtn; bool busy;
        Text menuHello, profileText, coinsText, matchText, endTitle;
        Transform shopList;
        bool inMatch, paused;

        void Awake()
        {
            Instance = this;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildCanvas();
            BuildLogin(); BuildMenu(); BuildShop(); BuildProfile(); BuildMatchmaking(); BuildEnd(); BuildPause();
            PlayAdapter.StateChanged += OnMatchState;
            PlayAdapter.MatchEnded += ShowEnd;
            Backend.ProfileChanged += OnProfile;
        }

        async void Start()
        {
            Show("login");
            try { await Backend.InitAsync(); } catch (Exception e) { SetErr(e.Message); }
        }

        void OnDestroy()
        {
            PlayAdapter.StateChanged -= OnMatchState;
            PlayAdapter.MatchEnded -= ShowEnd;
            Backend.ProfileChanged -= OnProfile;
            if (Instance == this) Instance = null;
        }

        void OnMatchState(string s)
        {
            if (matchText) matchText.text = s;
            if (s == "Countdown" || s == "Playing") { inMatch = true; if (!paused) Hide(); }   // get out of the way of gameplay
            else if (s == "Idle") { EndPlay(); Show("menu"); }
            else if (s.StartsWith("Error")) { EndPlay(); Show("menu"); Debug.LogWarning(s); }
        }

        void EndPlay()
        {
            inMatch = false; paused = false;
            PlayerController.InputBlocked = false;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (inMatch && kb != null && kb.escapeKey.wasPressedThisFrame) SetPaused(!paused);
        }

        // Online match: the world keeps running, only this player's input stops.
        void SetPaused(bool on)
        {
            paused = on;
            PlayerController.InputBlocked = on;
            Cursor.lockState = on ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = on;
            if (on) Show("pause"); else Hide();
        }

        // ---------- public ----------
        public void Show(string name)
        {
            foreach (var kv in screens) kv.Value.SetActive(kv.Key == name);
            RefreshAll();
        }

        public void Hide()
        {
            foreach (var kv in screens) kv.Value.SetActive(false);
        }

        public void ShowEnd(bool won)
        {
            EndPlay();
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            endTitle.text = won ? "VICTORY" : "DEFEAT";
            endTitle.color = won ? new Color(0.4f, 1f, 0.5f) : new Color(1f, 0.4f, 0.4f);
            Show("end");
        }

        void OnProfile(PlayerProfile p) { RefreshAll(); }

        void RefreshAll()
        {
            var p = Backend.Profile ?? new PlayerProfile();
            if (menuHello) menuHello.text = "Welcome, " + p.username + "\nCoins: " + p.coins;
            if (coinsText) coinsText.text = "Coins: " + p.coins;
            if (profileText) profileText.text =
                "Username: " + p.username + "\nCoins: " + p.coins + "\nWins: " + p.wins +
                "\nGames played: " + p.gamesPlayed + "\nOwned: " + (p.owned.Count == 0 ? "-" : string.Join(", ", p.owned)) +
                "\nSkin: " + (string.IsNullOrEmpty(p.equippedSkin) ? "-" : p.equippedSkin) +
                "\nAccessory: " + (string.IsNullOrEmpty(p.equippedAccessory) ? "-" : p.equippedAccessory);
            if (shopList && screens.ContainsKey("shop") && screens["shop"].activeSelf) RebuildShop();
        }

        // ---------- screens ----------
        void BuildCanvas()
        {
            var go = new GameObject("UICanvas");
            go.transform.SetParent(transform, false);
            var c = go.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 100;
            var s = go.AddComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(1280, 720); s.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            canvasT = go.transform;
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.transform.SetParent(transform, false);
                es.AddComponent<EventSystem>(); es.AddComponent<InputSystemUIInputModule>();
            }
        }

        Transform NewScreen(string name, string title, float width = 520)
        {
            var root = Img("Screen_" + name, canvasT, Bg);
            Stretch(root.rectTransform);
            screens[name] = root.gameObject;
            var col = Column(root.transform, new Vector2(width, 0), 14);
            if (!string.IsNullOrEmpty(title))
            {
                var t = Label(col, title, 48, TextAnchor.MiddleCenter, 76);
                t.fontStyle = FontStyle.Bold; t.color = Color.Lerp(Color.white, Accent, 0.35f);
                var sh = t.gameObject.AddComponent<Shadow>(); sh.effectDistance = new Vector2(2, -2);
                var line = Img("TitleLine", col, Accent);
                line.gameObject.AddComponent<LayoutElement>().preferredHeight = 3;
            }
            return col;
        }

        void BuildLogin()
        {
            var c = NewScreen("login", "MAZE RUSH");
            userIn = Input(c, "Username", false); passIn = Input(c, "Password", true);
            loginErr = Label(c, "", 22, TextAnchor.MiddleCenter, 60);
            loginBtn = Btn(c, "Sign In", () => Auth(false));
            regBtn = Btn(c, "Register", () => Auth(true));
        }

        async void Auth(bool register)
        {
            if (busy) return;
            string u = userIn.text.Trim(), p = passIn.text;
            if (u.Length < 3 || u.Length > 20) { SetErr("Username must be 3-20 characters."); return; }
            if (p.Length < 8) { SetErr("Password must be at least 8 characters."); return; }
            busy = true; loginBtn.interactable = regBtn.interactable = false;
            loginErr.text = "Loading..."; loginErr.color = Color.white;
            try
            {
                if (register) await Backend.SignUpAsync(u, p); else await Backend.SignInAsync(u, p);
                loginErr.text = ""; // Sign* already loaded the profile (or fell back to an empty one)
                Show("menu");
            }
            catch (Exception e) { SetErr(e.Message); }
            finally { busy = false; loginBtn.interactable = regBtn.interactable = true; }
        }

        void SetErr(string m)
        {
            if (!loginErr) return;
            loginErr.color = new Color(1f, 0.45f, 0.45f); loginErr.text = m;
        }

        void BuildMenu()
        {
            var c = NewScreen("menu", "MAIN MENU");
            menuHello = Label(c, "", 28, TextAnchor.MiddleCenter, 80);
            Btn(c, "Play", Play);
            Btn(c, "Shop", () => Show("shop"));
            Btn(c, "Profile", OpenProfile);
            Btn(c, "Logout", () => { Backend.SignOut(); Show("login"); });
        }

        async void OpenProfile()
        {
            Show("profile");
            try { await Backend.LoadProfileAsync(); } catch (Exception e) { Debug.LogWarning(e.Message); }
        }

        async void Play()
        {
            matchText.text = "Searching for opponent...";
            Show("match");
            try { await PlayAdapter.QuickPlayAsync(); }
            catch (Exception e) { Debug.LogWarning(e.Message); Show("menu"); }
        }

        void BuildMatchmaking()
        {
            var c = NewScreen("match", "MATCHMAKING");
            matchText = Label(c, "Searching...", 28, TextAnchor.MiddleCenter, 80);
            Btn(c, "Cancel", () => { PlayAdapter.Leave(); Show("menu"); });
        }

        void BuildPause()
        {
            var c = NewScreen("pause", "PAUSED", 420);
            screens["pause"].GetComponent<Image>().color = new Color(0.04f, 0.05f, 0.08f, 0.7f); // see the game behind
            Btn(c, "Resume", () => SetPaused(false));
            Btn(c, "Leave Match", () => { EndPlay(); PlayAdapter.Leave(); Show("menu"); });
            Label(c, "Esc to resume", 20, TextAnchor.MiddleCenter, 30).color = new Color(1, 1, 1, 0.5f);
        }

        void BuildEnd()
        {
            var c = NewScreen("end", "");
            endTitle = Label(c, "", 72, TextAnchor.MiddleCenter, 120);
            Btn(c, "Back to Menu", () => { PlayAdapter.Leave(); Show("menu"); });
        }

        void BuildProfile()
        {
            var c = NewScreen("profile", "PROFILE");
            profileText = Label(c, "", 26, TextAnchor.UpperLeft, 300);
            Btn(c, "Back", () => Show("menu"));
        }

        void BuildShop()
        {
            var c = NewScreen("shop", "SHOP", 760);
            coinsText = Label(c, "", 28, TextAnchor.MiddleCenter, 40);
            var holder = new GameObject("List", typeof(RectTransform));
            holder.transform.SetParent(c, false);
            holder.AddComponent<LayoutElement>().preferredHeight = 420;
            var v = holder.AddComponent<VerticalLayoutGroup>();
            v.spacing = 8; v.childControlWidth = v.childControlHeight = true; v.childForceExpandHeight = false;
            shopList = holder.transform;
            Btn(c, "Back", () => Show("menu"));
        }

        void RebuildShop()
        {
            for (int i = shopList.childCount - 1; i >= 0; i--) Destroy(shopList.GetChild(i).gameObject);
            var p = Backend.Profile;
            foreach (var item in CosmeticCatalog.Default.items)
            {
                var it = item;
                bool owned = p.owned.Contains(it.id);
                bool equipped = p.equippedSkin == it.id || p.equippedAccessory == it.id;
                var row = Img("Row", shopList, Panel);
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = 60;
                var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                h.padding = new RectOffset(12, 12, 6, 6); h.spacing = 10;
                h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = false;
                var sw = Img("Swatch", row.transform, it.color);
                if (it.id == CosmeticCatalog.RainbowId) sw.gameObject.AddComponent<RainbowTint>();
                sw.gameObject.AddComponent<LayoutElement>().preferredWidth = 36;
                var nm = Label(row.transform, it.displayName + "  (" + it.category + ")", 22, TextAnchor.MiddleLeft, 0);
                nm.GetComponent<LayoutElement>().flexibleWidth = 1;
                var pr = Label(row.transform, owned ? "Owned" : it.price + "c", 22, TextAnchor.MiddleRight, 0);
                pr.GetComponent<LayoutElement>().preferredWidth = 90;
                Button b;
                if (!owned)
                {
                    b = Btn(row.transform, "Buy", () => ShopOp(() => Backend.PurchaseAsync(it.id)), 120, 0, 22);
                    b.interactable = p.coins >= it.price;
                }
                else
                {
                    b = Btn(row.transform, equipped ? "Equipped" : "Equip", () => ShopOp(() => Backend.EquipAsync(it.id)), 120, 0, 22);
                    b.interactable = !equipped;
                }
                if (!b.interactable) b.GetComponent<Image>().color = Disabled;
            }
        }

        async void ShopOp(Func<Task> op)
        {
            try { await op(); } catch (Exception e) { Debug.LogWarning(e.Message); }
            RefreshAll();
        }

        // ---------- helpers ----------
        Image Img(string n, Transform parent, Color col)
        {
            var go = new GameObject(n, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var i = go.AddComponent<Image>(); i.color = col; return i;
        }

        static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
        }

        Transform Column(Transform parent, Vector2 size, float spacing)
        {
            var go = new GameObject("Column", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = size;
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing; v.childAlignment = TextAnchor.MiddleCenter;
            v.childControlWidth = v.childControlHeight = true; v.childForceExpandHeight = false;
            go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return go.transform;
        }

        Text Label(Transform parent, string text, int size, TextAnchor a, float h)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.supportRichText = true; t.raycastTarget = false;
            t.font = font; t.text = text; t.fontSize = size; t.alignment = a; t.color = Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            var le = go.AddComponent<LayoutElement>(); if (h > 0) le.preferredHeight = h;
            return t;
        }

        Button Btn(Transform parent, string text, Action click, float w = 0, float h = 60, int size = 28)
        {
            var img = Img("Btn_" + text, parent, Accent);
            var b = img.gameObject.AddComponent<Button>(); b.targetGraphic = img;
            b.onClick.AddListener(() => click());
            var cb = b.colors; // hover / press feedback
            cb.normalColor = Color.white; cb.highlightedColor = new Color(1.25f, 1.25f, 1.25f);
            cb.pressedColor = new Color(0.7f, 0.7f, 0.7f); cb.disabledColor = new Color(0.6f, 0.6f, 0.6f);
            cb.fadeDuration = 0.08f; b.colors = cb;
            var le = img.gameObject.AddComponent<LayoutElement>();
            if (h > 0) le.preferredHeight = h;
            if (w > 0) le.preferredWidth = w;
            var t = Label(img.transform, text, size, TextAnchor.MiddleCenter, 0);
            Destroy(t.GetComponent<LayoutElement>());
            Stretch(t.rectTransform);
            return b;
        }

        InputField Input(Transform parent, string placeholder, bool password)
        {
            var img = Img("Input_" + placeholder, parent, Panel);
            img.gameObject.AddComponent<LayoutElement>().preferredHeight = 56;
            var f = img.gameObject.AddComponent<InputField>();
            var txt = Label(img.transform, "", 26, TextAnchor.MiddleLeft, 0);
            Destroy(txt.GetComponent<LayoutElement>()); Stretch(txt.rectTransform);
            txt.rectTransform.offsetMin = new Vector2(14, 0); txt.rectTransform.offsetMax = new Vector2(-14, 0);
            var ph = Label(img.transform, placeholder, 26, TextAnchor.MiddleLeft, 0);
            Destroy(ph.GetComponent<LayoutElement>()); Stretch(ph.rectTransform);
            ph.rectTransform.offsetMin = new Vector2(14, 0); ph.color = new Color(1, 1, 1, 0.4f); ph.fontStyle = FontStyle.Italic;
            f.textComponent = txt; f.placeholder = ph; f.targetGraphic = img;
            if (password) f.contentType = InputField.ContentType.Password;
            return f;
        }
    }

    // Cycles the hue of its Image (used for the Rainbow shop swatch).
    public class RainbowTint : MonoBehaviour
    {
        UnityEngine.UI.Image img;
        void Awake() { img = GetComponent<UnityEngine.UI.Image>(); }
        void Update() { if (img) img.color = Color.HSVToRGB(Mathf.Repeat(Time.time * 0.4f, 1f), 0.8f, 1f); }
    }
}
