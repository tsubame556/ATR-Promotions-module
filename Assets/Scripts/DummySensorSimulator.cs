using UnityEngine;

namespace InfantPostureApp
{
    /// <summary>
    /// 実機のセンサが接続されていない状態でも動作検証を行うためのダミーデータ生成クラス
    /// </summary>
    public class DummySensorSimulator : MonoBehaviour
    {
        public PostureAnalyzer analyzer;
        public float motionSpeed = 2f;
        public float motionAmplitude = 45f;

        private void Update()
        {
            if (analyzer == null) return;

            // ダミーモードが有効なドライバに対して、疑似的な姿勢データ（サイン波）を注入する
            foreach (var driver in analyzer.SensorDrivers)
            {
                if (driver != null && driver.IsDummyMode)
                {
                    // センサごとに少し位相をずらして回転を生成
                    float offset = driver.sensorId * 1.5f;
                    float angleX = Mathf.Sin(Time.time * motionSpeed + offset) * motionAmplitude;
                    float angleY = Mathf.Cos(Time.time * motionSpeed + offset) * motionAmplitude;
                    
                    Quaternion dummyRot = Quaternion.Euler(angleX, angleY, 0);

                    var data = new SensorData
                    {
                        SensorId = driver.sensorId,
                        Rotation = dummyRot,
                        Acceleration = Vector3.zero,
                        Gyroscope = Vector3.zero,
                        BatteryLevel = 100,
                        Timestamp = Time.realtimeSinceStartup
                    };

                    driver.DataQueue.Enqueue(data);
                }
            }
        }
    }
}
