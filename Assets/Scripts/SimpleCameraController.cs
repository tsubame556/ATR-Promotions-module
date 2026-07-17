using UnityEngine;
using UnityEngine.InputSystem;

namespace InfantPostureApp
{
    public class SimpleCameraController : MonoBehaviour
    {
        public float sensitivityX = 0.3f;
        public float sensitivityY = 0.3f;
        public float zoomSensitivity = 0.005f;
        
        private float rotationX = 45f;
        private float rotationY = 20f;
        private float distance = 2.5f;
        
        private Vector3 targetCenter = new Vector3(0, 0.8f, 0);
        private bool _isDragging = false;

        private void Start()
        {
            Debug.Log("[SimpleCameraController] Start() が呼ばれました。カメラ制御を開始します。");
            
            // アバター（Animatorを持つオブジェクト）を検索して中心を自動設定
            var animator = Object.FindFirstObjectByType<Animator>();
            if (animator != null)
            {
                targetCenter = animator.transform.position + new Vector3(0, 0.8f, 0);
                Debug.Log($"[SimpleCameraController] Start() でアバターを発見しました。中心位置: {targetCenter}");
            }
            else
            {
                Debug.LogWarning("[SimpleCameraController] Start() でアバター(Animator)が見つかりませんでした。デフォルト位置 (0, 0.8, 0) を中心にします。");
            }
            
            UpdateCameraTransform();
        }

        private Vector2 _lastMousePos;
        private bool _wasPressedLastFrame;

        private void LateUpdate()
        {
            if (Mouse.current == null) return;

            bool isPressed = Mouse.current.rightButton.isPressed || Mouse.current.leftButton.isPressed;
            Vector2 currentMousePos = Mouse.current.position.ReadValue();

            // 押された瞬間に前回の位置をリセット
            if (isPressed && !_wasPressedLastFrame)
            {
                _lastMousePos = currentMousePos;
            }

            // 入力の取得（deltaを使わず、座標の差分を自前で計算する：Unityのバグ対策）
            if (isPressed)
            {
                float dx = currentMousePos.x - _lastMousePos.x;
                float dy = currentMousePos.y - _lastMousePos.y;

                // わずかでも動いたら回転
                if (Mathf.Abs(dx) > 0.1f || Mathf.Abs(dy) > 0.1f)
                {
                    rotationX += dx * sensitivityX;
                    rotationY -= dy * sensitivityY;
                    rotationY = Mathf.Clamp(rotationY, -89f, 89f);
                }
            }

            _lastMousePos = currentMousePos;
            _wasPressedLastFrame = isPressed;

            // マウスホイールでのズーム
            float scroll = Mouse.current.scroll.y.ReadValue();
            if (Mathf.Abs(scroll) > 0.01f)
            {
                distance -= scroll * zoomSensitivity;
                distance = Mathf.Clamp(distance, 0.1f, 50f);
            }

            // 常にアバター中心を向くように位置と回転を更新
            UpdateCameraTransform();
        }

        private void UpdateCameraTransform()
        {
            // 目標地点が原点のままの場合、念のためアバターを再検索
            if (targetCenter == new Vector3(0, 0.8f, 0) && Time.frameCount % 60 == 0)
            {
                var animator = Object.FindFirstObjectByType<Animator>();
                if (animator != null)
                {
                    targetCenter = animator.transform.position + new Vector3(0, 0.8f, 0);
                    Debug.Log($"[SimpleCameraController] アバターを発見しました。中心位置を更新: {targetCenter}");
                }
            }

            Quaternion rotation = Quaternion.Euler(rotationY, rotationX, 0);
            Vector3 position = targetCenter + rotation * new Vector3(0, 0, -distance);

            transform.rotation = rotation;
            transform.position = position;
        }

        // ゲーム開始時にカメラに自動でアタッチする処理
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttachToCamera()
        {
            Camera[] allCameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            bool attached = false;

            foreach (var cam in allCameras)
            {
                // UI用等のOrthographic（平行投影）カメラにはアタッチしない
                if (!cam.orthographic)
                {
                    if (cam.GetComponent<SimpleCameraController>() == null)
                    {
                        cam.gameObject.AddComponent<SimpleCameraController>();
                        Debug.Log($"[SimpleCameraController] 3D用カメラ ({cam.gameObject.name}) にオービットスクリプトを追加しました！");
                        attached = true;
                    }
                }
            }

            if (!attached)
            {
                Debug.LogError("[SimpleCameraController] シーン内に3D用のカメラ(Perspective)が見つかりません！");
            }
        }
    }
}
