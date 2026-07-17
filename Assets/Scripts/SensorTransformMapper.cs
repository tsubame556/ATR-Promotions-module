using UnityEngine;

namespace InfantPostureApp
{
    /// <summary>
    /// 特定のセンサ(TSND151)の回転データを、アバターの指定した複数ボーンに同期させるスクリプト。
    /// TSND151の独自の右手系からUnityの左手系へ、乳児の取り付け向きに合わせて座標変換を行います。
    /// </summary>
    public class SensorTransformMapper : MonoBehaviour
    {
        [Tooltip("全体の姿勢データを管理するPostureAnalyzerの参照")]
        public PostureAnalyzer analyzer;
        
        [Tooltip("このオブジェクトを同期させる対象のセンサID (1〜5)")]
        public int targetSensorId;

        [Tooltip("同期させるアバターのボーン（例: 両太ももの場合は2つのTransformを設定）")]
        public Transform[] targetTransforms;

        [Tooltip("モデルの初期向きを補正するための追加オフセット（必要時のみ）")]
        public Vector3 rotationOffset = Vector3.zero;

        [Header("Sensor Indicator")]
        [Tooltip("ボーンの位置にオレンジ色のセンサーインジケーターを表示するかどうか")]
        public bool showOrangeIndicator = true;
        [Tooltip("インジケーターのサイズ")]
        public float indicatorSize = 0.05f;

        // ボーンごとの初期回転を保持する配列
        private Quaternion[] _initialRotations;

        private void Start()
        {
            if (targetTransforms == null || targetTransforms.Length == 0)
            {
                Debug.LogWarning($"[SensorTransformMapper] Sensor {targetSensorId} の targetTransforms が設定されていません。");
                return;
            }

            _initialRotations = new Quaternion[targetTransforms.Length];
            for (int i = 0; i < targetTransforms.Length; i++)
            {
                if (targetTransforms[i] != null)
                {
                    // Tポーズなど、アバターのデフォルトのローカル回転を保存
                    _initialRotations[i] = targetTransforms[i].localRotation;

                    if (showOrangeIndicator)
                    {
                        CreateOrangeIndicator(targetTransforms[i]);
                    }
                }
            }
        }

        private void CreateOrangeIndicator(Transform target)
        {
            // オレンジ色の球体を生成してターゲットの子にする
            GameObject indicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            indicator.name = $"Sensor_{targetSensorId}_Indicator";
            indicator.transform.SetParent(target, false);
            indicator.transform.localPosition = Vector3.zero;
            indicator.transform.localScale = Vector3.one * indicatorSize;

            // コライダーは不要なので削除
            Destroy(indicator.GetComponent<Collider>());

            // オレンジ色のマテリアルを作成して適用
            Renderer rnd = indicator.GetComponent<Renderer>();
            Material orangeMat = new Material(Shader.Find("Standard"));
            orangeMat.color = new Color(1.0f, 0.5f, 0.0f); // オレンジ
            rnd.material = orangeMat;
        }

        private void Update()
        {
            if (analyzer == null || targetTransforms == null || _initialRotations == null) return;

            // 該当するSensorIDを持つドライバを検索
            var driver = analyzer.SensorDrivers.Find(d => d.sensorId == targetSensorId);
            if (driver != null)
            {
                // TSND151のクォータニオン (右手系)
                Quaternion rawQ = driver.Rotation;

                // ユーザー指定の厳密なルールに従ってマッピング
                Quaternion unityQ = PostureAnalyzer.MapSensorRotation(targetSensorId, rawQ);

                // アバターの空間（原点）に座標系（X:赤, Y:緑, Z:青）を表示する
                if (targetTransforms.Length > 0 && targetTransforms[0] != null)
                {
                    Transform root = targetTransforms[0];
                    Debug.DrawRay(root.position, root.right * 0.5f, Color.red);   // X軸
                    Debug.DrawRay(root.position, root.up * 0.5f, Color.green);    // Y軸
                    Debug.DrawRay(root.position, root.forward * 0.5f, Color.blue);// Z軸
                }

                // 追加のオフセットがあれば適用
                Quaternion offsetQ = Quaternion.Euler(rotationOffset);
                Quaternion finalSensorRot = unityQ * offsetQ;

                // 各ボーンの初期回転に対して、センサーの回転変化を掛け合わせる
                for (int i = 0; i < targetTransforms.Length; i++)
                {
                    if (targetTransforms[i] != null)
                    {
                        // initialRotation に finalSensorRot を掛け合わせることで、
                        // Tポーズを基準としてセンサーの回転分だけ姿勢が変わる
                        targetTransforms[i].localRotation = finalSensorRot * _initialRotations[i];
                    }
                }
            }
        }
    }
}
