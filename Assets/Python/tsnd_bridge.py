#!/usr/bin/env python3
"""
TSND151 UDP Bridge - eno-lab/tsnd公式ライブラリの仕様に準拠した実装
センサとの通信はpyserialが行い、データをUDPでUnityへ転送する
"""
import argparse
import os
import socket
import serial
import struct
import subprocess
import threading
import time
import sys

UDP_IP = "127.0.0.1"
UDP_PORT = 5000

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
is_running = True


# ============================================================
# macOS Bluetooth 自動接続 (blueutil使用)
# ============================================================

def get_bluetooth_address(device_name):
    """system_profilerからデバイス名に対応するMACアドレスを取得する"""
    try:
        output = subprocess.check_output(
            ['/usr/sbin/system_profiler', 'SPBluetoothDataType'],
            timeout=10
        ).decode('utf-8', errors='replace')
        
        lines = output.split('\n')
        found = False
        for line in lines:
            stripped = line.strip()
            if device_name in stripped:
                found = True
                continue
            if found and 'Address:' in stripped:
                addr = stripped.split('Address:')[1].strip()
                return addr
    except Exception as e:
        print(f"  [BT] system_profiler error: {e}", file=sys.stderr)
    return None


def ensure_bluetooth_connected(port_path):
    """
    macOSでBluetooth SPPデバイスの/devファイルが未出現の場合、
    bleutilを使ってBluetooth接続を確立し/devファイルを生成する
    """
    if os.path.exists(port_path):
        print(f"  [BT] Port {port_path} already exists.", file=sys.stderr)
        return True
    
    # ポート名からデバイス名を抽出: /dev/tty.TSND151-AP09182352 → TSND151-AP09182352
    device_name = os.path.basename(port_path).replace("tty.", "")
    print(f"  [BT] Port {port_path} not found. Attempting Bluetooth connect for {device_name}...", file=sys.stderr)
    
    # MACアドレスを取得
    mac_address = get_bluetooth_address(device_name)
    if not mac_address:
        print(f"  [BT] Could not find MAC address for {device_name}.", file=sys.stderr)
        return False
    
    print(f"  [BT] Found MAC {mac_address} for {device_name}. Connecting via blueutil...", file=sys.stderr)
    
    # bleutilで接続
    try:
        result = subprocess.run(
            ['blueutil', '--connect', mac_address],
            timeout=15,
            capture_output=True,
            text=True
        )
        if result.returncode != 0:
            print(f"  [BT] blueutil connect failed: {result.stderr}", file=sys.stderr)
    except FileNotFoundError:
        print("  [BT] blueutil not found. Install with: brew install blueutil", file=sys.stderr)
        return False
    except Exception as e:
        print(f"  [BT] blueutil error: {e}", file=sys.stderr)
        return False
    
    # /devファイルが生成されるのを待つ（最大15秒）
    print(f"  [BT] Waiting for {port_path} to appear...", file=sys.stderr)
    for i in range(30):
        time.sleep(0.5)
        if os.path.exists(port_path):
            print(f"  [BT] Port {port_path} appeared after {(i+1)*0.5:.1f}s!", file=sys.stderr)
            return True
    
    print(f"  [BT] Timed out waiting for {port_path}.", file=sys.stderr)
    return False

# 公式仕様に準拠したレスポンスパラメータ長マップ
RESPONSE_ARG_LEN = {
    0x8F: 1,   # simple ACK
    0x80: 22,  # acc_gyro_data
    0x81: 13,  # magnetism_data
    0x82: 9,   # atmosphere_data
    0x83: 7,   # battery_voltage_data
    0x84: 9,
    0x85: 6,
    0x86: 13,
    0x87: 5,
    0x88: 1,   # start_recording
    0x89: 1,   # stop_recording
    0x8A: 30,  # quaternion_acc_gyro_data
    0x8B: 22,
    0x8C: 12,
    0x90: 30,
    0x92: 8,   # time
    0x93: 13,  # recording_time_settings
    0x97: 3,
    0x99: 3,
    0x9B: 3,
    0x9D: 2,
    0x9F: 5,
    0xA1: 3,
    0xA3: 1,
    0xA6: 1,
    0xAA: 12,
    0xAB: 9,
    0xAD: 1,
    0xAF: 1,
    0xB1: 4,
    0xB3: 1,
    0xB6: 1,
    0xB7: 24,
    0xB8: 60,
    0xB9: 1,
    0xBA: 5,
    0xBB: 3,
    0xBC: 1,
    0xBD: 12,
    0xBE: 12,
    0xD1: 1,
    0xD3: 1,
    0xD6: 3,
    0xD8: 78,
    0xDA: 7,
    0xDC: 28,
    0xDD: 1,
}


