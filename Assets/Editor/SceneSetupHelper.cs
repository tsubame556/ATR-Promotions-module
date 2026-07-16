using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using InfantPostureApp;

public class SceneSetupHelper : EditorWindow
{
    [MenuItem("Tools/Infant Posture/Setup Avatar & Graph UI")]
    public static void SetupScene()
    {
        // 1. Find AppUIManager and PostureAnalyzer
        AppUIManager uiManager = Object.FindFirstObjectByType<AppUIManager>();
        PostureAnalyzer analyzer = Object.FindFirstObjectByType<PostureAnalyzer>();

        if (uiManager == null || analyzer == null)
        {
            EditorUtility.DisplayDialog("Error", "Scene内にAppUIManagerまたはPostureAnalyzerが見つかりません。", "OK");
            return;
        }

        // 2. Setup Graph UI
        RealtimeGraphController graph = Object.FindFirstObjectByType<RealtimeGraphController>();
        if (graph == null)
        {
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                GameObject canvasObj = new GameObject("Canvas");
                canvas = canvasObj.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasObj.AddComponent<CanvasScaler>();
                canvasObj.AddComponent<GraphicRaycaster>();
            }

            GameObject graphObj = new GameObject("RealtimeGraph");
            graphObj.transform.SetParent(canvas.transform, false);
            
            RectTransform rect = graphObj.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(1, 0);
            rect.anchorMax = new Vector2(1, 0);
            rect.pivot = new Vector2(1, 0);
            rect.anchoredPosition = new Vector2(-20, 20);
            rect.sizeDelta = new Vector2(400, 200);

            RawImage rawImage = graphObj.AddComponent<RawImage>();
            graph = graphObj.AddComponent<RealtimeGraphController>();
            graph.GraphWidth = 400;
            graph.GraphHeight = 200;

            // テキストラベル追加
            GameObject labelObj = new GameObject("GraphLabel");
            labelObj.transform.SetParent(graphObj.transform, false);
            Text txt = labelObj.AddComponent<Text>();
            txt.text = "Relative Euler Angles\nRed:Roll, Green:Pitch, Cyan:Yaw";
            txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            txt.color = Color.white;
            txt.alignment = TextAnchor.UpperLeft;
            RectTransform labelRect = txt.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0, 1);
            labelRect.anchorMax = new Vector2(1, 1);
            labelRect.pivot = new Vector2(0, 1);
            labelRect.anchoredPosition = new Vector2(5, -5);
            labelRect.sizeDelta = new Vector2(300, 40);

            Undo.RegisterCreatedObjectUndo(graphObj, "Create Graph UI");
        }

        Undo.RecordObject(uiManager, "Link Graph");
        uiManager.graphController = graph;

        // 3. ユーザーへ案内
        EditorUtility.DisplayDialog("Setup Complete", 
            "グラフUIの生成とリンクが完了しました！\n\n" +
            "【アバターの割り当てについて】\n" +
            "PostureAnalyzerのInspectorにある「Sensor Indicators」のリストを開き、お持ちの3Dモデルの各パーツ（Transform）をSensorIdに紐付けてください。", "OK");
    }
}
