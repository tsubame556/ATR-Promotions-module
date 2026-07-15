using System;
using System.Collections.Concurrent;
using System.IO.Ports;
using System.Threading;
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

    /// <summary>
    /// TSND151のシリアル通信およびデータパースを担うドライバクラス
    /// 複数台対応のため、1インスタンス1センサとして扱う構成を想定
    /// </summary>
    public class TSND151SerialDriver : MonoBehaviour
    {
        public int sensorId;
        public string portName = "/dev/tty.TSND151-XXXX"; // MacのBluetooth COMポート
        public int baudRate = 115200;

        private SerialPort _serialPort;
        private Thread _readThread;
        private bool _isRunning = false;

        // メインスレッドにデータを渡すためのスレッドセーフなキュー
        public ConcurrentQueue<SensorData> DataQueue = new ConcurrentQueue<SensorData>();

        // ダミーモード用フラグ
        public bool IsDummyMode = false;

        // ステータスプロパティ
        public bool IsConnected => (_serialPort != null && _serialPort.IsOpen) || IsDummyMode;
        public int CurrentBatteryLevel { get; private set; } = 100;
        public string ConnectionStatus { get; private set; } = "Disconnected";

        /// <summary>
        /// 指定したポートで接続を開始する
        /// </summary>
        public void Connect(string port)
        {
            if (IsConnected) return;

            portName = port;
            
            if (IsDummyMode)
            {
                ConnectionStatus = "Connected (Dummy)";
                Debug.Log($"[Sensor {sensorId}] Connected in Dummy Mode.");
                return;
            }

            try
            {
                // Mac/Mono環境でのシリアル通信設定
                _serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
                _serialPort.ReadTimeout = 100;
                _serialPort.WriteTimeout = 100;
                _serialPort.Open();

                _isRunning = true;
                ConnectionStatus = "Connected";
                
                // 受信スレッドの開始
                _readThread = new Thread(ReadLoop);
                _readThread.IsBackground = true;
                _readThread.Start();

                Debug.Log($"[Sensor {sensorId}] Connected to {portName}");
                
                // 計測開始コマンドの送信等が必要な場合はここに記述
                // TSND151の場合は、BCC付きのコマンドバイナリを構築して _serialPort.Write() を実行
            }
            catch (Exception e)
            {
                Debug.LogError($"[Sensor {sensorId}] Connection Error: {e.Message}");
                ConnectionStatus = "Error: " + e.Message;
            }
        }

        /// <summary>
        /// 接続を切断し、スレッドを停止する
        /// </summary>
        public void Disconnect()
        {
            _isRunning = false;

            if (_readThread != null && _readThread.IsAlive)
            {
                _readThread.Join(500); // スレッド終了を待つ
            }

            if (_serialPort != null && _serialPort.IsOpen)
            {
                // 計測停止コマンドの送信等をここで行う
                _serialPort.Close();
            }

            _serialPort = null;
            ConnectionStatus = "Disconnected";
            Debug.Log($"[Sensor {sensorId}] Disconnected.");
        }

        private void ReadLoop()
        {
            byte[] buffer = new byte[1024];

            while (_isRunning && _serialPort != null && _serialPort.IsOpen)
            {
                try
                {
                    if (_serialPort.BytesToRead > 0)
                    {
                        int bytesRead = _serialPort.Read(buffer, 0, buffer.Length);
                        ParseData(buffer, bytesRead);
                    }
                }
                catch (TimeoutException)
                {
                    // タイムアウトは無視してループ継続
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Sensor {sensorId}] Read Error: {e.Message}");
                    ConnectionStatus = "Read Error";
                    _isRunning = false;
                }
            }
        }

        /// <summary>
        /// 受信したバイト列からTSND151のパケットを解析し、クォータニオンや加速度を抽出する
        /// ※以下はパースの疑似的/簡易的な実装例。実際のプロトコルに従ってBCCチェックやヘッダ解析を行うこと。
        /// </summary>
        private void ParseData(byte[] data, int length)
        {
            // TODO: 実際のTSND151のパケットフォーマット（ヘッダ、レコードタイプ、BCC等）に従いパースする
            // 簡易的にダミーデータをキューに積む例
            
            // 例: byte配列からQuaternionを復元したと仮定
            Quaternion q = new Quaternion(0, 0, 0, 1); // Parsed Quaternion
            Vector3 acc = Vector3.zero; // Parsed Accel
            Vector3 gyro = Vector3.zero; // Parsed Gyro

            SensorData sd = new SensorData
            {
                SensorId = this.sensorId,
                Rotation = q,
                Acceleration = acc,
                Gyroscope = gyro,
                BatteryLevel = this.CurrentBatteryLevel,
                Timestamp = Time.realtimeSinceStartup // UnityAPI呼び出しはスレッドセーフでないため、別スレッドではSystem.DateTime等を利用するのが望ましい
            };

            DataQueue.Enqueue(sd);
        }

        private void OnDestroy()
        {
            Disconnect();
        }
    }
}
