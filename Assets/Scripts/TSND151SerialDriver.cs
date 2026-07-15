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

        private byte[] _rxBuffer = new byte[4096];
        private int _rxBufferLength = 0;

        // メインスレッドにデータを渡すためのスレッドセーフなキュー
        public ConcurrentQueue<SensorData> DataQueue = new ConcurrentQueue<SensorData>();

        // ダミーモード用フラグ
        public bool IsDummyMode = false;

        // ステータスプロパティ
        public bool IsConnected => (_serialPort != null && _serialPort.IsOpen) || IsDummyMode;
        public int CurrentBatteryLevel { get; private set; } = 100;
        public string ConnectionStatus { get; private set; } = "Disconnected";

        // 最新の回転データ（他スクリプトから参照可能）
        public Quaternion Rotation { get; set; } = Quaternion.identity;

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
                
                // 【要調整】実際の計測開始コマンドバイナリを送信
                // 例: byte[] startCmd = new byte[] { 0x9A, 0x00, 0x01, 0x13, 0x88 }; // (コマンド例)
                // _serialPort.Write(startCmd, 0, startCmd.Length);
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
                try 
                {
                    // 【要調整】実際の計測停止コマンドバイナリを送信
                    // byte[] stopCmd = new byte[] { 0x9A, 0x00, 0x01, 0x15, 0x8A };
                    // _serialPort.Write(stopCmd, 0, stopCmd.Length);
                } 
                catch { }

                _serialPort.Close();
            }

            _serialPort = null;
            ConnectionStatus = "Disconnected";
            Debug.Log($"[Sensor {sensorId}] Disconnected.");
        }

        private void ReadLoop()
        {
            byte[] readBuf = new byte[1024];

            while (_isRunning && _serialPort != null && _serialPort.IsOpen)
            {
                try
                {
                    if (_serialPort.BytesToRead > 0)
                    {
                        int bytesRead = _serialPort.Read(readBuf, 0, readBuf.Length);
                        ProcessBytes(readBuf, bytesRead);
                    }
                }
                catch (TimeoutException) { } // タイムアウトは通常動作
                catch (Exception e)
                {
                    Debug.LogWarning($"[Sensor {sensorId}] Read Error: {e.Message}");
                    ConnectionStatus = "Read Error";
                    _isRunning = false;
                }
            }
        }

        private void ProcessBytes(byte[] newBytes, int length)
        {
            // リングバッファまたは単純なバッファに追加
            if (_rxBufferLength + length > _rxBuffer.Length)
            {
                // バッファオーバーフロー対策
                _rxBufferLength = 0;
            }
            Array.Copy(newBytes, 0, _rxBuffer, _rxBufferLength, length);
            _rxBufferLength += length;

            // 汎用フレーミング処理（ヘッダ探索とパケット切り出し）
            // ※以下はお客様にてTSND151の実仕様に合わせて調整していただくための「枠組み」です。
            int parseIndex = 0;
            while (_rxBufferLength - parseIndex >= 3) // 最小パケット長を仮に3バイトとする
            {
                // 例: ヘッダが 0x9A だと仮定
                if (_rxBuffer[parseIndex] == 0x9A)
                {
                    // 例: 次のバイトがペイロード長だと仮定
                    int payloadLength = _rxBuffer[parseIndex + 1];
                    int packetLength = payloadLength + 3; // ヘッダ(1) + 長さ(1) + ペイロード + BCC(1)と仮定

                    // 異常な長さならヘッダを誤検知したとみなして進める
                    if (packetLength < 3 || packetLength > 200) 
                    {
                        parseIndex++;
                        continue;
                    }

                    if (_rxBufferLength - parseIndex >= packetLength)
                    {
                        // パケットが完全に到着している
                        byte[] packet = new byte[packetLength];
                        Array.Copy(_rxBuffer, parseIndex, packet, 0, packetLength);
                        
                        ParsePacket(packet);

                        parseIndex += packetLength;
                        continue;
                    }
                    else
                    {
                        // パケットの到着待ち
                        break;
                    }
                }
                else
                {
                    // ヘッダが見つからない場合は1バイト進める
                    parseIndex++;
                }
            }

            // 残りの未処理データをバッファ先頭に詰める
            int remaining = _rxBufferLength - parseIndex;
            if (remaining > 0 && parseIndex > 0)
            {
                Array.Copy(_rxBuffer, parseIndex, _rxBuffer, 0, remaining);
            }
            _rxBufferLength = remaining;
        }

        private void ParsePacket(byte[] packet)
        {
            // 【要調整】実際のTSND151パケットからクォータニオンや加速度を抽出する
            // ここでは仮にダミー値を生成しています。お客様の方でバイトオフセットを調整してください。
            // 例: float w = BitConverter.ToSingle(packet, 2);
            
            Quaternion q = new Quaternion(0, 0, 0, 1);
            Vector3 acc = Vector3.zero;
            Vector3 gyro = Vector3.zero;

            SensorData sd = new SensorData
            {
                SensorId = this.sensorId,
                Rotation = q,
                Acceleration = acc,
                Gyroscope = gyro,
                BatteryLevel = this.CurrentBatteryLevel,
                Timestamp = (float)DateTime.Now.TimeOfDay.TotalSeconds // スレッドセーフな時刻
            };

            DataQueue.Enqueue(sd);
        }

        private void OnDestroy()
        {
            Disconnect();
        }
    }
}
