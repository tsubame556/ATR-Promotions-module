#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace InfantPostureApp.Editor
{
    public class SceneSetupTool : UnityEditor.EditorWindow
    {
        // --- Color Palette (Apple Dark Mode Style) ---
        private static Color colorBg = new Color(0.0f, 0.0f, 0.0f, 1f); // #000000
        private static Color colorCard = new Color(0.11f, 0.11f, 0.12f, 1f); // #1C1C1E
        private static Color colorBlue = new Color(0.04f, 0.52f, 1.0f, 1f); // #0A84FF
        private static Color colorGrayBtn = new Color(0.23f, 0.23f, 0.24f, 1f); // #3A3A3C
        private static Color colorTextPrimary = Color.white;
        private static Color colorTextSecondary = new Color(0.56f, 0.56f, 0.58f, 1f); // #8E8E93
        private static Color colorLine = new Color(0.2f, 0.2f, 0.2f, 1f);

        private static Sprite roundedSprite;

        [MenuItem("InfantPostureApp/シーン自動構築 (Scene Setup)")]
        public static void GenerateScene()
        {
            // 丸角スプライトを生成
            roundedSprite = CreateRoundedSprite(12);

            // 1. システムGameObjectの構築
            GameObject systemObj = new GameObject("InfantPostureSystem");
            var analyzer = systemObj.AddComponent<PostureAnalyzer>();
            var dataManager = systemObj.AddComponent<DataManager>();
            var dummySim = systemObj.AddComponent<DummySensorSimulator>();
            var uiManager = systemObj.AddComponent<AppUIManager>();

            dataManager.postureAnalyzer = analyzer;
            dummySim.analyzer = analyzer;
            uiManager.postureAnalyzer = analyzer;
            uiManager.dataManager = dataManager;

            for (int i = 1; i <= 5; i++)
            {
                var driverObj = new GameObject($"SensorDriver_{i}");
                driverObj.transform.SetParent(systemObj.transform);
                var driver = driverObj.AddComponent<TSND151SerialDriver>();
                driver.sensorId = i;
                driver.IsDummyMode = true;
                analyzer.SensorDrivers.Add(driver);
            }
            analyzer.AddPair("TestPair(1->2)", 1, 2);

            // 2. UI (Canvas) の構築
            GameObject canvasObj = new GameObject("Canvas");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); // 基準解像度
            canvasObj.AddComponent<GraphicRaycaster>();

#if UNITY_2023_1_OR_NEWER
            if (Object.FindFirstObjectByType<EventSystem>() == null)
#else
            if (Object.FindObjectOfType<EventSystem>() == null)
#endif
            {
                GameObject eventSystem = new GameObject("EventSystem");
                eventSystem.AddComponent<EventSystem>();
                eventSystem.AddComponent<StandaloneInputModule>();
            }

            // 3. UI部品の生成 (モダンレイアウト)
            // 全体背景
            Image bgImg = canvasObj.AddComponent<Image>();
            bgImg.color = colorBg;

            // --- Main Container (Horizontal) ---
            GameObject mainContainer = new GameObject("MainContainer");
            mainContainer.transform.SetParent(canvasObj.transform);
            RectTransform mainRect = mainContainer.AddComponent<RectTransform>();
            mainRect.anchorMin = Vector2.zero;
            mainRect.anchorMax = Vector2.one;
            mainRect.offsetMin = new Vector2(0, 40); // 下部ステータスバー用に40px開ける
            mainRect.offsetMax = Vector2.zero;

            var mainLayout = mainContainer.AddComponent<HorizontalLayoutGroup>();
            mainLayout.padding = new RectOffset(20, 20, 20, 20);
            mainLayout.spacing = 20;
            mainLayout.childControlWidth = true;
            mainLayout.childControlHeight = true;

            // --- Left Sidebar ---
            GameObject sidebar = new GameObject("Sidebar");
            sidebar.transform.SetParent(mainContainer.transform);
            var sidebarLayout = sidebar.AddComponent<VerticalLayoutGroup>();
            sidebarLayout.spacing = 20;
            sidebarLayout.childControlWidth = true;
            sidebarLayout.childControlHeight = true;
            sidebarLayout.childForceExpandHeight = false;
            var sidebarElement = sidebar.AddComponent<LayoutElement>();
            sidebarElement.preferredWidth = 320; // サイドバーの固定幅

            // Group 1: Connection
            var connCard = CreateCard(sidebar.transform, "ConnectionCard");
            CreateLabel(connCard.transform, "CONNECTION");
            uiManager.btnConnectAll = CreateButton(connCard.transform, "Connect All Sensors", colorBlue, colorTextPrimary);
            uiManager.btnDisconnect = CreateButton(connCard.transform, "Disconnect", colorGrayBtn, colorTextPrimary);

            // Group 2: Recording
            var recCard = CreateCard(sidebar.transform, "RecordCard");
            CreateLabel(recCard.transform, "RECORDING");
            uiManager.btnStartRecord = CreateButton(recCard.transform, "Start Record", colorGrayBtn, colorTextPrimary);
            uiManager.btnStopRecord = CreateButton(recCard.transform, "Stop Record", colorGrayBtn, colorTextPrimary);
            uiManager.btnExportCSV = CreateButton(recCard.transform, "Export to CSV", colorGrayBtn, colorTextPrimary);

            // Group 3: Realtime Data
            var dataCard = CreateCard(sidebar.transform, "DataCard");
            CreateLabel(dataCard.transform, "REALTIME DATA");
            uiManager.txtTableData = CreateText(dataCard.transform, "Waiting for data...", colorTextPrimary, 14);

            // --- Right Content ---
            GameObject rightContent = new GameObject("RightContent");
            rightContent.transform.SetParent(mainContainer.transform);
            var rightLayout = rightContent.AddComponent<VerticalLayoutGroup>();
            rightLayout.spacing = 20;
            rightLayout.childControlWidth = true;
            rightLayout.childControlHeight = true;

            // 3D View Placeholder (透明)
            GameObject viewPlaceholder = new GameObject("3DViewArea");
            viewPlaceholder.transform.SetParent(rightContent.transform);
            var viewElement = viewPlaceholder.AddComponent<LayoutElement>();
            viewElement.flexibleHeight = 2; // グラフより広く

            // Graph Area
            GameObject graphCard = new GameObject("GraphCard");
            graphCard.transform.SetParent(rightContent.transform);
            Image graphBg = graphCard.AddComponent<Image>();
            graphBg.sprite = roundedSprite;
            graphBg.type = Image.Type.Sliced;
            graphBg.color = colorCard;
            var graphElement = graphCard.AddComponent<LayoutElement>();
            graphElement.flexibleHeight = 1;

            var graphLayout = graphCard.AddComponent<VerticalLayoutGroup>();
            graphLayout.padding = new RectOffset(16, 16, 16, 16);
            graphLayout.childControlWidth = true;
            graphLayout.childControlHeight = true;

            CreateLabel(graphCard.transform, "EULER ANGLES GRAPH (Roll=Red, Pitch=Green, Yaw=Cyan)");

            GameObject graphObj = new GameObject("GraphRawImage");
            graphObj.transform.SetParent(graphCard.transform);
            graphObj.AddComponent<RectTransform>();
            var graphController = graphObj.AddComponent<RealtimeGraphController>();
            // グラフ背景を黒寄りに
            graphController.BackgroundColor = new Color(0.05f, 0.05f, 0.05f, 1f); 
            uiManager.graphController = graphController;

            // --- Bottom Status Bar ---
            GameObject statusBar = new GameObject("StatusBar");
            statusBar.transform.SetParent(canvasObj.transform);
            RectTransform statusRect = statusBar.AddComponent<RectTransform>();
            statusRect.anchorMin = Vector2.zero;
            statusRect.anchorMax = new Vector2(1, 0);
            statusRect.pivot = new Vector2(0.5f, 0);
            statusRect.anchoredPosition = Vector2.zero;
            statusRect.sizeDelta = new Vector2(0, 40);

            Image statusBg = statusBar.AddComponent<Image>();
            statusBg.color = colorCard;

            var statusLayout = statusBar.AddComponent<HorizontalLayoutGroup>();
            statusLayout.padding = new RectOffset(20, 20, 0, 0);
            statusLayout.childControlWidth = true;
            statusLayout.childControlHeight = true;

            uiManager.txtStatusBars = new Text[5];
            for (int i = 0; i < 5; i++)
            {
                uiManager.txtStatusBars[i] = CreateText(statusBar.transform, $"Sensor {i+1}: Offline", colorTextSecondary, 12);
                uiManager.txtStatusBars[i].alignment = TextAnchor.MiddleLeft;
            }

            Debug.Log("Apple-Style Scene Setup Complete! UI has been beautifully aligned and generated.");
        }

        // --- Helper Methods ---
        
        private static GameObject CreateCard(Transform parent, string name)
        {
            GameObject card = new GameObject(name);
            card.transform.SetParent(parent);
            Image img = card.AddComponent<Image>();
            img.sprite = roundedSprite;
            img.type = Image.Type.Sliced;
            img.color = colorCard;

            var layout = card.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 16, 16);
            layout.spacing = 12;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            var element = card.AddComponent<LayoutElement>();
            element.flexibleWidth = 1;

            return card;
        }

        private static Text CreateLabel(Transform parent, string text)
        {
            Text txt = CreateText(parent, text, colorTextSecondary, 12);
            txt.fontStyle = FontStyle.Bold;
            return txt;
        }

        private static Button CreateButton(Transform parent, string label, Color bgColor, Color textColor)
        {
            GameObject btnObj = new GameObject("Btn_" + label.Replace(" ", ""));
            btnObj.transform.SetParent(parent);
            var element = btnObj.AddComponent<LayoutElement>();
            element.minHeight = 44; // iOS standard touch target height

            Image img = btnObj.AddComponent<Image>();
            img.sprite = roundedSprite;
            img.type = Image.Type.Sliced;
            img.color = bgColor;

            Button btn = btnObj.AddComponent<Button>();

            Text txt = CreateText(btnObj.transform, label, textColor, 16);
            txt.alignment = TextAnchor.MiddleCenter;
            txt.fontStyle = FontStyle.Bold;

            return btn;
        }

        private static Text CreateText(Transform parent, string content, Color color, int fontSize)
        {
            GameObject txtObj = new GameObject("Text");
            txtObj.transform.SetParent(parent);
            Text txt = txtObj.AddComponent<Text>();
            txt.text = content;
            txt.color = color;
            txt.fontSize = fontSize;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            
            // Textの高さを自動調整
            var fitter = txtObj.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            
            return txt;
        }

        /// <summary>
        /// プロシージャルに角丸スプライトを生成する（外部アセット不要化）
        /// </summary>
        private static Sprite CreateRoundedSprite(int radius)
        {
            int size = radius * 2 + 2;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float cx = x < radius ? radius : size - 1 - radius;
                    float cy = y < radius ? radius : size - 1 - radius;
                    
                    bool isCorner = (x < radius || x > size - 1 - radius) && (y < radius || y > size - 1 - radius);
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
                    
                    if (isCorner && dist > radius)
                        pixels[y * size + x] = Color.clear;
                    else
                        pixels[y * size + x] = Color.white;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }
    }
}
#endif
