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
        private static Color colorBg = new Color(0.0f, 0.0f, 0.0f, 1f);
        private static Color colorCard = new Color(0.11f, 0.11f, 0.12f, 1f);
        private static Color colorBlue = new Color(0.04f, 0.52f, 1.0f, 1f);
        private static Color colorGrayBtn = new Color(0.23f, 0.23f, 0.24f, 1f);
        private static Color colorTextPrimary = Color.white;
        private static Color colorTextSecondary = new Color(0.56f, 0.56f, 0.58f, 1f);

        private static Sprite roundedSprite;

        [MenuItem("InfantPostureApp/シーン自動構築 (Scene Setup)")]
        public static void GenerateScene()
        {
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

            // ==========================================
            // 2. 3Dアバター環境構築 (RenderTexture用)
            // ==========================================
            GameObject avatarRoot = new GameObject("3DAvatarEnvironment");
            avatarRoot.transform.position = new Vector3(0, 1000, 0); // UIと干渉しないよう遥か上空に隔離

            // 専用カメラ
            GameObject avatarCamObj = new GameObject("AvatarCamera");
            avatarCamObj.transform.SetParent(avatarRoot.transform);
            avatarCamObj.transform.localPosition = new Vector3(0, 0.5f, -4);
            Camera avatarCam = avatarCamObj.AddComponent<Camera>();
            avatarCam.clearFlags = CameraClearFlags.SolidColor;
            avatarCam.backgroundColor = colorCard; // UIのカードと同じ背景色にする

            // ライト
            GameObject avatarLight = new GameObject("AvatarLight");
            avatarLight.transform.SetParent(avatarRoot.transform);
            avatarLight.transform.localPosition = new Vector3(0, 5, -5);
            avatarLight.transform.localRotation = Quaternion.Euler(45, 0, 0);
            Light l = avatarLight.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 1.0f;

            // RenderTexture
            RenderTexture rt = new RenderTexture(1024, 1024, 24);
            rt.name = "AvatarRenderTexture";
            avatarCam.targetTexture = rt;

            // 3Dダミーモデル（CesiumMan.glb）のロードと生成
            GameObject babyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/CesiumMan.glb");
            if (babyPrefab != null)
            {
                GameObject babyModel = (GameObject)PrefabUtility.InstantiatePrefab(babyPrefab);
                babyModel.name = "CesiumManAvatar";
                babyModel.transform.SetParent(avatarRoot.transform);
                babyModel.transform.localPosition = new Vector3(0, -0.8f, 0);
                babyModel.transform.localScale = new Vector3(2.5f, 2.5f, 2.5f);
                babyModel.transform.localRotation = Quaternion.Euler(0, 180, 0); // カメラ側を向くように反転

                // 各パーツにSensorTransformMapperをアタッチ
                AttachMapper(babyModel.transform, "Skeleton_torso_joint_1", analyzer, 1);
                AttachMapper(babyModel.transform, "Skeleton_arm_joint_L__2_", analyzer, 2);
                AttachMapper(babyModel.transform, "Skeleton_arm_joint_R__2_", analyzer, 3);
                AttachMapper(babyModel.transform, "leg_joint_L_1", analyzer, 4);
                AttachMapper(babyModel.transform, "leg_joint_R_1", analyzer, 5);
            }
            else
            {
                Debug.LogWarning("CesiumMan.glbが見つかりません。Assets/Models/内に正しく配置されているか確認してください。");
            }

            // ==========================================
            // 3. UI (Canvas) の構築
            // ==========================================
            GameObject canvasObj = new GameObject("Canvas");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
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

            Image bgImg = canvasObj.AddComponent<Image>();
            bgImg.color = colorBg;

            GameObject mainContainer = new GameObject("MainContainer");
            mainContainer.transform.SetParent(canvasObj.transform, false);
            RectTransform mainRect = mainContainer.AddComponent<RectTransform>();
            mainRect.anchorMin = Vector2.zero; mainRect.anchorMax = Vector2.one;
            mainRect.offsetMin = new Vector2(0, 40); mainRect.offsetMax = Vector2.zero;

            var mainLayout = mainContainer.AddComponent<HorizontalLayoutGroup>();
            mainLayout.padding = new RectOffset(20, 20, 20, 20);
            mainLayout.spacing = 20;
            mainLayout.childControlWidth = true;
            mainLayout.childControlHeight = true;

            // --- Left Sidebar (Operations) ---
            GameObject sidebar = new GameObject("Sidebar");
            sidebar.transform.SetParent(mainContainer.transform, false);
            var sidebarLayout = sidebar.AddComponent<VerticalLayoutGroup>();
            sidebarLayout.spacing = 20;
            sidebarLayout.childControlWidth = true;
            sidebarLayout.childControlHeight = true;
            sidebarLayout.childForceExpandHeight = false;
            var sidebarElement = sidebar.AddComponent<LayoutElement>();
            sidebarElement.minWidth = 300;
            sidebarElement.preferredWidth = 300;
            sidebarElement.flexibleWidth = 0;

            var connCard = CreateCard(sidebar.transform, "ConnectionCard");
            CreateLabel(connCard.transform, "CONNECTION");

            uiManager.portDropdowns = new Dropdown[5];

            for (int i = 0; i < 5; i++)
            {
                GameObject row = new GameObject("PortRow" + (i + 1));
                row.transform.SetParent(connCard.transform, false);
                var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
                rowLayout.spacing = 10;
                rowLayout.childAlignment = TextAnchor.MiddleCenter;
                rowLayout.childControlWidth = true;
                rowLayout.childControlHeight = true;
                rowLayout.childForceExpandHeight = false;

                var element = row.AddComponent<LayoutElement>();
                element.minHeight = 30;
                element.preferredHeight = 30;
                element.flexibleHeight = 0;

                Text label = CreateText(row.transform, $"Sensor {i + 1}:", colorTextSecondary, 12, false);
                label.alignment = TextAnchor.MiddleLeft;
                var lblElement = label.gameObject.AddComponent<LayoutElement>();
                lblElement.minWidth = 60;
                lblElement.preferredWidth = 60;

                uiManager.portDropdowns[i] = CreateDropdownList(row.transform, "Dropdown" + (i + 1));
            }

            uiManager.btnRefreshPorts = CreateButton(connCard.transform, "Refresh Ports", colorGrayBtn, colorTextPrimary);
            uiManager.btnConnectAll = CreateButton(connCard.transform, "Connect All Sensors", colorBlue, colorTextPrimary);
            uiManager.btnDisconnect = CreateButton(connCard.transform, "Disconnect", colorGrayBtn, colorTextPrimary);

            var recCard = CreateCard(sidebar.transform, "RecordCard");
            CreateLabel(recCard.transform, "RECORDING");
            uiManager.btnStartRecord = CreateButton(recCard.transform, "Start Record", colorGrayBtn, colorTextPrimary);
            uiManager.btnStopRecord = CreateButton(recCard.transform, "Stop Record", colorGrayBtn, colorTextPrimary);
            uiManager.btnExportCSV = CreateButton(recCard.transform, "Export to CSV", colorGrayBtn, colorTextPrimary);

            // --- Right Content (Data View) ---
            GameObject rightContent = new GameObject("RightContent");
            rightContent.transform.SetParent(mainContainer.transform, false);
            var rightElement = rightContent.AddComponent<LayoutElement>();
            rightElement.flexibleWidth = 1;
            var rightLayout = rightContent.AddComponent<VerticalLayoutGroup>();
            rightLayout.spacing = 20;
            rightLayout.childControlWidth = true;
            rightLayout.childControlHeight = true;

            // Top Area (Avatar + Table)
            GameObject topArea = new GameObject("TopDataArea");
            topArea.transform.SetParent(rightContent.transform, false);
            var topLayout = topArea.AddComponent<HorizontalLayoutGroup>();
            topLayout.spacing = 20;
            topLayout.childControlWidth = true;
            topLayout.childControlHeight = true;
            var topElement = topArea.AddComponent<LayoutElement>();
            topElement.flexibleHeight = 1.5f;

            // 3D Avatar Area
            GameObject avatarCard = CreateCard(topArea.transform, "AvatarCard");
            var avatarElement = avatarCard.GetComponent<LayoutElement>();
            avatarElement.flexibleWidth = 1.5f;
            avatarElement.flexibleHeight = 1;
            
            Mask avatarMask = avatarCard.AddComponent<Mask>();
            avatarMask.showMaskGraphic = true;

            CreateLabel(avatarCard.transform, "3D AVATAR (REALTIME POSTURE)");

            GameObject rtObj = new GameObject("AvatarRenderTextureDisplay");
            rtObj.transform.SetParent(avatarCard.transform, false);
            RawImage rtImg = rtObj.AddComponent<RawImage>();
            rtImg.texture = rt; 
            rtObj.AddComponent<LayoutElement>().flexibleHeight = 1;

            // Realtime Data Table Area
            var dataCard = CreateCard(topArea.transform, "DataCard");
            var dataCardLayout = dataCard.GetComponent<LayoutElement>();
            dataCardLayout.flexibleWidth = 1;
            dataCardLayout.flexibleHeight = 1;
            CreateLabel(dataCard.transform, "REALTIME DATA");
            uiManager.txtTableData = CreateText(dataCard.transform, "Waiting for data...", colorTextPrimary, 14);

            // Graph Area
            GameObject graphCard = CreateCard(rightContent.transform, "GraphCard");
            var graphElement = graphCard.GetComponent<LayoutElement>();
            graphElement.flexibleWidth = 1;
            graphElement.flexibleHeight = 1;

            CreateLabel(graphCard.transform, "EULER ANGLES GRAPH (Roll=Red, Pitch=Green, Yaw=Cyan)");

            GameObject graphObj = new GameObject("GraphRawImage");
            graphObj.transform.SetParent(graphCard.transform, false);
            graphObj.AddComponent<RectTransform>();
            var graphController = graphObj.AddComponent<RealtimeGraphController>();
            graphController.BackgroundColor = new Color(0.05f, 0.05f, 0.05f, 1f); 
            uiManager.graphController = graphController;

            // --- Bottom Status Bar ---
            GameObject statusBar = new GameObject("StatusBar");
            statusBar.transform.SetParent(canvasObj.transform, false);
            RectTransform statusRect = statusBar.AddComponent<RectTransform>();
            statusRect.anchorMin = Vector2.zero; statusRect.anchorMax = new Vector2(1, 0);
            statusRect.pivot = new Vector2(0.5f, 0); statusRect.sizeDelta = new Vector2(0, 40);

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

            // --- Toast Notification Panel ---
            GameObject toastObj = new GameObject("ToastPanel");
            toastObj.transform.SetParent(canvasObj.transform, false);
            RectTransform toastRect = toastObj.AddComponent<RectTransform>();
            toastRect.anchorMin = new Vector2(0.5f, 1); toastRect.anchorMax = new Vector2(0.5f, 1);
            toastRect.pivot = new Vector2(0.5f, 1); toastRect.anchoredPosition = new Vector2(0, -60);
            toastRect.sizeDelta = new Vector2(400, 50);

            Image toastImg = toastObj.AddComponent<Image>();
            toastImg.sprite = roundedSprite; toastImg.type = Image.Type.Sliced;
            toastImg.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);

            var toastGroup = toastObj.AddComponent<CanvasGroup>();
            toastGroup.alpha = 0f; toastGroup.blocksRaycasts = false;

            Text tText = CreateText(toastObj.transform, "Notification", colorTextPrimary, 16, false);
            tText.alignment = TextAnchor.MiddleCenter;
            var tRect = tText.GetComponent<RectTransform>();
            tRect.anchorMin = Vector2.zero; tRect.anchorMax = Vector2.one;
            tRect.offsetMin = Vector2.zero; tRect.offsetMax = Vector2.zero;

            uiManager.toastPanel = toastRect;
            uiManager.toastText = tText;
            uiManager.toastCanvasGroup = toastGroup;

            Debug.Log("Apple-Style Scene Setup Complete! UI and 3D Avatar have been successfully generated.");
        }

        // --- Helper Methods ---
        private static void AttachMapper(Transform root, string targetName, PostureAnalyzer analyzer, int sensorId)
        {
            Transform target = FindChildRecursive(root, targetName);
            if (target != null)
            {
                var mapper = target.gameObject.AddComponent<SensorTransformMapper>();
                mapper.analyzer = analyzer;
                mapper.targetSensorId = sensorId;
            }
        }

        private static Transform FindChildRecursive(Transform parent, string exactName)
        {
            if (parent.name == exactName) return parent;
            foreach (Transform child in parent)
            {
                Transform found = FindChildRecursive(child, exactName);
                if (found != null) return found;
            }
            return null;
        }

        private static GameObject CreateCard(Transform parent, string name)
        {
            GameObject card = new GameObject(name);
            card.transform.SetParent(parent, false);
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
            element.flexibleHeight = 0;

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
            btnObj.transform.SetParent(parent, false);
            var element = btnObj.AddComponent<LayoutElement>();
            element.minHeight = 44;

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

        private static Text CreateText(Transform parent, string content, Color color, int fontSize, bool useFitter = true)
        {
            GameObject txtObj = new GameObject("Text");
            txtObj.transform.SetParent(parent, false);
            Text txt = txtObj.AddComponent<Text>();
            txt.text = content;
            txt.color = color;
            txt.fontSize = fontSize;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            
            if (useFitter) 
            {
                var fitter = txtObj.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            
            return txt;
        }

        private static Dropdown CreateDropdownList(Transform parent, string name)
        {
            DefaultControls.Resources uiResources = new DefaultControls.Resources();
            // Dropdownの背景のみ丸角を使用し、他（チェックマークや矢印）は標準を使用するためnullにする
            uiResources.standard = null;
            uiResources.background = roundedSprite;
            uiResources.dropdown = null;
            uiResources.checkmark = null;
            uiResources.mask = null;

            GameObject dropdownObj = DefaultControls.CreateDropdown(uiResources);
            dropdownObj.name = name;
            dropdownObj.transform.SetParent(parent, false);
            var element = dropdownObj.AddComponent<LayoutElement>();
            element.minHeight = 30;
            element.preferredHeight = 30;
            element.flexibleHeight = 0;
            element.flexibleWidth = 1;

            Dropdown dropdown = dropdownObj.GetComponent<Dropdown>();
            
            // 全体の背景色
            dropdown.GetComponent<Image>().color = new Color(0.95f, 0.95f, 0.95f, 1f);
            
            // 展開されたリストの背景色
            var templateImg = dropdown.template.GetComponent<Image>();
            if (templateImg != null) templateImg.color = new Color(0.9f, 0.9f, 0.9f, 1f);

            // フォントの割り当て
            Text labelTxt = dropdown.captionText;
            if (labelTxt != null)
            {
                labelTxt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                labelTxt.color = Color.black;
            }

            Text itemTxt = dropdown.itemText;
            if (itemTxt != null)
            {
                itemTxt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                itemTxt.color = Color.black;
            }

            return dropdown;
        }

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
