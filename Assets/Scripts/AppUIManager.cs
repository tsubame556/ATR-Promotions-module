using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace InfantPostureApp
{
    public enum AppState
    {
        Disconnected,
        Connected,
        Recording,
        Stopped
    }

    /// <summary>
    /// UIのイベント、状態遷移（ステートマシン）、通知を管理するマネージャー
    /// </summary>
    public class AppUIManager : MonoBehaviour
    {
        [Header("Core Systems")]
        public PostureAnalyzer postureAnalyzer;
        public DataManager dataManager;
        public RealtimeGraphController graphController;

        [Header("UI Controls")]
        public Button btnConnectAll;
        public Button btnDisconnect;
        public Button btnStartRecord;
        public Button btnStopRecord;
        public Button btnExportCSV;

        [Header("UI Displays")]
        public Text txtTableData;
        public Text[] txtStatusBars;
        public Dropdown[] portDropdowns; // 変更: InputFieldからDropdownへ
        public Button btnRefreshPorts;   // 追加: ポート再走査ボタン

        [Header("Toast Notification")]
        public RectTransform toastPanel;
        public Text toastText;
        public CanvasGroup toastCanvasGroup;

        // UI Colors
        private Color colorBlue = new Color(0.04f, 0.52f, 1.0f, 1f);
        private Color colorGray = new Color(0.23f, 0.23f, 0.24f, 1f);

        private AppState currentState = AppState.Disconnected;
        private Coroutine toastCoroutine;

        private void Start()
        {
            if (TSND151UdpManager.Instance == null)
            {
                gameObject.AddComponent<TSND151UdpManager>();
            }

            // ボタンのイベント紐付け
            if (btnConnectAll != null)
                btnConnectAll.onClick.AddListener(OnConnectAllClicked);
            
            if (btnDisconnect != null)
                btnDisconnect.onClick.AddListener(OnDisconnectClicked);

            if (btnStartRecord != null)
                btnStartRecord.onClick.AddListener(OnStartRecordClicked);

            if (btnStopRecord != null)
                btnStopRecord.onClick.AddListener(OnStopRecordClicked);

            if (btnExportCSV != null)
                btnExportCSV.onClick.AddListener(OnExportCSVClicked);

            if (btnRefreshPorts != null)
                btnRefreshPorts.onClick.AddListener(RefreshPorts);

            // 起動時に現在のポート一覧を取得してドロップダウンに反映
            RefreshPorts();

            // 初期状態の適用
            if (toastPanel != null) toastPanel.gameObject.SetActive(false);
            ChangeState(AppState.Disconnected);
        }

        private void ChangeState(AppState newState)
        {
            currentState = newState;

            switch (newState)
            {
                case AppState.Disconnected:
                    SetButtonState(btnConnectAll, true);
                    SetButtonState(btnDisconnect, false);
                    SetButtonState(btnStartRecord, false);
                    SetButtonState(btnStopRecord, false);
                    SetButtonState(btnExportCSV, false);
                    break;
                case AppState.Connected:
                    SetButtonState(btnConnectAll, false);
                    SetButtonState(btnDisconnect, true);
                    SetButtonState(btnStartRecord, true);
                    SetButtonState(btnStopRecord, false);
                    SetButtonState(btnExportCSV, false);
                    break;
                case AppState.Recording:
                    SetButtonState(btnConnectAll, false);
                    SetButtonState(btnDisconnect, false); // 記録中の切断を防止
                    SetButtonState(btnStartRecord, false);
                    SetButtonState(btnStopRecord, true);
                    SetButtonState(btnExportCSV, false);
                    break;
                case AppState.Stopped:
                    SetButtonState(btnConnectAll, false);
                    SetButtonState(btnDisconnect, true);
                    SetButtonState(btnStartRecord, false);
                    SetButtonState(btnStopRecord, false);
                    SetButtonState(btnExportCSV, true);
                    break;
            }
        }

        private void SetButtonState(Button btn, bool interactable)
        {
            if (btn == null) return;
            btn.interactable = interactable;
            
            Image img = btn.GetComponent<Image>();
            if (img != null)
            {
                img.color = interactable ? colorBlue : colorGray;
            }
        }

        private void OnConnectAllClicked()
        {
            if (postureAnalyzer == null) return;
            
            // sensorId と port のペアを収集
            var sensorMappings = new List<(int sensorId, string port)>();

            for (int i = 0; i < postureAnalyzer.SensorDrivers.Count; i++)
            {
                var driver = postureAnalyzer.SensorDrivers[i];
                if (driver == null) continue;

                // UIからポート名を取得（設定されていれば）
                string targetPort = driver.portName;
                if (portDropdowns != null && i < portDropdowns.Length && portDropdowns[i] != null)
                {
                    string selectedText = portDropdowns[i].options[portDropdowns[i].value].text;
                    if (selectedText == "None" || string.IsNullOrEmpty(selectedText))
                    {
                        Debug.Log($"[UIManager] Sensor {driver.sensorId} port is None. Skipping.");
                        continue; // Noneの場合は接続をスキップ
                    }
                    targetPort = selectedText;
                }

                driver.Connect(targetPort);
                if (!driver.IsDummyMode)
                {
                    sensorMappings.Add((driver.sensorId, targetPort));
                }
            }
            
            // Python UDPブリッジを起動（sensorIdとポートの対応を正確に伝達）
            if (TSND151UdpManager.Instance != null && sensorMappings.Count > 0)
            {
                TSND151UdpManager.Instance.StartBridge(sensorMappings);
            }
            
            ChangeState(AppState.Connected);
        }

        private void OnDisconnectClicked()
        {
            if (postureAnalyzer == null) return;
            foreach (var driver in postureAnalyzer.SensorDrivers)
            {
                if (driver != null) driver.Disconnect();
            }

            // Python UDPブリッジを停止
            if (TSND151UdpManager.Instance != null)
            {
                TSND151UdpManager.Instance.StopBridge();
            }

            ChangeState(AppState.Disconnected);
        }

        private void OnStartRecordClicked()
        {
            if (dataManager != null)
            {
                dataManager.StartRecording();
                ChangeState(AppState.Recording);
            }
        }

        private void OnStopRecordClicked()
        {
            if (dataManager != null)
            {
                dataManager.StopRecording();
                ChangeState(AppState.Stopped);
            }
        }

        private void OnExportCSVClicked()
        {
            if (dataManager != null)
            {
                string path = dataManager.ExportCSV();
                if (!string.IsNullOrEmpty(path))
                {
                    ShowToast("保存完了: " + path);
                    // 保存が完了したら再度Startボタンを光らせる(待機状態へ戻る)
                    ChangeState(AppState.Connected);
                }
                else
                {
                    ShowToast("保存に失敗しました");
                }
            }
        }

        public void RefreshPorts()
        {
            System.Collections.Generic.List<string> options = new System.Collections.Generic.List<string> { "None" };
            try
            {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
                if (System.IO.Directory.Exists("/dev"))
                {
                    // Python(pyserial)を使用するため、すべてのペアリング済み機器が確実に見えるttyを使用
                    string[] ttyPorts = System.IO.Directory.GetFiles("/dev", "tty.TSND151*");
                    options.AddRange(ttyPorts);
                }
#else
                // Windows等の場合の汎用シリアルポート取得
                options.AddRange(System.IO.Ports.SerialPort.GetPortNames());
#endif
            }
            catch (System.Exception e) { Debug.LogWarning("Port scan error: " + e.Message); }

            if (portDropdowns != null)
            {
                for (int i = 0; i < portDropdowns.Length; i++)
                {
                    var dropdown = portDropdowns[i];
                    if (dropdown != null)
                    {
                        dropdown.ClearOptions();
                        dropdown.AddOptions(options);

                        // 見つかったポートがあれば、Sensor 1は自動的に最初のポートを選択する（利便性のため）
                        if (i == 0 && options.Count > 1)
                        {
                            dropdown.value = 1;
                        }
                    }
                }
            }
        }

        private void Update()
        {
            if (postureAnalyzer == null) return;

            UpdateTableDisplay();
            UpdateStatusBars();
            UpdateGraphDisplay();
        }

        private void UpdateTableDisplay()
        {
            if (txtTableData == null) return;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== Realtime Angles ===");
            
            foreach (var pair in postureAnalyzer.ActivePairs)
            {
                sb.AppendLine($"[Pair: {pair.PairName}]");
                sb.AppendLine($"  Roll:  {pair.RelativeEulerAngles.x:F1}°");
                sb.AppendLine($"  Pitch: {pair.RelativeEulerAngles.y:F1}°");
                sb.AppendLine($"  Yaw:   {pair.RelativeEulerAngles.z:F1}°");
            }
            
            txtTableData.text = sb.ToString();
        }

        private void UpdateStatusBars()
        {
            if (txtStatusBars == null || txtStatusBars.Length == 0) return;

            for (int i = 0; i < postureAnalyzer.SensorDrivers.Count; i++)
            {
                if (i >= txtStatusBars.Length) break;
                var driver = postureAnalyzer.SensorDrivers[i];
                if (driver != null && txtStatusBars[i] != null)
                {
                    string statusTxt = $"Sensor {driver.sensorId}: {driver.ConnectionStatus} | Batt: {driver.CurrentBatteryLevel}%";
                    txtStatusBars[i].text = statusTxt;
                    
                    // 状態に応じた色分け
                    string status = driver.ConnectionStatus;
                    if (status.Contains("Error") || status.Contains("Timeout"))
                        txtStatusBars[i].color = Color.red;
                    else if (status.Contains("Connecting"))
                        txtStatusBars[i].color = Color.yellow;
                    else if (driver.IsConnected)
                        txtStatusBars[i].color = Color.green;
                    else
                        txtStatusBars[i].color = colorGray;
                }
            }
        }

        private void UpdateGraphDisplay()
        {
            if (graphController == null || postureAnalyzer == null) return;

            if (postureAnalyzer.ActivePairs.Count > 0)
            {
                var targetPair = postureAnalyzer.ActivePairs[0];
                graphController.UpdateGraph(targetPair.RelativeEulerAngles);
            }
            else if (postureAnalyzer.SensorDrivers.Count > 0)
            {
                // ペアが未設定の場合は、接続中の最初のセンサの生オイラー角をグラフ描画のフォールバックとする
                foreach (var driver in postureAnalyzer.SensorDrivers)
                {
                    if (driver != null && driver.IsConnected)
                    {
                        graphController.UpdateGraph(driver.Rotation.eulerAngles);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// トースト通知（ポップアップしてフェードアウトする）を表示
        /// </summary>
        public void ShowToast(string message)
        {
            if (toastPanel == null || toastText == null || toastCanvasGroup == null) return;

            if (toastCoroutine != null)
            {
                StopCoroutine(toastCoroutine);
            }
            toastCoroutine = StartCoroutine(ToastRoutine(message));
        }

        private IEnumerator ToastRoutine(string message)
        {
            toastText.text = message;
            toastPanel.gameObject.SetActive(true);
            toastCanvasGroup.alpha = 0f;

            // Fade in
            float t = 0;
            while (t < 0.2f)
            {
                t += Time.deltaTime;
                toastCanvasGroup.alpha = t / 0.2f;
                yield return null;
            }
            toastCanvasGroup.alpha = 1f;

            // Wait
            yield return new WaitForSeconds(3.5f);

            // Fade out
            t = 0;
            while (t < 0.3f)
            {
                t += Time.deltaTime;
                toastCanvasGroup.alpha = 1f - (t / 0.3f);
                yield return null;
            }
            toastCanvasGroup.alpha = 0f;
            toastPanel.gameObject.SetActive(false);
        }
    }
}
