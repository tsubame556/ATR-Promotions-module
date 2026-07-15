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

        public string pythonPath = "python3"; // Mac標準
        public int udpPort = 5000;

        private Process _pythonProcess;
        private UdpClient _udpClient;
        private Thread _receiveThread;
        private bool _isRunning = false;

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

        public void StartBridge(List<string> ports)
        {
            if (_isRunning) StopBridge();

            if (ports == null || ports.Count == 0) return;

            string portArgs = string.Join(" ", ports);
            string scriptPath = Application.dataPath + "/Python/tsnd_bridge.py";
            string venvPythonPath = Application.dataPath + "/Python/venv/bin/python";
            
            try
            {
                // Macのpython3で実行 (venv内のPythonを指定してpyserialを使えるようにする)
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = venvPythonPath;
                psi.Arguments = $"\"{scriptPath}\" --ports {portArgs}";
                psi.UseShellExecute = false;
                psi.RedirectStandardInput = true; // 標準入力をリダイレクトして、終了時にパイプを閉じてPythonを自動終了させる
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;

                _pythonProcess = new Process();
                _pythonProcess.StartInfo = psi;
                
                // ログの非同期出力
                _pythonProcess.OutputDataReceived += (sender, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.Log(e.Data); };
                _pythonProcess.ErrorDataReceived += (sender, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.LogError(e.Data); };
                
                _pythonProcess.Start();
                _pythonProcess.BeginOutputReadLine();
                _pythonProcess.BeginErrorReadLine();

                Debug.Log($"[UDPManager] Started Python bridge: {pythonPath} {psi.Arguments}");

                // UDP受信の開始
                _isRunning = true;
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
                _receiveThread.Join(200);
            }

            if (_pythonProcess != null && !_pythonProcess.HasExited)
            {
                try
                {
                    // 標準入力を閉じることで、Pythonスクリプト側がEOFを検知して安全に終了処理を行う
                    _pythonProcess.StandardInput.Close();
                    _pythonProcess.WaitForExit(1000);
                    if (!_pythonProcess.HasExited)
                    {
                        _pythonProcess.Kill();
                    }
                }
                catch { }
                _pythonProcess.Dispose();
                _pythonProcess = null;
                Debug.Log("[UDPManager] Stopped Python bridge.");
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
                    if (data.Length > 1)
                    {
                        int sensorId = data[0];
                        // data[1..] is the packet starting with 0x9A
                        byte[] packet = new byte[data.Length - 1];
                        Array.Copy(data, 1, packet, 0, packet.Length);
                        ParsePacket(sensorId, packet);
                    }
                }
                catch (SocketException)
                {
                    // UdpClient closed
                    break;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[UDPManager] UDP Receive error: {e.Message}");
                }
            }
        }

        private void ParsePacket(int sensorId, byte[] packet)
        {
            if (packet.Length < 2) return;
            byte cmd = packet[1];

            if (cmd == 0x8A && packet.Length >= 32)
            {
                // クォータニオン
                float qw = BitConverter.ToInt16(packet, 6) * 0.0001f;
                float qx = BitConverter.ToInt16(packet, 8) * 0.0001f;
                float qy = BitConverter.ToInt16(packet, 10) * 0.0001f;
                float qz = BitConverter.ToInt16(packet, 12) * 0.0001f;

                // 加速度
                float ax = ParseInt24(packet, 14) * 0.0001f;
                float ay = ParseInt24(packet, 17) * 0.0001f;
                float az = ParseInt24(packet, 20) * 0.0001f;

                Quaternion q = new Quaternion(qx, qy, qz, qw);
                Vector3 acc = new Vector3(ax, ay, az);

                // 対象のセンサドライバを探してデータを渡す
                var driver = TSND151SerialDriver.GetDriver(sensorId);
                if (driver != null)
                {
                    SensorData sd = new SensorData
                    {
                        SensorId = sensorId,
                        Rotation = q,
                        Acceleration = acc,
                        Gyroscope = Vector3.zero,
                        BatteryLevel = driver.CurrentBatteryLevel,
                        Timestamp = (float)DateTime.Now.TimeOfDay.TotalSeconds
                    };
                    driver.EnqueueData(sd);
                }
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