def build_cmd(cmd_code, args):
    """公式仕様に完全準拠したコマンド構築"""
    total_cmd = [0x9A, cmd_code]
    if isinstance(args, (list, tuple)):
        total_cmd.extend(args)
    else:
        total_cmd.append(args)
    
    bcc = 0x00
    for c in total_cmd:
        bcc ^= c
    total_cmd.append(bcc)
    return bytes(total_cmd)


def send_cmd(ser, cmd_code, args, label=""):
    """コマンドを送信し、ACK応答を待つ"""
    packet = build_cmd(cmd_code, args)
    ser.write(packet)
    ser.flush()
    print(f"  [CMD] Sent 0x{cmd_code:02X} ({label}): {packet.hex()}", file=sys.stderr)
    

def wait_for_ack(ser, timeout=2.0):
    """ACK(0x8F)応答を待つ。返却値: (成功したか, 応答バイト列)"""
    start = time.time()
    buf = bytearray()
    while time.time() - start < timeout:
        if ser.in_waiting > 0:
            buf.extend(ser.read(ser.in_waiting))
        # 0x9Aヘッダを探す
        while len(buf) >= 2:
            if buf[0] == 0x9A:
                cmd = buf[1]
                if cmd in RESPONSE_ARG_LEN:
                    expected_len = 2 + RESPONSE_ARG_LEN[cmd] + 1  # header+cmd+params+bcc
                    if len(buf) >= expected_len:
                        pkt = buf[:expected_len]
                        buf = buf[expected_len:]
                        # BCC検証
                        bcc = 0
                        for b in pkt[:-1]:
                            bcc ^= b
                        if bcc == pkt[-1]:
                            if cmd == 0x8F:
                                result = pkt[2]  # 0x00=OK, 0x01=NG
                                print(f"  [ACK] 0x8F result=0x{result:02X} {'OK' if result == 0 else 'NG'}", file=sys.stderr)
                                return result == 0x00, pkt
                            else:
                                # ACK以外のレスポンス（0x93等）は消費して続行
                                print(f"  [RSP] 0x{cmd:02X} received (non-ACK)", file=sys.stderr)
                                continue
                        else:
                            buf = buf[1:]  # BCC不一致、1バイト進む
                    else:
                        break  # データ不足、待つ
                else:
                    buf = buf[1:]  # 未知のコマンド、1バイト進む
            else:
                buf = buf[1:]  # ヘッダでない、1バイト進む
        time.sleep(0.01)
    print(f"  [ACK] Timeout waiting for ACK", file=sys.stderr)
    return False, None


def init_sensor(ser):
    """公式ライブラリの手順に準拠したセンサ初期化"""
    # 0. まず以前の計測が動いたままになっている場合を考慮してストップコマンドを送る
    send_cmd(ser, 0x15, [0x00], "force_stop")
    time.sleep(0.5)
    
    # バッファをクリア
    ser.reset_input_buffer()
    time.sleep(0.1)
    
    # 1. 時刻設定 (0x11)
    send_cmd(ser, 0x11, [26, 7, 15, 12, 0, 0, 0, 0], "set_time")
    ok, _ = wait_for_ack(ser)
    if not ok:
        print("  [WARN] set_time ACK failed", file=sys.stderr)
    time.sleep(0.1)
    
    # 2. 加速度・角速度設定 (0x16) [OFFにする]
    # 公式仕様: クォータニオン(0x55)を利用する場合は0x16と競合するため0を指定してOFFにする
    send_cmd(ser, 0x16, [0, 0, 0], "set_acc_gyro_interval_off")
    ok, _ = wait_for_ack(ser)
    if not ok:
        print("  [WARN] set_acc_gyro_interval ACK failed", file=sys.stderr)
    time.sleep(0.1)
    
    # 3. クォータニオン設定 (0x55) [周期10ms, 送信1回, 記録0回]
    send_cmd(ser, 0x55, [10, 1, 0], "set_quaternion_interval")
    ok, _ = wait_for_ack(ser)
    if not ok:
        print("  [WARN] set_quaternion_interval ACK failed", file=sys.stderr)
    time.sleep(0.1)
    
    # 4. 計測開始 (0x13) - 公式仕様: 即時開始 + 永久実行
    start_flag = [0, 0, 1, 1, 0, 0, 0,   # 即時開始 (秒=0で即時)
                  0, 0, 1, 1, 0, 0, 0]    # 永久実行
    send_cmd(ser, 0x13, start_flag, "start_recording")
    # 0x13は0x93(recording_time_settings)と0x88(start_recording)を返す
    # これらを消費する
    time.sleep(0.5)
    if ser.in_waiting > 0:
        resp = ser.read(ser.in_waiting)
        print(f"  [RSP] start response: {resp.hex()}", file=sys.stderr)


def stop_sensor(ser):
    """計測停止"""
    send_cmd(ser, 0x15, [0x00], "stop")
    time.sleep(0.3)
    if ser.in_waiting > 0:
        ser.read(ser.in_waiting)


