/*
 BAHASA PASAR CARD GAME  (Unity) - full logic + dev panel
 TOP 60%  = the sprites needed for the sentence (placeholder boxes with the
            sprite name; shows "Please put 4 props inside the bowl" when empty)
 BOTTOM 40% = built sentence + status + meaning (EN/中文) + screen response
 DEV button (top-right) opens all cards so you can click any combo (even 2 of a
 category to test the error) and press CAKAP to evaluate - dev only.

 Receives real cards over UDP 5005 (from your Python detector + Arduino button).
 NOTE: 中文 needs a CJK TMP font asset to render (English works out of the box).
*/

using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class BahasaPasarGame : MonoBehaviour
{
    public int port = 5005;

    [Header("Manual Character Slots")]
    public Image characterBodyImage;
    public Image characterFaceImage;
    public Image characterClothesImage;
    public Image characterHairImage;
    public Image characterFacialImage;

    [Header("Manual Prop Slots")]
    public Image intentImage;
    public Image topicImage;
    public Image toneImage;
    public Image extraImage;

    [Header("Manual Canvas Layout")]
    public bool useManualCanvasLayout;
    public TextMeshProUGUI manualSentenceText;
    public TextMeshProUGUI manualStatusText;
    public TextMeshProUGUI manualMeaningText;
    public TextMeshProUGUI manualResponseText;
    public GameObject manualEmptyMessage;

    [Header("Manual Dev Controls")]
    public bool autoCreateDevPanel = false;
    public GameObject manualDevPanel;
    public Button manualDevToggleButton;
    public Button manualCakapButton;
    public Button manualClearButton;

    class Card {
        public string text, spoken, category, type, en, zh;
        public string[] accepts;
        public Card(string t, string s, string cat, string en, string zh,
                    string type = null, string[] accepts = null) {
            text = t; spoken = s; category = cat; this.en = en; this.zh = zh;
            this.type = type; this.accepts = accepts;
        }
    }

    readonly List<Card> cards = new List<Card>() {
        new Card("Boss / Tauke","Boss","speaker","boss / shop owner","老板 / 头家"),
        new Card("Lengzai / LengLui","Lengzai","speaker","handsome guy / pretty girl","靓仔 / 靓女"),
        new Card("Ah Moi","Ah Moi","speaker","young lady","阿妹 / 小姐"),
        new Card("Uncle / Aunty","Uncle","speaker","uncle / aunty","叔叔 / 阿姨"),
        new Card("Adik","Adik","speaker","younger person","弟弟妹妹 / 年轻人"),
        new Card("Bang","Bang","speaker","brother / abang","大哥 / 哥哥"),
        new Card("Makan","makan","intent","eat","吃", null, new[]{"Food"}),
        new Card("Tahpau / Bungkus","tapau","intent","take away / pack","打包", null, new[]{"Food"}),
        new Card("Tambah","tambah","intent","add more","加一点 / 加多", null, new[]{"AddOn","Taste","Ice","Food"}),
        new Card("Tak Mau","tak mahu","intent","do not want","不要", null, new[]{"AddOn","Taste","Ice","Food"}),
        new Card("Mau","mahu","intent","want","要", null, new[]{"Food","AddOn","Taste","Ice"}),
        new Card("Ambik","ambil","intent","take / collect","拿 / 取", null, new[]{"Food","AddOn"}),
        new Card("Sos / Zap","sos","topic","sauce / gravy","酱汁","AddOn"),
        new Card("Mee / Min","mee","topic","noodles","面","Food"),
        new Card("Pedas / Laht","pedas","topic","spicy taste","辣","Taste"),
        new Card("Sambal","sambal","topic","sambal chili paste","叁巴辣椒酱","AddOn"),
        new Card("Ais / Beng / Bing","ais","topic","ice / iced drink","冰","Ice"),
        new Card("NasiLemak","nasi lemak","topic","nasi lemak","椰浆饭","Food"),
        new Card("Lah","lah","tone","casual / confident tone","随便自然的语气"),
        new Card("Woi","woi","tone","loud / annoyed tone","大声 / 不爽的语气"),
        new Card("Je","je","tone","only / just a little tone","只是 / 一点点的语气"),
        new Card("Ke?","ke","tone","questioning tone","疑问语气"),
        new Card("Kan?","kan","tone","confirming tone, like right?","确认语气，像对吗"),
        new Card("Oh","oh","tone","surprised / realizing tone","惊讶 / 明白了的语气"),
    };

    readonly string[] catOrder = { "speaker", "intent", "topic", "tone" };
    readonly Dictionary<string, Color> catColor = new Dictionary<string, Color>() {
        { "speaker", new Color(0.18f,0.44f,0.45f) }, { "intent", new Color(0.85f,0.45f,0.10f) },
        { "topic", new Color(0.13f,0.55f,0.30f) }, { "tone", new Color(0.16f,0.35f,0.75f) },
    };
    readonly Dictionary<string,string> expr = new Dictionary<string,string>() {
        {"Lah","Confident / Lah expression"},{"Woi","Loud / Woi expression"},
        {"Je","Chill / Je expression"},{"Ke?","Questioning / Ke? expression"},
        {"Kan?","Confirming / Kan? expression"},{"Oh","Surprised / Oh expression"},
    };
    readonly Dictionary<string,string> intentVis = new Dictionary<string,string>() {
        {"Makan","Makan eating pose"},{"Tahpau / Bungkus","Tahpau / Bungkus action pose"},
        {"Tambah","Tambah adding motion pose"},{"Tak Mau","Tak Mau reject hand-stop pose"},
        {"Mau","Mau wanting / pointing pose"},{"Ambik","Ambik grabbing motion pose"},
    };
    readonly Dictionary<string,string> topicVis = new Dictionary<string,string>() {
        {"Sos / Zap","Sos / Zap sauce splash sprite"},{"Mee / Min","Mee / Min noodle bowl sprite"},
        {"Pedas / Laht","Pedas / Laht chili flame sprite"},{"Sambal","Sambal chili paste sprite"},
        {"Ais / Beng / Bing","Ais / Beng / Bing ice cup sprite"},{"NasiLemak","NasiLemak rice pack sprite"},
    };
    readonly Dictionary<string,string> toneFx = new Dictionary<string,string>() {
        {"Lah","Lah tone effect"},{"Woi","Woi loud effect"},{"Je","Je small effect"},
        {"Ke?","Ke? question effect"},{"Kan?","Kan? confirmation effect"},{"Oh","Oh surprise effect"},
    };

    Dictionary<string, Card> selected = new Dictionary<string, Card>();
    CharacterLook forcedCharacterLook;
    readonly string[] bodySkinPaths = {
        "Chracters/Body-SkinColor/1 Lightest", "Chracters/Body-SkinColor/2",
        "Chracters/Body-SkinColor/3", "Chracters/Body-SkinColor/4",
        "Chracters/Body-SkinColor/5", "Chracters/Body-SkinColor/6 Darkest"
    };
    readonly string[] faceSkinPaths = {
        "Chracters/Face-SkinColor/1 Lightest (2)", "Chracters/Face-SkinColor/2 (2)",
        "Chracters/Face-SkinColor/3 (2)", "Chracters/Face-SkinColor/4 (2)",
        "Chracters/Face-SkinColor/5 (2)", "Chracters/Face-SkinColor/6 Darkest (2)"
    };
    readonly string[] maleFacialPaths = {
        "", "Chracters/Facial/Beard", "Chracters/Facial/Glasses", "Chracters/Facial/Goatee"
    };

    class CharacterLook {
        public string clothesPath, hairPath, facialPath;
        public Sprite clothesSprite;
        public int skinIndex;
    }

    // UI
    Transform characterSlot, spriteRow; TextMeshProUGUI sentenceText, statusText, meaningText, responseText;
    GameObject emptyMsg, devPanel; readonly List<Card> devPick = new List<Card>();

    // UDP
    UdpClient client; Thread thread; volatile bool running;
    readonly Queue<string> queue = new Queue<string>(); readonly object locker = new object();
    [System.Serializable] class Item { public string name; public string category; }
    [System.Serializable] class Detection { public string status; public int count; public Item[] items; }

    void Start() {
        if (useManualCanvasLayout) UseManualUI();
        else BuildUI();
        ShowEmpty();
        StartReceiver();
    }

    void UseManualUI() {
        sentenceText = manualSentenceText;
        statusText = manualStatusText;
        meaningText = manualMeaningText;
        responseText = manualResponseText;
        emptyMsg = manualEmptyMessage;
        devPanel = manualDevPanel;

        if (manualDevToggleButton != null) manualDevToggleButton.onClick.AddListener(ToggleDevPanel);
        if (manualCakapButton != null) manualCakapButton.onClick.AddListener(DevEval);
        if (manualClearButton != null) manualClearButton.onClick.AddListener(ClearManualDevPick);



        if (devPanel != null) devPanel.SetActive(false);
    }

    Transform FindManualCanvasRoot() {
        if (manualSentenceText != null) return manualSentenceText.canvas.transform;
        if (characterBodyImage != null) return characterBodyImage.canvas.transform;
        if (intentImage != null) return intentImage.canvas.transform;
        var canvas = FindObjectOfType<Canvas>();
        return canvas != null ? canvas.transform : null;
    }

    // ================= UI =================
    void BuildUI() {
        var cGO = new GameObject("GameCanvas");
        var canvas = cGO.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var sc = cGO.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        cGO.AddComponent<GraphicRaycaster>();
        var root = cGO.transform;
        Panel(root, Vector2.zero, Vector2.one, new Color(0.09f,0.10f,0.12f,1));

        // TOP 60% visual area: character on the left, props on the right.
        var top = Panel(root, new Vector2(0.02f,0.42f), new Vector2(0.98f,0.97f), new Color(1,1,1,0.04f));
        var characterPanel = Panel(top.transform, new Vector2(0.02f,0.06f), new Vector2(0.32f,0.94f), new Color(0.12f,0.14f,0.18f,1));
        var characterGrid = characterPanel.AddComponent<GridLayoutGroup>();
        characterGrid.cellSize = new Vector2(150,130); characterGrid.spacing = new Vector2(12,12);
        characterGrid.padding = new RectOffset(16,16,16,16);
        characterGrid.childAlignment = TextAnchor.UpperCenter;
        characterSlot = characterPanel.transform;

        var propPanel = Panel(top.transform, new Vector2(0.36f,0.06f), new Vector2(0.98f,0.94f), new Color(0.10f,0.11f,0.14f,1));
        var grid = propPanel.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(260,140); grid.spacing = new Vector2(24,24);
        grid.padding = new RectOffset(24,24,24,24);
        grid.childAlignment = TextAnchor.UpperLeft;
        spriteRow = propPanel.transform;

        emptyMsg = Text(root,"Please put 4 props inside the bowl",
            new Vector2(0.1f,0.6f), new Vector2(0.9f,0.8f), 54, TextAlignmentOptions.Center, new Color(.7f,.7f,.7f)).gameObject;

        // BOTTOM 40%
        Panel(root, new Vector2(0.03f,0.03f), new Vector2(0.97f,0.40f), new Color(1,1,1,0.06f));
        sentenceText = Text(root,"", new Vector2(0.05f,0.30f), new Vector2(0.95f,0.39f), 52, TextAlignmentOptions.Center, Color.white);
        statusText   = Text(root,"", new Vector2(0.05f,0.24f), new Vector2(0.95f,0.30f), 30, TextAlignmentOptions.Center, Color.white);
        meaningText  = Text(root,"", new Vector2(0.05f,0.11f), new Vector2(0.95f,0.24f), 26, TextAlignmentOptions.Center, new Color(.85f,.85f,.85f));
        responseText = Text(root,"", new Vector2(0.05f,0.04f), new Vector2(0.95f,0.11f), 26, TextAlignmentOptions.Center, new Color(.55f,.8f,.95f));

        // DEV toggle button
        var devBtn = MakeButton(root, "DEV", new Vector2(0.90f,0.955f), new Vector2(0.99f,0.995f),
                            new Color(0.3f,0.3f,0.35f), () => devPanel.SetActive(!devPanel.activeSelf));
        BuildDevPanel(root);
        devPanel.SetActive(false);
    }

    void BuildDevPanel(Transform root) {
        devPanel = Panel(root, new Vector2(0.05f,0.05f), new Vector2(0.95f,0.95f), new Color(0.05f,0.06f,0.09f,0.97f));
        var pt = devPanel.transform;
        Text(pt,"DEV - click cards (can pick 2 of a category to test), then CAKAP",
             new Vector2(0.02f,0.93f), new Vector2(0.98f,0.99f), 26, TextAlignmentOptions.Center, Color.white);
        // 4 columns of cards
        for (int ci=0; ci<catOrder.Length; ci++) {
            string cat = catOrder[ci];
            float x0 = 0.02f + ci*0.245f, x1 = x0+0.23f;
            Text(pt, cat.ToUpper(), new Vector2(x0,0.86f), new Vector2(x1,0.91f), 24, TextAlignmentOptions.Center, Color.white);
            var col = cards.Where(c=>c.category==cat).ToList();
            for (int i=0;i<col.Count;i++) {
                var card = col[i];
                float y1 = 0.85f - i*0.115f, y0 = y1-0.10f;
                MakeButton(pt, card.text, new Vector2(x0,y0), new Vector2(x1,y1), catColor[cat],
                       () => { if (devPick.Contains(card)) devPick.Remove(card); else devPick.Add(card);
                               DevEval(); });
            }
        }
        MakeButton(pt,"CAKAP (test)", new Vector2(0.30f,0.02f), new Vector2(0.55f,0.09f), new Color(0.2f,0.6f,0.3f), DevEval);
        MakeButton(pt,"CLEAR", new Vector2(0.57f,0.02f), new Vector2(0.70f,0.09f), new Color(0.6f,0.25f,0.25f),
               () => { devPick.Clear(); Process(new List<string>()); });
    }

    public void ToggleDevPanel() {
        if (devPanel != null) devPanel.SetActive(!devPanel.activeSelf);
    }
    public void ClearManualDevPick() {
        devPick.Clear();
        Process(new List<string>());
    }
    public void PickBoss() { ForceSpeakerLookFromFolder("Boss / Tauke", "Boss", "Man - Guy", true, false); }
    public void PickAhMoi() { ForceSpeakerLookFromFolder("Ah Moi", "Ah Moi", "Girl - Woman (Other Race)", false, false); }
    public void PickLenglui() { ForceSpeakerLookFromFolder("Lengzai / LengLui", "Lenglui", "Girl - Woman (Other Race)", false, false); }
    public void PickLengzai() { ForceSpeakerLookFromFolder("Lengzai / LengLui", "Lengzai", "Man - Guy", true, false); }
    public void PickBang() { ForceSpeakerLookFromFolder("Bang", "Bang", "Male (Malay)", true, true); }
    public void PickAunty() { ForceSpeakerLookFromFolder("Uncle / Aunty", "Aunty", "Girls - Woman (Malay)", false, true); }
    public void PickUncle() { ForceSpeakerLookFromFolder("Uncle / Aunty", "Uncle", "Uncle - Old Guy", true, true); }
    public void PickAdik() { ForceSpeakerLookFromFolder("Adik", "Adik", "Young Boy", true, true); }
    public void PickAdikFemale() { ForceSpeakerLookFromFolder("Adik", "Adik (Female)", "Girls - Woman (Malay) 2", false, true); }

    void ToggleDevCard(string cardText) {
        forcedCharacterLook = null;
        var card = FindCard(cardText);
        if (card == null) return;
        if (devPick.Contains(card)) devPick.Remove(card);
        else devPick.Add(card);
        DevEval();
    }
    void ForceSpeakerLook(string cardText, string clothes, string hair, bool male, bool malaySkin) {
        ForceSpeakerLookFromFolder(cardText, clothes, hair, male, malaySkin);
    }
    void ForceSpeakerLookFromFolder(string cardText, string clothesFolder, string hair, bool male, bool malaySkin) {
        var card = FindCard(cardText);
        if (card == null) return;
        if (devPick.Contains(card)) {
            devPick.Remove(card);
            forcedCharacterLook = null;
        } else {
            devPick.Add(card);
            forcedCharacterLook = BuildLookFromClothesFolder(clothesFolder, hair, male, malaySkin);
        }
        DevEval();
    }
    void DevEval() { Process(devPick.Select(c=>c.text).ToList()); }

    GameObject Panel(Transform p, Vector2 aMin, Vector2 aMax, Color c) {
        var go = new GameObject("Panel"); go.transform.SetParent(p,false);
        var img = go.AddComponent<Image>(); img.color = c;
        var rt = img.rectTransform; rt.anchorMin=aMin; rt.anchorMax=aMax; rt.offsetMin=Vector2.zero; rt.offsetMax=Vector2.zero;
        return go;
    }
    TextMeshProUGUI Text(Transform p, string s, Vector2 aMin, Vector2 aMax, float size, TextAlignmentOptions al, Color c) {
        var go = new GameObject("Text"); go.transform.SetParent(p,false);
        var t = go.AddComponent<TextMeshProUGUI>(); t.text=s; t.fontSize=size; t.alignment=al; t.color=c;
        t.enableWordWrapping = true;
        var rt=t.rectTransform; rt.anchorMin=aMin; rt.anchorMax=aMax; rt.offsetMin=Vector2.zero; rt.offsetMax=Vector2.zero;
        return t;
    }
    Button MakeButton(Transform p, string label, Vector2 aMin, Vector2 aMax, Color c, UnityEngine.Events.UnityAction onClick) {
        var go = new GameObject("Btn"); go.transform.SetParent(p,false);
        var img = go.AddComponent<Image>(); img.color=c;
        var rt=img.rectTransform; rt.anchorMin=aMin; rt.anchorMax=aMax; rt.offsetMin=Vector2.zero; rt.offsetMax=Vector2.zero;
        var btn = go.AddComponent<Button>(); btn.onClick.AddListener(onClick);
        Text(go.transform, label, Vector2.zero, Vector2.one, 22, TextAlignmentOptions.Center, Color.white);
        return btn;
    }

    // sprite placeholder box in the top row
    void AddSpriteBox(string name) {
        var go = new GameObject("Sprite"); go.transform.SetParent(spriteRow,false);
        var img = go.AddComponent<Image>(); img.color = new Color(0.2f,0.22f,0.26f,1);
        Text(go.transform, name, new Vector2(0.04f,0.04f), new Vector2(0.96f,0.96f), 20, TextAlignmentOptions.Center, Color.white);
    }
    void AddCharacterBox(Card speaker) {
        var look = forcedCharacterLook ?? RandomCharacterLook(speaker);
        if (HasManualCharacterSlots()) {
            SetSlotSprite(characterBodyImage, bodySkinPaths[look.skinIndex]);
            SetSlotSprite(characterFaceImage, faceSkinPaths[look.skinIndex]);
            SetSlotSprite(characterClothesImage, look.clothesSprite, look.clothesPath);
            SetSlotSprite(characterHairImage, look.hairPath);
            SetSlotSprite(characterFacialImage, look.facialPath);
            return;
        }

        AddCharacterPart(bodySkinPaths[look.skinIndex], "Body");
        AddCharacterPart(faceSkinPaths[look.skinIndex], "Face");
        AddCharacterPart(look.clothesSprite, look.clothesPath, "Clothes");
        AddCharacterPart(look.hairPath, "Hair");
        if (!string.IsNullOrEmpty(look.facialPath)) AddCharacterPart(look.facialPath, "Facial");
    }
    void AddCharacterPart(string resourcePath, string label) {
        AddCharacterPart(null, resourcePath, label);
    }
    void AddCharacterPart(Sprite sprite, string resourcePath, string label) {
        if (sprite == null && !string.IsNullOrEmpty(resourcePath)) sprite = Resources.Load<Sprite>(resourcePath);
        if (sprite == null) {
            Debug.LogWarning("Missing character sprite: " + resourcePath);
            return;
        }

        var card = new GameObject(label + "Part"); card.transform.SetParent(characterSlot,false);
        var bg = card.AddComponent<Image>(); bg.color = new Color(0.16f,0.18f,0.22f,1);
        var btn = card.AddComponent<Button>();
        btn.onClick.AddListener(() => { ClearSprites(); FillSprites(SpriteModules("valid")); });

        var imageGo = new GameObject(label); imageGo.transform.SetParent(card.transform,false);
        var img = imageGo.AddComponent<Image>(); img.sprite = sprite; img.color = Color.white; img.preserveAspect = true;
        var rt = img.rectTransform;
        rt.anchorMin = new Vector2(0.06f,0.20f); rt.anchorMax = new Vector2(0.94f,0.96f);
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

        Text(card.transform, label, new Vector2(0.04f,0.02f), new Vector2(0.96f,0.18f), 14,
             TextAlignmentOptions.Center, new Color(.85f,.85f,.85f));
    }
    CharacterLook RandomCharacterLook(Card speaker) {
        var look = new CharacterLook();
        bool male = true;
        bool malaySkin = false;
        string clothes = "Boss", hair = "Man - Guy";

        switch (speaker.text) {
            case "Boss / Tauke":
                clothes = "Boss";
                hair = Random.value < .5f ? "Man - Guy" : "Man - Guy 2";
                break;
            case "Lengzai / LengLui":
                male = Random.value < .5f;
                clothes = male ? "Lengzai" : "Lenglui";
                hair = male ? (Random.value < .5f ? "Man - Guy" : "Man - Guy 2") : "Girl - Woman (Other Race)";
                break;
            case "Ah Moi":
                male = false;
                clothes = "Ah Moi";
                hair = "Girl - Woman (Other Race)";
                break;
            case "Uncle / Aunty":
                male = Random.value < .5f;
                clothes = male ? "Uncle" : "Aunty";
                hair = male ? "Uncle - Old Guy" : "Girls - Woman (Malay)";
                malaySkin = !male || Random.value < .55f;
                break;
            case "Adik":
                male = Random.value < .55f;
                clothes = male ? "Adik" : (Random.value < .5f ? "Ah Moi" : "Lenglui");
                hair = male ? "Young Boy" : "Girls - Woman (Malay) 2";
                malaySkin = true;
                break;
            case "Bang":
                clothes = "Bang";
                hair = "Male (Malay)";
                malaySkin = true;
                break;
        }

        return BuildLookFromClothesFolder(clothes, hair, male, malaySkin);
    }
    CharacterLook BuildLook(string clothes, string hair, bool male, bool malaySkin) {
        return BuildLookFromClothesFolder(clothes, hair, male, malaySkin);
    }
    CharacterLook BuildLookFromClothesFolder(string clothesFolder, string hair, bool male, bool malaySkin) {
        var look = new CharacterLook();
        look.skinIndex = malaySkin ? Random.Range(3, 6) : Random.Range(0, 6);
        look.clothesPath = "Chracters/Clothes/" + clothesFolder;
        var clothesSprites = Resources.LoadAll<Sprite>(look.clothesPath);
        if (clothesSprites != null && clothesSprites.Length > 0) {
            look.clothesSprite = clothesSprites[Random.Range(0, clothesSprites.Length)];
        } else {
            Debug.LogWarning("Missing clothes folder sprites: " + look.clothesPath);
        }
        look.hairPath = "Chracters/Hair/" + hair;
        look.facialPath = male ? maleFacialPaths[Random.Range(0, maleFacialPaths.Length)] : "";
        return look;
    }
    void ClearSprites() {
        ClearChildren(spriteRow);
        ClearChildren(characterSlot);
        ClearManualSlots();
    }
    void ClearChildren(Transform parent) {
        if (parent == null) return;
        for (int i=parent.childCount-1;i>=0;i--) Destroy(parent.GetChild(i).gameObject);
    }
    bool HasManualCharacterSlots() {
        return characterBodyImage != null || characterFaceImage != null || characterClothesImage != null ||
               characterHairImage != null || characterFacialImage != null;
    }
    void ClearManualSlots() {
        ClearSlot(characterBodyImage); ClearSlot(characterFaceImage); ClearSlot(characterClothesImage);
        ClearSlot(characterHairImage); ClearSlot(characterFacialImage); ClearSlot(intentImage);
        ClearSlot(topicImage); ClearSlot(toneImage); ClearSlot(extraImage);
    }
    void ClearSlot(Image img) {
        if (img == null) return;
        img.sprite = null;
        img.enabled = false;
    }
    void SetSlotSprite(Image img, string resourcePath) {
        SetSlotSprite(img, null, resourcePath);
    }
    void SetSlotSprite(Image img, Sprite sprite, string resourcePath) {
        if (img == null) return;
        if (sprite == null && !string.IsNullOrEmpty(resourcePath)) sprite = Resources.Load<Sprite>(resourcePath);
        if (sprite == null) {
            if (!string.IsNullOrEmpty(resourcePath)) Debug.LogWarning("Missing manual slot sprite: " + resourcePath);
            ClearSlot(img);
            return;
        }
        img.sprite = sprite;
        img.preserveAspect = true;
        img.enabled = true;
    }

    // ================= UDP =================
    void StartReceiver() {
        try { client=new UdpClient(); client.Client.SetSocketOption(SocketOptionLevel.Socket,SocketOptionName.ReuseAddress,true);
              client.Client.Bind(new IPEndPoint(IPAddress.Any,port)); }
        catch(System.Exception e){ Debug.LogError("UDP bind failed: "+e.Message); return; }
        running=true; thread=new Thread(Loop){IsBackground=true}; thread.Start();
    }
    void Loop(){ var any=new IPEndPoint(IPAddress.Any,0);
        while(running){ try{ var d=client.Receive(ref any); lock(locker) queue.Enqueue(Encoding.UTF8.GetString(d)); } catch{ break; } } }

    void Update() {
        if (Input.GetKeyDown(KeyCode.Alpha1)) T("Boss","Makan","Mee / Min","Lah");
        if (Input.GetKeyDown(KeyCode.Alpha2)) T("Ah Moi","Tambah","Pedas / Laht","Je");
        if (Input.GetKeyDown(KeyCode.Alpha3)) T("Makan","Sambal");
        if (Input.GetKeyDown(KeyCode.Alpha4)) T("Boss");
        if (Input.GetKeyDown(KeyCode.D)) ToggleDevPanel();

        string msg=null; lock(locker){ while(queue.Count>0) msg=queue.Dequeue(); }
        if (msg==null) return;
        var d=JsonUtility.FromJson<Detection>(msg); if(d==null) return;
        var names=new List<string>(); if(d.items!=null) foreach(var it in d.items) names.Add(it.name);
        Process(names);
    }
    void T(params string[] n){ Process(n.ToList()); }

    // ================= LOGIC =================
    Card FindCard(string name) {
        if (string.IsNullOrEmpty(name)) return null;
        string key = Norm(name);
        foreach (var c in cards) if (Norm(c.text)==key || Norm(c.spoken)==key) return c;
        return null;
    }
    string Norm(string s)=>s.ToLower().Replace(" ","").Replace("/","").Replace("?","").Replace(".","");
    Card Sel(string cat)=> selected.ContainsKey(cat)?selected[cat]:null;
    string P(Card c)=> c==null?"":"("+c.text+")";

    void Process(List<string> names) {
        var byCat = new Dictionary<string,List<Card>>();
        foreach (var n in names){ var c=FindCard(n); if(c==null) continue;
            if(!byCat.ContainsKey(c.category)) byCat[c.category]=new List<Card>();
            if(!byCat[c.category].Contains(c)) byCat[c.category].Add(c); }

        foreach (var kv in byCat) if (kv.Value.Count>1) {
            selected.Clear(); FillSprites(new List<string>());
            Show("CANNOT BUILD","ERROR", "Too many "+kv.Key+" cards - leave only ONE "+kv.Key+".","",
                 new Color(0.9f,0.2f,0.2f)); return;
        }
        selected.Clear(); foreach(var kv in byCat) selected[kv.Key]=kv.Value[0];
        Evaluate();
    }

    string BuildSentence() {
        var parts=new List<string>();
        if(Sel("intent")!=null) parts.Add(Sel("intent").text);
        if(Sel("topic")!=null) parts.Add(Sel("topic").text);
        if(Sel("tone")!=null) parts.Add(Sel("tone").text);
        string phrase=string.Join(" ",parts);
        if(Sel("speaker")!=null && phrase!="") return Sel("speaker").text+", "+phrase;
        if(Sel("speaker")!=null) return Sel("speaker").text;
        return phrase==""?"No cards":phrase;
    }
    string ValidEn(){ var sp=Sel("speaker"); var it=Sel("intent"); var tp=Sel("topic"); var tn=Sel("tone");
        return (sp!=null?P(sp)+", ":"")+P(it)+" "+P(tp)+(tn!=null?" "+P(tn):""); }

    void Evaluate() {
        int n=selected.Count;
        if (n==0){ ShowEmpty(); return; }
        var it=Sel("intent"); var tp=Sel("topic"); var tn=Sel("tone");
        string code, statusTxt, meaning, resp; Color col;

        if (n==1){ var c=selected.Values.First();
            code="single"; statusTxt="SINGLE WORD";
            meaning="You placed ("+c.text+") = "+c.en+" ["+c.zh+"]. Add more cards to make a sentence.";
            resp="One card only ah. Add more cards.";
            col=new Color(0.3f,0.5f,0.9f);
        } else if (it!=null && tp!=null) {
            if (it.accepts.Contains(tp.type)) {
                code="valid";
                statusTxt = tn!=null ? "VALID" : "VALID (add a tone for more feel)";
                meaning = tn!=null ? "This works. You said: "+ValidEn()+".  \""+it.en+" "+tp.en+"\""
                                   : "This works. You said: "+ValidEn()+". Tone is neutral - add (Lah)/(Ke?)/(Kan?) for more Bahasa Pasar feel.";
                resp = ScreenResp(it,tp,tn,false);
                col=new Color(0.2f,0.75f,0.35f);
            } else {
                code="weird"; statusTxt="WEIRD / INCOMPATIBLE";
                meaning="("+it.text+") does not work well with ("+tp.text+"). The action and topic do not match - try changing one card.";
                resp = "Problem: "+P(it)+" + "+P(tp)+". These two do not match.";
                col=new Color(0.85f,0.3f,0.2f);
            }
        } else if (tp!=null && it==null) {
            code="incomplete"; statusTxt="INCOMPLETE";
            meaning="You placed ("+tp.text+") = "+tp.en+", but no action yet. Add an Intent like (Mau), (Makan) or (Tahpau).";
            resp="Can guess a bit, but belum complete. Add the action card.";
            col=new Color(0.85f,0.55f,0.15f);
        } else if (it!=null && tp==null) {
            code="incomplete"; statusTxt="INCOMPLETE";
            meaning="Action ("+it.text+") = "+it.en+" is clear, but the item is missing. Add a Topic card.";
            resp="Can guess a bit, but belum complete. Add the item card.";
            col=new Color(0.85f,0.55f,0.15f);
        } else {
            code="incomplete"; statusTxt="INCOMPLETE";
            meaning="Add an Intent and a Topic card to make a full sentence.";
            resp="Add more cards and I can understand better.";
            col=new Color(0.85f,0.55f,0.15f);
        }
        if (n > 1) meaning += "\n中文：" + ZhSentence();
        FillSprites(SpriteModules(code));
        Show(BuildSentence(), statusTxt, meaning, resp, col);
    }

    string ZhSentence() {
        var parts = new List<string>();
        foreach (var cat in catOrder) { var c = Sel(cat); if (c != null) parts.Add("(" + c.zh + ")"); }
        return string.Join(" ", parts);
    }

    string ScreenResp(Card it, Card tp, Card tn, bool weird) {
        if (tn!=null && tn.text=="Woi") return "Woi, relax lah. I understand already.";
        if (tn!=null && tn.text=="Je") return "Okay, simple only.";
        if (it.text=="Tahpau / Bungkus" && tp.text=="NasiLemak") return "Okay lah, NasiLemak tahpau. Wait kejap.";
        if (it.text=="Tambah" && tp.text=="Sos / Zap") return "Tambah sos je? Can, sikit only.";
        if (it.text=="Tak Mau" && tp.text=="Pedas / Laht") return "Tak mau pedas ke? Okay, less spicy.";
        if (it.text=="Ambik" && tp.text=="Sambal") return "Ambik sambal oh? Can, take sikit.";
        return it.text+" "+tp.text+(tn!=null?" "+tn.text:"")+"? Okay, boleh.";
    }

    List<string> SpriteModules(string code) {
        var m=new List<string>(); bool single=code=="single", weird=code=="weird", incomplete=code=="incomplete";
        var sp=Sel("speaker"); var it=Sel("intent"); var tp=Sel("topic"); var tn=Sel("tone");
        if (sp!=null) m.Add(sp.text+" base character");
        else if (!single && (it!=null||tp!=null)) m.Add("Generic hand / listener sprite");
        if (sp!=null||weird||tn!=null||incomplete) m.Add(weird?"Confused / Weird expression":(tn!=null&&expr.ContainsKey(tn.text)?expr[tn.text]:"Neutral expression"));
        if (it!=null) m.Add(intentVis[it.text]);
        if (tp!=null) m.Add(topicVis[tp.text]);
        if (tn!=null && tn.text=="Kan?" && it!=null && tp!=null) m.Add("Seller handoff sprite");
        else if (weird) m.Add("Warning / confusion icon");
        else if (sp!=null && it!=null && tp!=null) m.Add("Hand / object interaction sprite");
        if (tn!=null) m.Add(toneFx[tn.text]);
        if (!single || it!=null || sp!=null || tn!=null) m.Add(tn!=null?"Speech bubble":"Neutral speech bubble");
        return m.Distinct().ToList();
    }

    // ================= DISPLAY =================
    void FillSprites(List<string> modules) {
        ClearSprites();
        var sp = Sel("speaker");
        bool hasCharacter = sp != null;
        if (emptyMsg != null) emptyMsg.SetActive(modules.Count==0 && !hasCharacter);
        if (hasCharacter) AddCharacterBox(sp);
        foreach (var mName in modules) {
            if (mName.Contains("base character") || mName.Contains("expression")) continue;
            if (spriteRow != null) AddSpriteBox(mName);
        }
    }
    void ShowEmpty() { FillSprites(new List<string>());
        Show("", "", "", "", Color.gray); if(sentenceText!=null) sentenceText.text=""; }
    void Show(string sentence, string status, string meaning, string resp, Color col) {
        if (sentenceText==null) return;
        sentenceText.text=sentence; statusText.text=status; statusText.color=col;
        meaningText.text=meaning; responseText.text=resp;
    }

    void FillSlots(){} // (legacy no-op)

    void OnDisable(){ running=false; if(client!=null) client.Close(); if(thread!=null) thread.Join(200); }
    void OnApplicationQuit(){ OnDisable(); }
}
