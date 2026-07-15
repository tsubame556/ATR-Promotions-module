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
        // SensorId -> 生データ
        public Dictionary<int, SensorData> SensorDataMap = new Dictionary<int, SensorData>();
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

        public void StartRecording()
        {
            if (IsRecording) return;
            
            _recordedData.Clear();
            _recordStartTime = Time.time;
            IsRecording = true;
            Debug.Log("Recording Started.");
        }

        public void StopRecording()
        {
            if (!IsRecording) return;
            
            IsRecording = false;
            Debug.Log($"Recording Stopped. Total Frames: {_recordedData.Count}");
        }

        private void Update()
        {
            if (!IsRecording || postureAnalyzer == null) return;

            var frame = new RecordFrame
            {
                Timestamp = Time.time - _recordStartTime
            };

            // 個別の全生データを記録
            foreach (var kvp in postureAnalyzer.LatestSensorData)
            {
                frame.SensorDataMap[kvp.Key] = kvp.Value;
            }

            // ペア相対角度データの記録
            foreach (var pair in postureAnalyzer.ActivePairs)
            {
                frame.PairEulers[pair.PairName] = pair.RelativeEulerAngles;
            }

            _recordedData.Add(frame);
        }

        public string ExportCSV()
        {
            if (_recordedData.Count == 0)
            {
                Debug.LogWarning("No data to export.");
                return null;
            }

            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"InfantPostureData_{timestamp}.csv";
                string filePath = Path.Combine(desktopPath, fileName);

                StringBuilder sb = new StringBuilder();
                
                // --- ヘッダー生成 ---
                sb.Append("Timestamp,");
                
                var firstFrame = _recordedData[0];
                
                // 生データ用ヘッダー
                foreach (var sensorId in firstFrame.SensorDataMap.Keys)
                {
                    string p = $"S{sensorId}";
                    sb.Append($"{p}_Qw,{p}_Qx,{p}_Qy,{p}_Qz,");
                    sb.Append($"{p}_Roll,{p}_Pitch,{p}_Yaw,");
                    sb.Append($"{p}_AccX,{p}_AccY,{p}_AccZ,");
                    sb.Append($"{p}_GyroX,{p}_GyroY,{p}_GyroZ,");
                }

                // ペアデータ用ヘッダー
                foreach (var pairKey in firstFrame.PairEulers.Keys)
                {
                    sb.Append($"{pairKey}_Roll,{pairKey}_Pitch,{pairKey}_Yaw,");
                }
                sb.AppendLine();

                // --- データ行生成 ---
                foreach (var frame in _recordedData)
                {
                    sb.Append($"{frame.Timestamp:F3},");
                    
                    // 生データ出力
                    foreach (var sensorId in firstFrame.SensorDataMap.Keys)
                    {
                        if (frame.SensorDataMap.TryGetValue(sensorId, out SensorData sd))
                        {
                            var q = sd.Rotation;
                            var e = q.eulerAngles;
                            var a = sd.Acceleration;
                            var g = sd.Gyroscope;
                            
                            // eulerAnglesは0~360で返るので、必要に応じて-180~180に正規化すると見やすい
                            float roll = NormalizeAngle(e.x);
                            float pitch = NormalizeAngle(e.y);
                            float yaw = NormalizeAngle(e.z);

                            sb.Append($"{q.w:F4},{q.x:F4},{q.y:F4},{q.z:F4},");
                            sb.Append($"{roll:F2},{pitch:F2},{yaw:F2},");
                            sb.Append($"{a.x:F2},{a.y:F2},{a.z:F2},");
                            sb.Append($"{g.x:F2},{g.y:F2},{g.z:F2},");
                        }
                        else
                        {
                            sb.Append(",,,,,,,,,,,,,");
                        }
                    }

                    // ペアデータ出力
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

        private float NormalizeAngle(float angle)
        {
            while (angle > 180f) angle -= 360f;
            while (angle < -180f) angle += 360f;
            return angle;
        }
    }
}