def read_loop(ser, sensor_id):
    """公式仕様に準拠したパケット受信ループ"""
    buf = bytearray()
    data_count = 0
    
    while is_running:
        try:
            if ser.in_waiting > 0:
                new_data = ser.read(ser.in_waiting)
                buf.extend(new_data)
            
            # パケット解析（公式と同じ方式：ヘッダを探し→コマンドコードで長さを確定→BCC検証）
            while len(buf) >= 2:
                # 0x9Aヘッダを探す
                if buf[0] != 0x9A:
                    buf = buf[1:]
                    continue
                
                cmd = buf[1]
                if cmd not in RESPONSE_ARG_LEN:
                    buf = buf[1:]
                    continue
                
                expected_len = 2 + RESPONSE_ARG_LEN[cmd] + 1  # header+cmd+params+bcc
                if len(buf) < expected_len:
                    break  # データ不足、次の受信を待つ
                
                pkt = buf[:expected_len]
                
                # BCC検証
                bcc = 0
                for b in pkt[:-1]:
                    bcc ^= b
                if bcc != pkt[-1]:
                    buf = buf[1:]  # BCC不一致、1バイト進んでリトライ
                    continue
                
                # 有効なパケット
                buf = buf[expected_len:]
                
                # 0x8A: クォータニオン+加速度+ジャイロ データ → UDPでUnityに転送
                if cmd == 0x8A:
                    params = pkt[2:-1]  # パラメータ部分（30バイト）
                    # sensor_id(1バイト) + パラメータ全体(30バイト) をUDP送信
                    udp_payload = bytes([sensor_id]) + params
                    sock.sendto(udp_payload, (UDP_IP, UDP_PORT))
                    data_count += 1
                    if data_count % 100 == 1:
                        print(f"[Bridge] Sensor {sensor_id}: {data_count} packets sent", file=sys.stderr)
            
            if ser.in_waiting == 0:
                time.sleep(0.005)
                
        except Exception as e:
            print(f"[Bridge] Error on sensor {sensor_id}: {e}", file=sys.stderr)
            break


def main():
    global is_running
    parser = argparse.ArgumentParser()
    parser.add_argument('--sensors', nargs='+', 
                        help='List of id:port pairs, e.g. "1:/dev/tty.TSND151-xxx"')
    # 後方互換: 古い --ports 引数もサポート
    parser.add_argument('--ports', nargs='+', help='(Legacy) List of serial ports')
    args = parser.parse_args()

    sensor_specs = []
    if args.sensors:
        for spec in args.sensors:
            parts = spec.split(":", 1)
            if len(parts) == 2:
                sensor_specs.append((int(parts[0]), parts[1]))
    elif args.ports:
        for i, port in enumerate(args.ports):
            sensor_specs.append((i + 1, port))
    
    if not sensor_specs:
        print("[Bridge] No sensors specified.", file=sys.stderr)
        sys.exit(1)

    threads = []
    serials = []

    for sensor_id, port in sensor_specs:
        print(f"[Bridge] Connecting Sensor {sensor_id} on {port}...", file=sys.stderr)
        try:
            # macOS: /devファイルが存在しない場合はBluetoothを自動接続
            if not ensure_bluetooth_connected(port):
                print(f"[Bridge] SKIPPED Sensor {sensor_id}: Bluetooth connection failed.", file=sys.stderr)
                continue
            
            # 公式仕様に準拠: timeout=0.01(10ms)、DTR/RTS設定なし
            ser = serial.Serial(port, 115200, timeout=0.01)
            time.sleep(2)  # 公式と同じ: 接続安定のため2秒待機
            serials.append((sensor_id, ser))
            
            print(f"[Bridge] Initializing Sensor {sensor_id}...", file=sys.stderr)
            init_sensor(ser)
            print(f"[Bridge] Sensor {sensor_id} initialized successfully.", file=sys.stderr)
            
            t = threading.Thread(target=read_loop, args=(ser, sensor_id), daemon=True)
            t.start()
            threads.append(t)
        except Exception as e:
            print(f"[Bridge] FAILED to connect {port}: {e}", file=sys.stderr)

    if not serials:
        print("[Bridge] No sensors connected. Exiting.", file=sys.stderr)
        sys.exit(1)

    print(f"[Bridge] {len(serials)} sensor(s) running. Waiting for stdin EOF...", file=sys.stderr)
    sys.stderr.flush()
    
    try:
        while True:
            line = sys.stdin.readline()
            if not line:
                break
    except KeyboardInterrupt:
        pass

    print("[Bridge] Shutting down...", file=sys.stderr)
    is_running = False
    for sensor_id, ser in serials:
        try:
            stop_sensor(ser)
            ser.close()
            print(f"[Bridge] Sensor {sensor_id} closed.", file=sys.stderr)
        except:
            pass
    print("[Bridge] Exited cleanly.", file=sys.stderr)


if __name__ == "__main__":
    main()
