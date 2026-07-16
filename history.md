# プロジェクト作業履歴 (Infant Posture App)

## 2026-07-16
- **OrbitCamera の入力システム修正**
  - Legacy Input (`Input.GetMouseButton`等) から 新しい Input System (`UnityEngine.InputSystem`) へ切り替え。マウスのドラッグによる回転、ホイールによるズームに対応。
- **TSND151接続安定性の劇的な向上 (Pythonブリッジ)**
  - Mac特有のゴーストポートバグ対策として、`system_profiler` から MACアドレスを抽出し、ポートが存在しない場合は `blueutil --connect` を用いてPythonからMacのBluetooth制御を強制実行する自己修復メカニズム (`force_mac_connection`) を実装。
  - `blueutil` のタイムアウトを10秒に延長し、複数センサーの連続接続時の遅延に対応。
  - センサー通信開始時 (`init_sensor`) に `ser.reset_input_buffer()` と `ser.reset_output_buffer()` を実行し、前回の強制終了によって残存したゾンビデータによる `force_stop` タイムアウトバグを解消。
- **UI表示名からデバイスパスへの対応**
  - `AppUIManager.cs` にて、ユーザーに見やすい短縮名（TSND151-AP...）から実際のシステムパス（/dev/tty...）への変換辞書 (`_portDisplayToFullPath`) を導入。
