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
                        continue;
                    }
                    // 短縮表示名をフルパスに復元（対応がなければそのまま使用）
                    if (_portDisplayToFullPath.TryGetValue(selectedText, out string fullPath))
                    {
                        targetPort = fullPath;
                    }
                    else
                    {
                        targetPort = selectedText;
                    }
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
                if (TSND151UdpManager.Instance != null)
                {
                    TSND151UdpManager.Instance.StartMeasurement();
                }
                dataManager.StartRecording();
                ChangeState(AppState.Recording);
            }
        }

        private void OnStopRecordClicked()
        {
            if (dataManager != null)
            {
                if (TSND151UdpManager.Instance != null)
                {
                    TSND151UdpManager.Instance.StopMeasurement();
                }
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

        // ドロップダウンの短縮表示名 → フルパスの対応表
        private Dictionary<string, string> _portDisplayToFullPath = new Dictionary<string, string>();

        public void RefreshPorts()
        {
            System.Collections.Generic.List<string> options = new System.Collections.Generic.List<string> { "None" };
            _portDisplayToFullPath.Clear();
            _portDisplayToFullPath["None"] = "";
            HashSet<string> addedPorts = new HashSet<string>();

            try
            {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
                // 1. /devに既に存在するポートを追加
                if (System.IO.Directory.Exists("/dev"))
                {
                    string[] ttyPorts = System.IO.Directory.GetFiles("/dev", "tty.TSND151*");
                    foreach (var p in ttyPorts)
                    {
                        if (addedPorts.Add(p))
                        {
                            // "/dev/tty.TSND151-AP09182352" → "TSND151-AP09182352"
                            string displayName = p.Replace("/dev/tty.", "");
                            options.Add(displayName);
                            _portDisplayToFullPath[displayName] = p;
                        }
                    }
                }

                // 2. system_profiler でペアリング済みBluetoothデバイスからTSND151を検出
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "/usr/sbin/system_profiler",
                        Arguments = "SPBluetoothDataType",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };
                    var proc = System.Diagnostics.Process.Start(psi);
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(3000);

                    var lines = output.Split('\n');
                    foreach (var line in lines)
                    {
                        string trimmed = line.Trim();
                        if (trimmed.Contains("TSND151"))
                        {
                            string deviceName = trimmed.TrimEnd(':', ' ').Trim();
                            if (deviceName.Length > 0)
                            {
                                string fullPath = "/dev/tty." + deviceName;
                                if (addedPorts.Add(fullPath))
                                {
                                    options.Add(deviceName);
                                    _portDisplayToFullPath[deviceName] = fullPath;
                                    Debug.Log($"[PortScan] Predicted port: {deviceName} → {fullPath}");
                                }
                            }
                        }
                    }
                }
                catch (System.Exception e2)
                {
                    Debug.LogWarning("[PortScan] system_profiler scan failed: " + e2.Message);
                }
#else
                options.AddRange(System.IO.Ports.SerialPort.GetPortNames());
#endif
            }
            catch (System.Exception e) { Debug.LogWarning("Port scan error: " + e.Message); }

            Debug.Log($"[PortScan] Found {options.Count - 1} port(s): {string.Join(", ", options)}");

            if (portDropdowns != null)
            {
                for (int i = 0; i < portDropdowns.Length; i++)
                {
                    var dropdown = portDropdowns[i];
                    if (dropdown != null)
                    {
                        dropdown.ClearOptions();
                        dropdown.AddOptions(options);

                        // ドロップダウン展開時のテンプレート幅を広げて全文字表示
                        var template = dropdown.template;
                        if (template != null)
                        {
                            template.sizeDelta = new Vector2(350f, template.sizeDelta.y);
                        }

                        // 自動割り当て
                        if (options.Count > i + 1)
                        {
                            dropdown.value = i + 1;
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
            
            // 1. 各センサ単体の生角度（Roll, Pitch, Yaw）を表示
            sb.AppendLine("=== Absolute Angles ===");
            bool hasConnected = false;
            foreach (var driver in postureAnalyzer.SensorDrivers)
            {
                if (driver != null && driver.IsConnected)
                {
                    hasConnected = true;
                    var e = driver.Rotation.eulerAngles;
                    float rx = e.x > 180 ? e.x - 360 : e.x;
                    float ry = e.y > 180 ? e.y - 360 : e.y;
                    float rz = e.z > 180 ? e.z - 360 : e.z;
                    sb.AppendLine($"Sensor {driver.sensorId}: R={rx:F1}° P={ry:F1}° Y={rz:F1}°");
                    sb.AppendLine($"  Acc: {driver.Acceleration.x:F2}, {driver.Acceleration.y:F2}, {driver.Acceleration.z:F2}");
                    sb.AppendLine($"  Gyr: {driver.Gyroscope.x:F1}, {driver.Gyroscope.y:F1}, {driver.Gyroscope.z:F1}");
                }
            }
            if (!hasConnected) sb.AppendLine("No sensors connected.");
            
            sb.AppendLine();
            
            // 2. ペアごとの相対角度を表示
            sb.AppendLine("=== Relative Angles ===");
            if (postureAnalyzer.ActivePairs.Count == 0)
            {
                sb.AppendLine("(No Pairs Configured)");
            }
            else
            {
                foreach (var pair in postureAnalyzer.ActivePairs)
                {
                    // 内部のID設定が漏れている場合（0->0など）が一目でわかるように表示
                    sb.AppendLine($"[{pair.PairName} (S{pair.ParentSensorId}->S{pair.ChildSensorId})]");
                    sb.AppendLine($"  Roll:  {pair.RelativeEulerAngles.x:F1}°");
                    sb.AppendLine($"  Pitch: {pair.RelativeEulerAngles.y:F1}°");
                    sb.AppendLine($"  Yaw:   {pair.RelativeEulerAngles.z:F1}°");
                }
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
