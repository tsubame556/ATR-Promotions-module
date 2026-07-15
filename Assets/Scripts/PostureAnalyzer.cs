using System.Collections.Generic;
using UnityEngine;

namespace InfantPostureApp
{
    [System.Serializable]
    public class SensorPair
    {
        public string PairName;
        public int ParentSensorId;
        public int ChildSensorId;
        
        // 算出された相対角度
        public Quaternion RelativeRotation { get; set; } = Quaternion.identity;
        public Vector3 RelativeEulerAngles { get; set; } = Vector3.zero;

        // UI描画用 (LineRendererやTextなど)
        [HideInInspector] public LineRenderer ConnectorLine;
    }

    [System.Serializable]
    public class SensorIndicatorMapping
    {
        public int SensorId;
        public Transform IndicatorTransform;
    }

    /// <summary>
    /// 複数センサのクォータニオンから、任意のペアの関節角度（相対オイラー角）を演算するクラス
    /// M対Nの柔軟な設定を許容する
    /// </summary>
    public class PostureAnalyzer : MonoBehaviour
    {
        [Header("Sensor Setup")]
        // ドライバのリスト（1〜5台）
        public List<TSND151SerialDriver> SensorDrivers = new List<TSND151SerialDriver>();
        
        // 3Dモデル上のセンサ装着部位（Indicator）のマッピング
        public List<SensorIndicatorMapping> SensorIndicators = new List<SensorIndicatorMapping>();
        
        [Header("Pair Configuration")]
        // ユーザーが動的に設定可能なペアリスト
        public List<SensorPair> ActivePairs = new List<SensorPair>();

        // 最新のセンサ姿勢を保持
        private Dictionary<int, Quaternion> _latestRotations = new Dictionary<int, Quaternion>();

        public void AddPair(string name, int parentId, int childId)
        {
            var pair = new SensorPair { PairName = name, ParentSensorId = parentId, ChildSensorId = childId };
            // TODO: UI上のLineRenderer等があればここで初期化・割り当てを行う
            ActivePairs.Add(pair);
        }

        public void RemovePair(SensorPair pair)
        {
            if (ActivePairs.Contains(pair))
            {
                if (pair.ConnectorLine != null) Destroy(pair.ConnectorLine.gameObject);
                ActivePairs.Remove(pair);
            }
        }

        private void Update()
        {
            // 1. 各センサドライバのキューから最新データを取得し更新
            foreach (var driver in SensorDrivers)
            {
                if (driver == null) continue;

                while (driver.DataQueue.TryDequeue(out SensorData data))
                {
                    _latestRotations[data.SensorId] = data.Rotation;
                    driver.Rotation = data.Rotation;

                    // Indicator（3D上の球体など）が存在すれば回転を適用
                    foreach (var mapping in SensorIndicators)
                    {
                        if (mapping.SensorId == data.SensorId && mapping.IndicatorTransform != null)
                        {
                            mapping.IndicatorTransform.rotation = data.Rotation;
                        }
                    }
                }
            }

            // 2. ペアごとの相対角度演算
            foreach (var pair in ActivePairs)
            {
                if (_latestRotations.TryGetValue(pair.ParentSensorId, out Quaternion qParent) &&
                    _latestRotations.TryGetValue(pair.ChildSensorId, out Quaternion qChild))
                {
                    // q_relative = q_parent^-1 * q_child
                    Quaternion qRelative = Quaternion.Inverse(qParent) * qChild;
                    pair.RelativeRotation = qRelative;
                    pair.RelativeEulerAngles = NormalizeEuler(qRelative.eulerAngles);

                    // 3D UI用のLine更新（親と子を結ぶ）
                    UpdatePairVisualization(pair);
                }
            }
        }

        /// <summary>
        /// オイラー角を -180 〜 180 の範囲に正規化する
        /// </summary>
        private Vector3 NormalizeEuler(Vector3 euler)
        {
            float NormalizeAngle(float angle)
            {
                while (angle > 180f) angle -= 360f;
                while (angle < -180f) angle += 360f;
                return angle;
            }

            return new Vector3(
                NormalizeAngle(euler.x),
                NormalizeAngle(euler.y),
                NormalizeAngle(euler.z)
            );
        }

        /// <summary>
        /// ペア間を結ぶ線の描画更新処理
        /// </summary>
        private void UpdatePairVisualization(SensorPair pair)
        {
            if (pair.ConnectorLine == null) return;
            
            if (SensorIndicators.TryGetValue(pair.ParentSensorId, out Transform pTransform) &&
                SensorIndicators.TryGetValue(pair.ChildSensorId, out Transform cTransform))
            {
                pair.ConnectorLine.SetPosition(0, pTransform.position);
                pair.ConnectorLine.SetPosition(1, cTransform.position);
                
                // TODO: 中間点に pair.RelativeEulerAngles を表示するTextUIの位置更新もここで行う
            }
        }
    }
}
