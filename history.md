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
- **計測開始のUI連動化**
  - `tsnd_bridge.py`: `init_sensor`から計測開始(`0x13`)を分離。
  - `TSND151UdpManager.cs`: `StartMeasurement`, `StopMeasurement`を追加しPythonの標準入力にコマンド送信。
  - `AppUIManager.cs`: 記録開始/停止ボタンと連動し、全センサーの計測を同時開始・同時停止するよう改善。
- **スタンバイ時のUIステータス表示修正**
  - 計測データが届かないスタンバイ状態でも、Python側からの`initialized successfully`メッセージを検知し、UIのステータスバーを緑色(Connected)にするよう修正。
- **【根本原因修正】シリアルポートを /dev/tty.* → /dev/cu.* に変更**
  - macOSでは `/dev/tty.*` はDCD（Data Carrier Detect）信号を待つためシリアル通信がブロックされ、全てのACKがタイムアウトしていた。
  - Bluetooth SPPの発信側接続では `/dev/cu.*` を使う必要があるため、`AppUIManager.cs` のポート生成ロジックを修正。
  - `tsnd_bridge.py` の `serial.Serial` に `rtscts=False, dsrdtr=False` を明示的に指定し、ハードウェアフロー制御によるブロッキングも防止。
  - Part 1 のポートスキャン（`/dev/`直接スキャン）も `tty.TSND151*` → `cu.TSND151*` に変更し、Part 2（system_profiler予測）との重複登録（同じセンサーが2回ドロップダウンに表示される問題）を解消。
- **ゾンビプロセス対策とBluetooth接続リセットの修正**
  - `TSND151UdpManager.cs`: `StartBridge`開始時と`OnDestroy`時に`pkill -f tsnd_bridge.py`を実行し、前回のPythonプロセスが残ってシリアルポートをロックする問題を根絶。
  - `tsnd_bridge.py`: 前回導入した一括BT切断はRFCOMMチャネルを破壊し再構築されないため、逆に接続を壊していた。一括リセットを廃止し、センサーの初期化に失敗した場合のみ個別に `_reset_single_sensor` を呼び出して切断→再接続を行うように修正。既存の正常なポートはそのまま利用する。
