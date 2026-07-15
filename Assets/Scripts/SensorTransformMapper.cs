using UnityEngine;

namespace InfantPostureApp
{
    /// <summary>
    /// 特定のセンサ(TSND151)の回転データを、自身のTransform(3Dモデル)に同期させるスクリプト
    /// </summary>
    public class SensorTransformMapper : MonoBehaviour
    {
        [Tooltip("全体の姿勢データを管理するPostureAnalyzerの参照")]
        public PostureAnalyzer analyzer;
        
        [Tooltip("このオブジェクトを同期させる対象のセンサID (1〜5)")]
        public int targetSensorId;

        [Tooltip("モデルの初期向きを補正するためのオフセット（オイラー角）")]
        public Vector3 rotationOffset = Vector3.zero;

        private void Update()
        {
            if (analyzer == null) return;

            // 該当するSensorIDを持つドライバを検索
            var driver = analyzer.SensorDrivers.Find(d => d.sensorId == targetSensorId);
            if (driver != null)
            {
                // ドライバの回転量に初期オフセットを加味してローカルの回転に適用
                transform.localRotation = driver.Rotation * Quaternion.Euler(rotationOffset);
            }
        }
    }
}
