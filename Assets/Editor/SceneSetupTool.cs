#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace InfantPostureApp.Editor
{
    public class SceneSetupTool : UnityEditor.EditorWindow
    {
        [MenuItem("InfantPostureApp/シーン自動構築 (Scene Setup)")]
        public static void GenerateScene()
        {
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

            // 5台のドライバ生成とダミーモード設定
            for (int i = 1; i <= 5; i++)
            {
                var driverObj = new GameObject($"SensorDriver_{i}");
                driverObj.transform.SetParent(systemObj.transform);
                var driver = driverObj.AddComponent<TSND151SerialDriver>();
                driver.sensorId = i;
                driver.IsDummyMode = true; // 初期の動作テスト用にダミーモードON
                analyzer.SensorDrivers.Add(driver);
            }

            // テスト用のペア設定 (Sensor 1 -> 2)
            analyzer.AddPair("TestPair(1->2)", 1, 2);

            // 2. UI (Canvas) の構築
            GameObject canvasObj = new GameObject("Canvas");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObj.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasObj.AddComponent<GraphicRaycaster>();

            // EventSystemの追加（もし無ければ）
            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                GameObject eventSystem = new GameObject("EventSystem");
                eventSystem.AddComponent<EventSystem>();
                eventSystem.AddComponent<StandaloneInputModule>();
            }

            // 3. UI部品の生成（簡易版）
            // 左上: 操作パネル (TopLeft)
            RectTransform pnlTopLeft = CreatePanel(canvasObj.transform, "Panel_TopLeft", new Vector2(0, 0.5f), new Vector2(0.5f, 1));
            uiManager.btnConnectAll = CreateButton(pnlTopLeft, "Btn_ConnectAll", "Connect All (Dummy)", new Vector2(20, -20));
            uiManager.btnDisconnect = CreateButton(pnlTopLeft, "Btn_Disconnect", "Disconnect", new Vector2(20, -70));
            uiManager.btnStartRecord = CreateButton(pnlTopLeft, "Btn_StartRec", "Start Record", new Vector2(20, -120));
            uiManager.btnStopRecord = CreateButton(pnlTopLeft, "Btn_StopRec", "Stop Record", new Vector2(20, -170));
            uiManager.btnExportCSV = CreateButton(pnlTopLeft, "Btn_ExportCSV", "Export CSV", new Vector2(20, -220));

            // 左下: リアルタイム表 (BottomLeft)
            RectTransform pnlBottomLeft = CreatePanel(canvasObj.transform, "Panel_BottomLeft", new Vector2(0, 0.1f), new Vector2(0.5f, 0.5f));
            uiManager.txtTableData = CreateText(pnlBottomLeft, "Txt_Table", "Data will appear here...", new Vector2(10, -10));

            // 右下: グラフ領域 (BottomRight)
            RectTransform pnlBottomRight = CreatePanel(canvasObj.transform, "Panel_BottomRight", new Vector2(0.5f, 0.1f), new Vector2(1, 0.5f));
            GameObject graphObj = new GameObject("GraphRawImage");
            graphObj.transform.SetParent(pnlBottomRight);
            RectTransform graphRect = graphObj.AddComponent<RectTransform>();
            graphRect.anchorMin = Vector2.zero; graphRect.anchorMax = Vector2.one;
            graphRect.offsetMin = graphRect.offsetMax = Vector2.zero;
            var graphController = graphObj.AddComponent<RealtimeGraphController>();
            uiManager.graphController = graphController;

            // 最下部: ステータスバー (Bottom)
            RectTransform pnlBottom = CreatePanel(canvasObj.transform, "Panel_Bottom", new Vector2(0, 0), new Vector2(1, 0.1f));
            uiManager.txtStatusBars = new Text[5];
            for (int i = 0; i < 5; i++)
            {
                uiManager.txtStatusBars[i] = CreateText(pnlBottom, $"Txt_Status_{i+1}", $"Sensor {i+1} Status", new Vector2(10 + (i * 200), -10));
                uiManager.txtStatusBars[i].GetComponent<RectTransform>().sizeDelta = new Vector2(190, 30);
            }

            Debug.Log("Scene Setup Complete! The UI, Managers, and Dummy Sensors have been successfully generated.");
        }

        private static RectTransform CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            
            // 背景色（半透明の黒）
            Image img = obj.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0.3f);
            return rect;
        }

        private static Button CreateButton(Transform parent, string name, string label, Vector2 pos)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = pos;
            rect.sizeDelta = new Vector2(160, 40);

            Image img = obj.AddComponent<Image>();
            Button btn = obj.AddComponent<Button>();

            GameObject txtObj = new GameObject("Text");
            txtObj.transform.SetParent(obj.transform);
            RectTransform txtRect = txtObj.AddComponent<RectTransform>();
            txtRect.anchorMin = Vector2.zero; txtRect.anchorMax = Vector2.one;
            txtRect.offsetMin = txtRect.offsetMax = Vector2.zero;
            
            Text txt = txtObj.AddComponent<Text>();
            txt.text = label;
            txt.color = Color.black;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            return btn;
        }

        private static Text CreateText(Transform parent, string name, string label, Vector2 pos)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = pos;
            rect.sizeDelta = new Vector2(300, 200);

            Text txt = obj.AddComponent<Text>();
            txt.text = label;
            txt.color = Color.white;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            
            return txt;
        }
    }
}
#endif
