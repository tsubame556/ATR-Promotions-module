using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace InfantPostureApp
{
    public class RecordFrame
    {
        public float Timestamp;
        // SensorId -> EulerAngles
        public Dictionary<int, Vector3> SensorEulers = new Dictionary<int, Vector3>();
        // PairName -> RelativeEulerAngles
        public Dictionary<string, Vector3> PairEulers = new Dictionary<string, Vector3>();
    }

    /// <summary>
    /// 計測データのメモリ記録と、CSVファイルへのエクスポートを担うクラス
    /// </summary>
    public class DataManager : MonoBehaviour
    {
        public PostureAnalyzer postureAnalyzer;
        public bool IsRecording { get; private set; } = false;

        private List<RecordFrame> _recordedData = new List<RecordFrame>();
        private float _recordStartTime;

        /// <summary>
        /// 計測開始（メモリバッファへ蓄積開始）
        /// </summary>
        public void StartRecording()
        {
            if (IsRecording) return;
            
            _recordedData.Clear();
            _recordStartTime = Time.time;
            IsRecording = true;
            Debug.Log("Recording Started.");
        }

        /// <summary>
        /// 計測終了
        /// </summary>
        public void StopRecording()
        {
            if (!IsRecording) return;
            
            IsRecording = false;
            Debug.Log($"Recording Stopped. Total Frames: {_recordedData.Count}");
        }

        private void Update()
        {
            if (!IsRecording || postureAnalyzer == null) return;

            // 毎フレームのデータを記録する
            var frame = new RecordFrame
            {
                Timestamp = Time.time - _recordStartTime
            };

            // 個別センサデータの記録
            foreach (var driver in postureAnalyzer.SensorDrivers)
            {
                if (driver != null && driver.IsConnected)
                {
                    // ※本来はdriver側から最新オイラーを取得するロジックが必要だが、
                    // ここではAnalyzer側で計算済みの値を取得すると仮定
                    // frame.SensorEulers[driver.sensorId] = ...
                }
            }

            // ペア相対角度データの記録
            foreach (var pair in postureAnalyzer.ActivePairs)
            {
                frame.PairEulers[pair.PairName] = pair.RelativeEulerAngles;
            }

            _recordedData.Add(frame);
        }

        /// <summary>
        /// 記録済みデータをCSV形式でエクスポートする
        /// 成功した場合は保存先パスを返し、失敗時はnullを返す
        /// </summary>
        public string ExportCSV()
        {
            if (_recordedData.Count == 0)
            {
                Debug.LogWarning("No data to export.");
                return null;
            }

            try
            {
                // Macのデスクトップディレクトリ等へ保存
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"InfantPostureData_{timestamp}.csv";
                string filePath = Path.Combine(desktopPath, fileName);

                StringBuilder sb = new StringBuilder();
                
                // ヘッダー生成
                sb.Append("Timestamp,");
                
                // 例として最初のフレームからヘッダー項目を抽出（本来は設定内容に依存する）
                var firstFrame = _recordedData[0];
                foreach (var pairKey in firstFrame.PairEulers.Keys)
                {
                    sb.Append($"{pairKey}_Roll,{pairKey}_Pitch,{pairKey}_Yaw,");
                }
                sb.AppendLine();

                // データ行生成
                foreach (var frame in _recordedData)
                {
                    sb.Append($"{frame.Timestamp:F3},");
                    
                    foreach (var pairKey in firstFrame.PairEulers.Keys)
                    {
                        if (frame.PairEulers.TryGetValue(pairKey, out Vector3 euler))
                        {
                            sb.Append($"{euler.x:F2},{euler.y:F2},{euler.z:F2},");
                        }
                        else
                        {
                            sb.Append(",,,");
                        }
                    }
                    sb.AppendLine();
                }

                File.WriteAllText(filePath, sb.ToString());
                Debug.Log($"CSV Exported to: {filePath}");
                
                return filePath;
            }
            catch (Exception e)
            {
                Debug.LogError($"CSV Export Failed: {e.Message}");
                return null;
            }
        }
    }
}
