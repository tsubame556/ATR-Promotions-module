using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace InfantPostureApp
{
    /// <summary>
    /// UI（ボタンやテキスト）と裏側のデータ処理システムを繋ぐマネージャー
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
        public Text txtTableData; // 左下のリアルタイムデータ表（簡易テキスト）
        public Text[] txtStatusBars; // 下部のステータス（要素数5想定）

        private void Start()
        {
            // ボタンのイベント紐付け
            if (btnConnectAll != null)
                btnConnectAll.onClick.AddListener(OnConnectAllClicked);
            
            if (btnDisconnect != null)
                btnDisconnect.onClick.AddListener(OnDisconnectClicked);

            if (btnStartRecord != null)
                btnStartRecord.onClick.AddListener(() => dataManager?.StartRecording());

            if (btnStopRecord != null)
                btnStopRecord.onClick.AddListener(() => dataManager?.StopRecording());

            if (btnExportCSV != null)
                btnExportCSV.onClick.AddListener(() => dataManager?.ExportCSV());
        }

        private void OnConnectAllClicked()
        {
            if (postureAnalyzer == null) return;
            foreach (var driver in postureAnalyzer.SensorDrivers)
            {
                if (driver != null) driver.Connect(driver.portName);
            }
        }

        private void OnDisconnectClicked()
        {
            if (postureAnalyzer == null) return;
            foreach (var driver in postureAnalyzer.SensorDrivers)
            {
                if (driver != null) driver.Disconnect();
            }
        }

        private void Update()
        {
            if (postureAnalyzer == null) return;

            UpdateTableDisplay();
            UpdateStatusBars();
            UpdateGraphDisplay();
        }

        /// <summary>
        /// リアルタイム角度データテキストの更新
        /// </summary>
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

        /// <summary>
        /// 下部ステータスバーの更新
        /// </summary>
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
                    txtStatusBars[i].color = driver.IsConnected ? Color.green : Color.white;
                    if (driver.ConnectionStatus.Contains("Error")) txtStatusBars[i].color = Color.red;
                }
            }
        }

        /// <summary>
        /// グラフ描画コントローラへのデータ送信
        /// </summary>
        private void UpdateGraphDisplay()
        {
            if (graphController == null || postureAnalyzer.ActivePairs.Count == 0) return;

            // 代表として最初のペアの角度をグラフに描画する
            var targetPair = postureAnalyzer.ActivePairs[0];
            graphController.UpdateGraph(targetPair.RelativeEulerAngles);
        }
    }
}
