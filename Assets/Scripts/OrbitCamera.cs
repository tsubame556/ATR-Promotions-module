using UnityEngine;
using UnityEngine.InputSystem;

namespace InfantPostureApp
{
    /// <summary>
    /// 指定したターゲット（センサー2が紐づく胴体など）を中心に、
    /// マウス操作で視点を回転・ズームできるカメラスクリプト。
    /// （新しい Input System 対応版）
    /// </summary>
    public class OrbitCamera : MonoBehaviour
    {
        [Tooltip("カメラが常に中心に捉えるターゲット")]
        public Transform target;

        [Tooltip("ターゲットからの距離")]
        public float distance = 2.0f;
        public float minDistance = 0.5f;
        public float maxDistance = 10.0f;

        [Tooltip("回転の感度")]
        public float xSpeed = 10.0f;
        public float ySpeed = 10.0f;

        [Tooltip("Y軸（縦）回転の制限角度")]
        public float yMinLimit = -20f;
        public float yMaxLimit = 80f;

        [Tooltip("ズームの感度")]
        public float scrollSensitivity = 0.01f;

        private float x = 0.0f;
        private float y = 0.0f;

        void Start()
        {
            Vector3 angles = transform.eulerAngles;
            x = angles.y;
            y = angles.x;

            if (target != null)
            {
                x = 0f;
                y = 30f;
            }
        }

        void LateUpdate()
        {
            if (target == null)
            {
                Debug.LogWarning("[OrbitCamera] ターゲットが設定されていません。Inspectorでターゲット（胴体ボーン等）を割り当ててください。");
                return;
            }

            if (Mouse.current != null)
            {
                // 右クリックまたは左クリックドラッグで回転
                if (Mouse.current.leftButton.isPressed || Mouse.current.rightButton.isPressed)
                {
                    Vector2 mouseDelta = Mouse.current.delta.ReadValue();
                    x += mouseDelta.x * xSpeed * 0.02f;
                    y -= mouseDelta.y * ySpeed * 0.02f;
                }

                // スクロールホイールでズーム
                float scroll = Mouse.current.scroll.ReadValue().y;
                distance -= scroll * scrollSensitivity;
                distance = Mathf.Clamp(distance, minDistance, maxDistance);
            }

            // 角度制限
            y = ClampAngle(y, yMinLimit, yMaxLimit);

            // カメラの位置と回転を計算
            Quaternion rotation = Quaternion.Euler(y, x, 0);
            Vector3 position = rotation * new Vector3(0.0f, 0.0f, -distance) + target.position;

            transform.rotation = rotation;
            transform.position = position;
        }

        static float ClampAngle(float angle, float min, float max)
        {
            if (angle < -360F)
                angle += 360F;
            if (angle > 360F)
                angle -= 360F;
            return Mathf.Clamp(angle, min, max);
        }
    }
}
