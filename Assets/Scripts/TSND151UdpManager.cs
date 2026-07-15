using UnityEngine;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace InfantPostureApp
{
    public class TSND151UdpManager : MonoBehaviour
    {
        public static TSND151UdpManager Instance { get; private set; }

        public int udpPort = 5000;

        private Process _pythonProcess;
        private UdpClient _udpClient;
        private Thread _receiveThread;
        private bool _isRunning = false;

        // パケット受信カウンタ（デバッグ用）
        private int _packetCount = 0;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// Pythonブリッジを起動し、UDP受信を開始する
        /// sensorMappings: (sensorId, portName) のリスト
        /// </summary>
        public void StartBridge(List<(int sensorId, string port)> sensorMappings)
        {
            if (_isRunning) StopBridge();

            if (sensorMappings == null || sensorMappings.Count == 0) return;

            // sensorId:port 形式の引数を構築
            List<string> sensorArgs = new List<string>();
            foreach (var (sensorId, port) in sensorMappings)
            {
                sensorArgs.Add($"{sensorId}:{port}");
            }
            string sensorArgsStr = string.Join(" ", sensorArgs);
            
            string scriptPath = Application.dataPath + "/Python/tsnd_bridge.py";
            string venvPythonPath = Application.dataPath + "/Python/venv/bin/python";
            
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = venvPythonPath;
                psi.Arguments = $"\"{scriptPath}\" --sensors {sensorArgsStr}";
                psi.UseShellExecute = false;
                psi.RedirectStandardInput = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;

                _pythonProcess = new Process();
                _pythonProcess.StartInfo = psi;
                
                // Pythonからのログ出力をUnityコンソールに表示
                _pythonProcess.OutputDataReceived += (sender, e) => 
                { 
                    if (!string.IsNullOrEmpty(e.Data)) 
                        Debug.Log($"[Python] {e.Data}"); 
                };
                _pythonProcess.ErrorDataReceived += (sender, e) => 
                { 
                    if (!string.IsNullOrEmpty(e.Data)) 
                        Debug.Log($"[Python] {e.Data}"); // stderrもLogとして表示（エラーではなくログとして扱う）
                };
                
                _pythonProcess.Start();
                _pythonProcess.BeginOutputReadLine();
                _pythonProcess.BeginErrorReadLine();

                Debug.Log($"[UDPManager] Python bridge started: {psi.FileName} {psi.Arguments}");

                // UDP受信開始
                _isRunning = true;
                _packetCount = 0;
                _udpClient = new UdpClient(udpPort);
                _receiveThread = new Thread(ReceiveLoop);
                _receiveThread.IsBackground = true;
                _receiveThread.Start();
            }
            catch (Exception e)
            {
                Debug.LogError($"[UDPManager] Failed to start Python bridge: {e.Message}");
            }
        }

        public void StopBridge()
        {
            _isRunning = false;
            
            if (_udpClient != null)
            {
                _udpClient.Close();
                _udpClient = null;
            }

            if (_receiveThread != null && _receiveThread.IsAlive)
            {
                _receiveThread.Join(500);
            }

            if (_pythonProcess != null && !_pythonProcess.HasExited)
            {
                try
                {
                    _pythonProcess.StandardInput.Close();
                    _pythonProcess.WaitForExit(2000);
                    if (!_pythonProcess.HasExited)
                    {
                        _pythonProcess.Kill();
                    }
                }
                catch { }
                _pythonProcess.Dispose();
                _pythonProcess = null;
                Debug.Log("[UDPManager] Python bridge stopped.");
            }
        }

        private void ReceiveLoop()
        {
            IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
            while (_isRunning)
            {
                try
                {
                    byte[] data = _udpClient.Receive(ref remoteEndPoint);
                    if (data.Length >= 31)  // 1(sensorId) + 30(params)
                    {
                        int sensorId = data[0];
                        byte[] params_ = new byte[30];
                        Array.Copy(data, 1, params_, 0, 30);
                        ParseQuaternionAccGyro(sensorId, params_);
                        
                        _packetCount++;
                        if (_packetCount % 500 == 1)
                        {
                            Debug.Log($"[UDPManager] Total UDP packets received: {_packetCount}");
                        }
                    }
                }
                catch (SocketException)
                {
                    break;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[UDPManager] UDP error: {e.Message}");
                }
            }
        }

        /// <summary>
        /// 公式仕様に準拠した0x8Aパケット（パラメータ30バイト）の解析
        /// オフセット:
        ///   [0-3]   タイムスタンプ (uint32, Little-Endian, ms)
        ///   [4-11]  クォータニオン W,X,Y,Z (int16 x4, Little-Endian)
        ///   [12-20] 加速度 X,Y,Z (int24 x3, Little-Endian, 0.1mg単位)
        ///   [21-29] ジャイロ X,Y,Z (int24 x3, Little-Endian, 0.01dps単位)
        /// </summary>
        private void ParseQuaternionAccGyro(int sensorId, byte[] p)
        {
            // タイムスタンプ
            uint timestamp_ms = BitConverter.ToUInt32(p, 0);
            
            // クォータニオン (int16, スケール: 値/10000 で正規化されたクォータニオン成分)
            float qw = BitConverter.ToInt16(p, 4) / 10000f;
            float qx = BitConverter.ToInt16(p, 6) / 10000f;
            float qy = BitConverter.ToInt16(p, 8) / 10000f;
            float qz = BitConverter.ToInt16(p, 10) / 10000f;

            // 加速度 (int24, 0.1mg単位 → G単位に変換: value * 0.1 / 1000 = value * 0.0001)
            float ax = ParseInt24(p, 12) * 0.0001f;
            float ay = ParseInt24(p, 15) * 0.0001f;
            float az = ParseInt24(p, 18) * 0.0001f;

            // ジャイロ (int24, 0.01dps単位)
            float gx = ParseInt24(p, 21) * 0.01f;
            float gy = ParseInt24(p, 24) * 0.01f;
            float gz = ParseInt24(p, 27) * 0.01f;

            Quaternion q = new Quaternion(qx, qy, qz, qw);
            Vector3 acc = new Vector3(ax, ay, az);
            Vector3 gyro = new Vector3(gx, gy, gz);

            // デバッグ用: 生データをコンソールに出力（100回に1回の頻度で間引いて表示）
            if (_packetCount % 100 == 1)
            {
                Debug.Log($"[RawData Sensor {sensorId}] Q({qw:F2}, {qx:F2}, {qy:F2}, {qz:F2}) | Acc({ax:F2}, {ay:F2}, {az:F2}) | Gyro({gx:F2}, {gy:F2}, {gz:F2})");
            }

            var driver = TSND151SerialDriver.GetDriver(sensorId);
            if (driver != null)
            {
                SensorData sd = new SensorData
                {
                    SensorId = sensorId,
                    Rotation = q,
                    Acceleration = acc,
                    Gyroscope = gyro,
                    BatteryLevel = driver.CurrentBatteryLevel,
                    Timestamp = timestamp_ms / 1000f
                };
                driver.EnqueueData(sd);
            }
        }

        private int ParseInt24(byte[] buf, int offset)
        {
            int value = buf[offset] | (buf[offset + 1] << 8) | (buf[offset + 2] << 16);
            if ((value & 0x800000) != 0)
            {
                value |= unchecked((int)0xFF000000);
            }
            return value;
        }

        private void OnDestroy()
        {
            StopBridge();
        }
    }
}
