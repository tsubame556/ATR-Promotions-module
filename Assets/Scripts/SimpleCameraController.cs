using UnityEngine;
using UnityEngine.InputSystem;

namespace InfantPostureApp
{
    public class SimpleCameraController : MonoBehaviour
    {
        public float sensitivityX = 0.3f;
        public float sensitivityY = 0.3f;
        public float zoomSensitivity = 0.01f;
        public float moveSpeed = 0.5f;

        private float rotationX = 0f;
        private float rotationY = 0f;

        private void Start()
        {
            // 初期視点を「斜め横前」に設定
            transform.position = new Vector3(1.5f, 1.0f, 1.5f);
            transform.LookAt(new Vector3(0, 0.8f, 0));

            Vector3 angles = transform.eulerAngles;
            rotationX = angles.y;
            rotationY = angles.x;
        }

        private void LateUpdate()
        {
            if (Mouse.current == null) return;

            // 右クリックで視点回転（Look Around）
            if (Mouse.current.rightButton.isPressed)
            {
                rotationX += Mouse.current.delta.x.ReadValue() * sensitivityX;
                rotationY -= Mouse.current.delta.y.ReadValue() * sensitivityY;
                rotationY = Mathf.Clamp(rotationY, -89f, 89f);
                transform.localRotation = Quaternion.Euler(rotationY, rotationX, 0);
            }

            // マウスホイールで前進・後退（ズーム）
            float scroll = Mouse.current.scroll.y.ReadValue();
            if (Mathf.Abs(scroll) > 0.01f)
            {
                transform.Translate(Vector3.forward * scroll * zoomSensitivity, Space.Self);
            }

            // 中クリック（ホイール押し込み）または左クリック+Shiftで平行移動（Pan）
            bool shiftPressed = Keyboard.current != null && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
            if (Mouse.current.middleButton.isPressed || (Mouse.current.leftButton.isPressed && shiftPressed))
            {
                float moveX = -Mouse.current.delta.x.ReadValue() * moveSpeed * Time.deltaTime;
                float moveY = -Mouse.current.delta.y.ReadValue() * moveSpeed * Time.deltaTime;
                transform.Translate(new Vector3(moveX, moveY, 0), Space.Self);
            }
        }

        // ゲーム開始時にメインカメラに自動でアタッチする処理
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttachToCamera()
        {
            if (Camera.main != null)
            {
                if (Camera.main.GetComponent<SimpleCameraController>() == null)
                {
                    Camera.main.gameObject.AddComponent<SimpleCameraController>();
                    Debug.Log("[SimpleCameraController] メインカメラに視点操作スクリプト(Input System版)を自動追加しました。");
                }
            }
        }
    }
}
