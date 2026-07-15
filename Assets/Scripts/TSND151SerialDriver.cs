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
            
            if (_isRunning)
            {
                // TSND151 計測停止コマンド (0x15) + パラメータ(0x00)
                SendCommand(0x15, new byte[] { 0x00 });
                System.Threading.Thread.Sleep(200); // コマンド送信完了まで少し待機
            }
            
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
                _serialPort.DtrEnable = true; // Mac SPP通信の安定化
                _serialPort.RtsEnable = true;
                _serialPort.Open();

                _isRunning = true;
                ConnectionStatus = "Connected";
                
                // SensorControllerと同様に、センサへ初期化設定を送信して「Bluetooth送信」を強制的にONにする
                
                // 1. 時刻設定 (0x11) - TSND仕様上必須になることがあるためダミー時刻を送信
                byte[] timeParams = new byte[] { 26, 7, 15, 12, 0, 0, 0, 0 }; // 2026/07/15 12:00
                SendCommand(0x11, timeParams);
                System.Threading.Thread.Sleep(50);

                // 2. 加速度・角速度設定 (0x16)
                // [計測周期(10ms), 送信回数(1=毎回送信), 記録回数(0=記録なし)]
                byte[] agsParams = new byte[] { 10, 1, 0 };
                SendCommand(0x16, agsParams);
                System.Threading.Thread.Sleep(50);

                // 3. クォータニオン設定 (0x55)
                // [計測周期(10ms), 送信回数(1=毎回送信), 記録回数(0=記録なし)]
                byte[] quatParams = new byte[] { 10, 1, 0 };
                SendCommand(0x55, quatParams);
                System.Threading.Thread.Sleep(50);

                // 4. TSND151 計測開始コマンド (0x13) + 相対時間での開始時刻(1秒後)と終了時刻(フリーラン)を指定する14バイトのパラメータ
                byte[] startParams = new byte[] {
                    0x00, // Mode: 0 (相対時間)
                    0x00, // Year
                    0x01, // Month
                    0x01, // Day
                    0x00, // Hour
                    0x00, // Minute
                    0x01, // Second (1秒後に開始)
                    0x00, // End Mode: 0 (相対時間)
                    0x00, // End Year
                    0x01, // End Month
                    0x01, // End Day
                    0x00, // End Hour
                    0x00, // End Minute
                    0x00  // End Second (0でフリーラン)
                };
                SendCommand(0x13, startParams);
                
                // 受信スレッドの開始
                _readThread = new Thread(ReadLoop);
                _readThread.IsBackground = true;
                _readThread.Start();

                Debug.Log($"[Sensor {sensorId}] Connected to {portName}");
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
                    // 実際の計測停止コマンドバイナリを送信 (0x15 = Stop Measurement)
                    SendCommand(0x15);
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
                    // Mac/Mono環境では BytesToRead が常に0を返すバグがあるため、
                    // 事前チェックを外して直接 Read でブロックさせ、タイムアウトで抜ける
                    int bytesRead = _serialPort.Read(readBuf, 0, readBuf.Length);
                    if (bytesRead > 0)
                    {
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

        public void SendCommand(byte cmd, byte[] args = null)
        {
            if (_serialPort == null || !_serialPort.IsOpen) return;
            try 
            {
                int len = (args != null) ? args.Length : 0;
                byte[] packet = new byte[3 + len];
                packet[0] = 0x9A; // Header
                packet[1] = cmd;  // Command code
                
                byte bcc = (byte)(0x9A ^ cmd);
                for (int i = 0; i < len; i++) {
                    packet[2 + i] = args[i];
                    bcc ^= args[i];
                }
                packet[2 + len] = bcc;
                
                _serialPort.Write(packet, 0, packet.Length);
                Debug.Log($"[Sensor {sensorId}] Sent Command: 0x{cmd:X2}");
            } 
            catch(Exception e) 
            {
                Debug.LogWarning($"[Sensor {sensorId}] Send Error: {e.Message}");
            }
        }

        private bool _hasLoggedFormat = false;

        private void ProcessBytes(byte[] newBytes, int length)
        {
            if (_rxBufferLength + length > _rxBuffer.Length)
            {
                _rxBufferLength = 0; // バッファオーバーフロー対策
            }
            Array.Copy(newBytes, 0, _rxBuffer, _rxBufferLength, length);
            _rxBufferLength += length;

            // 動的BCCパケットフレーミング
            // 長さが不明な場合でも、XORチェックサム(BCC)を全探索してパケットを特定します
            int parseIndex = 0;
            while (_rxBufferLength - parseIndex >= 3) // 最小: ヘッダ, コマンド, BCC
            {
                if (_rxBuffer[parseIndex] == 0x9A) // ヘッダ検出
                {
                    byte cmd = _rxBuffer[parseIndex + 1];
                    int validLength = -1;
                    int maxSearch = Math.Min(100, _rxBufferLength - parseIndex); // 一般的に1パケット100バイト以内
                    
                    // パケット長を仮定してBCCを計算し、末尾のBCCと一致するかチェック
                    for (int len = 3; len <= maxSearch; len++)
                    {
                        byte bcc = 0;
                        for (int i = 0; i < len - 1; i++) {
                            bcc ^= _rxBuffer[parseIndex + i];
                        }
                        if (bcc == _rxBuffer[parseIndex + len - 1]) {
                            validLength = len;
                            break; // 正しいパケット長を発見！
                        }
                    }

                    if (validLength != -1)
                    {
                        // パケットを抽出
                        byte[] packet = new byte[validLength];
                        Array.Copy(_rxBuffer, parseIndex, packet, 0, validLength);
                        
                        // 最初のデータパケットならHexダンプを出力して形式をログに残す（Discovery Mode）
                        if (!_hasLoggedFormat && (cmd == 0x80 || cmd == 0x8D)) 
                        {
                            Debug.Log($"<color=cyan>[TSND151-Discovery] Sensor {sensorId} Command: 0x{cmd:X2} / Length: {validLength} bytes\nData: {BitConverter.ToString(packet)}</color>");
                            _hasLoggedFormat = true;
                        }
                        
                        ParsePacket(packet);
                        parseIndex += validLength;
                    }
                    else
                    {
                        // BCCが一致する長さが見つからない場合
                        if (_rxBufferLength - parseIndex > 100) {
                            parseIndex++; // 100バイト探しても無い場合はノイズとみなして破棄
                        } else {
                            break; // まだパケットの全データが到着していないので待機
                        }
                    }
                }
                else
                {
                    parseIndex++; // 0x9Aが見つかるまで1バイトずつ読み飛ばし
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
