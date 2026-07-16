using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

namespace InfantPostureApp
{
    public class SensorData
    {
        public int SensorId;
        public Quaternion Rotation;
        public Vector3 Acceleration;
        public Vector3 Gyroscope;
        public int BatteryLevel;
        public float Timestamp;
    }

    public class TSND151SerialDriver : MonoBehaviour
    {
        [Header("Settings")]
        public int sensorId;
        public string portName = "/dev/cu.TSND151-XXXX"; // Pythonブリッジに渡すためのポート名保持
        public int baudRate = 115200; 

        // UDPManagerからセンサIDでインスタンスを検索するための辞書
        private static Dictionary<int, TSND151SerialDriver> _driverInstances = new Dictionary<int, TSND151SerialDriver>();

        public static TSND151SerialDriver GetDriver(int id)
        {
            if (_driverInstances.TryGetValue(id, out var driver))
            {
                return driver;
            }
            return null;
        }

        // メインスレッドにデータを渡すためのスレッドセーフなキュー
        public ConcurrentQueue<SensorData> DataQueue = new ConcurrentQueue<SensorData>();

        // ダミーモード用フラグ
        public bool IsDummyMode = false;

        // ステータスプロパティ
        public bool IsConnected { get; private set; } = false;
        public int CurrentBatteryLevel { get; private set; } = 100;
        public string ConnectionStatus { get; private set; } = "Disconnected";

        // 最新の回転データ（他スクリプトから参照可能）
        public Quaternion Rotation { get; set; } = Quaternion.identity;
        public Vector3 Acceleration { get; set; } = Vector3.zero;
        public Vector3 Gyroscope { get; set; } = Vector3.zero;

        private float _lastDataTime = 0f;
        private volatile bool _hasNewData = false;

        private void Awake()
        {
            // 同じIDのドライバが複数存在する場合は上書きする
            _driverInstances[sensorId] = this;
        }

        private void OnDestroy()
        {
            if (_driverInstances.ContainsKey(sensorId))
            {
                _driverInstances.Remove(sensorId);
            }
        }

        /// <summary>
        /// 実際のシリアル通信はTSND151UdpManager(Pythonブリッジ)が行うため、
        /// ここではUI上の状態更新とポート名の記憶のみを行う。
        /// </summary>
        public void Connect(string port)
        {
            if (IsConnected) return;

            portName = port;
            
            if (IsDummyMode)
            {
                ConnectionStatus = "Connected (Dummy)";
                IsConnected = true;
                Debug.Log($"[Sensor {sensorId}] Connected in Dummy Mode.");
                return;
            }

            IsConnected = false;
            ConnectionStatus = "Connecting...";
        }

        private void Update()
        {
            if (!IsDummyMode)
            {
                if (_hasNewData)
                {
                    IsConnected = true;
                    ConnectionStatus = "Connected";
                    _lastDataTime = Time.time;
                    _hasNewData = false;
                }

                if (IsConnected && Time.time - _lastDataTime > 3.0f)
                {
                    IsConnected = false;
                    ConnectionStatus = "Data Timeout";
                }
            }
        }

        public void Disconnect()
        {
            IsConnected = false;
            ConnectionStatus = "Disconnected";
        }

        public void EnqueueData(SensorData sd)
        {
            if (!IsDummyMode)
            {
                _hasNewData = true;
            }
            DataQueue.Enqueue(sd);
        }
    }
}
