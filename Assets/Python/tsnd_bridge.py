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
        while len(buf) >= 2:
            if buf[0] == 0x9A:
                cmd = buf[1]
                if cmd in RESPONSE_ARG_LEN:
                    expected_len = 2 + RESPONSE_ARG_LEN[cmd] + 1
                    if len(buf) >= expected_len:
                        pkt = buf[:expected_len]
                        buf = buf[expected_len:]
                        bcc = 0
                        for b in pkt[:-1]:
                            bcc ^= b
                        if bcc == pkt[-1]:
                            if cmd == 0x8F:
                                result = pkt[2]
                                print(f"  [ACK] 0x8F result=0x{result:02X} {'OK' if result == 0 else 'NG'}", file=sys.stderr)
                                return result == 0x00, pkt
                            else:
                                print(f"  [RSP] 0x{cmd:02X} received (non-ACK)", file=sys.stderr)
                                continue
                        else:
                            buf = buf[1:]
                    else:
                        break
                else:
                    buf = buf[1:]
            else:
                buf = buf[1:]
        time.sleep(0.01)
    print(f"  [ACK] Timeout waiting for ACK", file=sys.stderr)
    return False, None

def init_sensor(ser):
    """公式ライブラリの手順に準拠したセンサ初期化。"""
    # 以前のゾンビ状態のパケットを破棄する
    try:
        ser.reset_input_buffer()
        ser.reset_output_buffer()
    except Exception:
        pass
    time.sleep(0.5)

    print("  [CMD] Waking up sensor with force_stop...", file=sys.stderr)
    awake = False
    for _ in range(3):
        send_cmd(ser, 0x15, [0x00], "force_stop")
        ok, _ = wait_for_ack(ser, timeout=1.5)
        if ok:
            awake = True
            break
        time.sleep(0.5)
        
    if not awake:
        print("  [WARN] Sensor did not respond to initial wake up (force_stop).", file=sys.stderr)
        return False
        
    ser.reset_input_buffer()
    time.sleep(0.1)
    
    send_cmd(ser, 0x11, [26, 7, 15, 12, 0, 0, 0, 0], "set_time")
    ok, _ = wait_for_ack(ser)
    if not ok:
        print("  [WARN] set_time ACK failed", file=sys.stderr)
        return False
    time.sleep(0.1)
    
    send_cmd(ser, 0x16, [0, 0, 0], "set_acc_gyro_interval_off")
    ok, _ = wait_for_ack(ser)
    time.sleep(0.1)
    
    send_cmd(ser, 0x55, [10, 1, 0], "set_quaternion_interval")
    ok, _ = wait_for_ack(ser)
    time.sleep(0.1)
    
    return True

def start_measurement(ser):
    start_flag = [0, 0, 1, 1, 0, 0, 0, 0, 0, 1, 1, 0, 0, 0]
    send_cmd(ser, 0x13, start_flag, "start_recording")
    time.sleep(0.1)

def stop_sensor(ser):
    send_cmd(ser, 0x15, [0x00], "stop")
    time.sleep(0.3)
    if ser.in_waiting > 0:
        ser.read(ser.in_waiting)

def read_loop(ser, sensor_id):
    buf = bytearray()
    data_count = 0
    while is_running:
        try:
            if ser.in_waiting > 0:
                buf.extend(ser.read(ser.in_waiting))
            
            while len(buf) >= 2:
                if buf[0] != 0x9A:
                    buf = buf[1:]
                    continue
                cmd = buf[1]
                if cmd not in RESPONSE_ARG_LEN:
                    buf = buf[1:]
                    continue
                
                expected_len = 2 + RESPONSE_ARG_LEN[cmd] + 1
                if len(buf) < expected_len:
                    break
                
                pkt = buf[:expected_len]
                bcc = 0
                for b in pkt[:-1]:
                    bcc ^= b
                if bcc != pkt[-1]:
                    buf = buf[1:]
                    continue
                
                buf = buf[expected_len:]
                
                if cmd == 0x8A:
                    params = pkt[2:-1]
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

def force_mac_connection(port_path):
    import subprocess
    dev_name = port_path.split('.')[-1]
    try:
        out = subprocess.check_output(["/usr/sbin/system_profiler", "SPBluetoothDataType"], text=True)
        lines = out.split('\n')
        mac = None
        for i, line in enumerate(lines):
            if dev_name in line:
                for j in range(1, 6):
                    if i+j < len(lines) and "Address:" in lines[i+j]:
                        mac = lines[i+j].split("Address:")[1].strip()
                        break
                break
        if mac:
            print(f"[Bridge] Auto-connecting {dev_name} ({mac}) via blueutil...", file=sys.stderr)
            blueutil_path = "/opt/homebrew/bin/blueutil"
            if not os.path.exists(blueutil_path):
                blueutil_path = "blueutil"
            
            # Increase timeout to 30s because Mac Bluetooth can be very slow when connecting multiple devices sequentially
            # Killing blueutil before it finishes corrupts the blued daemon state!
            try:
                res = subprocess.run([blueutil_path, "--connect", mac], timeout=30, capture_output=True, text=True)
                if res.returncode != 0:
                    print(f"[Bridge] blueutil connect failed for {dev_name}: {res.stderr.strip()}", file=sys.stderr)
            except subprocess.TimeoutExpired:
                print(f"[Bridge] blueutil connect timeout for {dev_name}", file=sys.stderr)
                
            # Wait up to 5 seconds for the port to be created by macOS
            for _ in range(25):
                if os.path.exists(port_path):
                    return True
                time.sleep(0.2)
                
            print(f"[Bridge] Port {port_path} still missing after blueutil.", file=sys.stderr)
            return False
    except Exception as e:
        print(f"[Bridge] force_mac_connection error for {dev_name}: {e}", file=sys.stderr)
    return False

def _reset_bluetooth_connections(sensor_specs):
    """全センサーのBluetooth接続を切断→再接続してRFCOMMチャネルをリセットする"""
    import subprocess
    blueutil_path = "/opt/homebrew/bin/blueutil"
    if not os.path.exists(blueutil_path):
        blueutil_path = "blueutil"
    
    # system_profiler からMAC辞書を構築
    mac_map = {}
    try:
        out = subprocess.check_output(["/usr/sbin/system_profiler", "SPBluetoothDataType"], text=True)
        lines = out.split('\n')
        for i, line in enumerate(lines):
            for sensor_id, port in sensor_specs:
                dev_name = port.split('.')[-1]
                if dev_name in line:
                    for j in range(1, 6):
                        if i+j < len(lines) and "Address:" in lines[i+j]:
                            mac = lines[i+j].split("Address:")[1].strip()
                            mac_map[sensor_id] = mac
                            break
    except Exception as e:
        print(f"[Bridge] Failed to build MAC map: {e}", file=sys.stderr)
        return
    
    if not mac_map:
        print("[Bridge] No MAC addresses found, skipping BT reset.", file=sys.stderr)
        return
    
    # 全センサーを切断
    print(f"[Bridge] Resetting Bluetooth connections for {len(mac_map)} sensors...", file=sys.stderr)
    for sensor_id, mac in mac_map.items():
        try:
            subprocess.run([blueutil_path, "--disconnect", mac], timeout=5, capture_output=True)
            print(f"[Bridge] Disconnected Sensor {sensor_id} ({mac})", file=sys.stderr)
        except Exception:
            pass
    
    time.sleep(2)
    
    # 順番に再接続（1台ずつ待機して接続する）
    for sensor_id, mac in mac_map.items():
        if not is_running:
            break
        try:
            print(f"[Bridge] Reconnecting Sensor {sensor_id} ({mac})...", file=sys.stderr)
            res = subprocess.run([blueutil_path, "--connect", mac], timeout=30, capture_output=True, text=True)
            if res.returncode == 0:
                print(f"[Bridge] Sensor {sensor_id} reconnected successfully.", file=sys.stderr)
            else:
                print(f"[Bridge] Sensor {sensor_id} reconnect failed: {res.stderr.strip()}", file=sys.stderr)
        except subprocess.TimeoutExpired:
            print(f"[Bridge] Sensor {sensor_id} reconnect timeout.", file=sys.stderr)
        time.sleep(1)
    
    # ポートが生成されるまで少し待つ
    time.sleep(2)
    print("[Bridge] Bluetooth reset complete.", file=sys.stderr)

def connection_manager(sensor_specs, serials, threads):
    unconnected = list(sensor_specs)
    
    # 初回のみ: 全センサーのBluetooth接続をリセットし、ゴーストRFCOMMチャネルをクリア
    _reset_bluetooth_connections(sensor_specs)
    
    while is_running and unconnected:
        # Pre-pass: Force connect ALL missing ports first BEFORE opening any serial ports
        # This prevents the active SPP data stream of an early sensor from blocking the Mac's Bluetooth daemon
        # from negotiating connections for subsequent sensors.
        for sensor_id, port in unconnected:
            if not is_running:
                break
            if not os.path.exists(port):
                print(f"[Bridge] Pre-connecting Sensor {sensor_id} on {port}...", file=sys.stderr)
                force_mac_connection(port)
                
        still_unconnected = []
        for sensor_id, port in unconnected:
            if not is_running:
                break
                
            print(f"[Bridge] Opening Sensor {sensor_id} on {port}...", file=sys.stderr)
            
            if not os.path.exists(port):
                print(f"[Bridge] SKIPPED Sensor {sensor_id}: Port {port} does not exist.", file=sys.stderr)
                still_unconnected.append((sensor_id, port))
                continue
            
            try:
                # タイムアウトを少し長めにしてRFCOMM確立を確実にする
                # rtscts/dsrdtr を無効化しハードウェアフロー制御によるブロッキングを防止
                ser = serial.Serial(port, 115200, timeout=1.5, write_timeout=1.0,
                                    rtscts=False, dsrdtr=False)
                time.sleep(1.5)
            except Exception as e:
                print(f"[Bridge] Error opening port for Sensor {sensor_id}: {e}", file=sys.stderr)
                still_unconnected.append((sensor_id, port))
                continue
                
            print(f"[Bridge] Initializing Sensor {sensor_id}...", file=sys.stderr)
            init_success = False
            for attempt in range(3):
                if not is_running:
                    break
                if init_sensor(ser):
                    init_success = True
                    break
                print(f"[Bridge] Sensor {sensor_id} init attempt {attempt+1} failed. Retrying...", file=sys.stderr)
                time.sleep(2)
                
            if init_success:
                print(f"[Bridge] Sensor {sensor_id} initialized successfully.", file=sys.stderr)
                serials.append((sensor_id, ser))
                t = threading.Thread(target=read_loop, args=(ser, sensor_id), daemon=True)
                t.start()
                threads.append(t)
            else:
                print(f"[Bridge] Sensor {sensor_id} init permanently failed. Will retry next loop.", file=sys.stderr)
                ser.close()
                still_unconnected.append((sensor_id, port))
                
        unconnected = still_unconnected
        if unconnected and is_running:
            print("[Bridge] Retrying unconnected sensors in 5 seconds...", file=sys.stderr)
            for _ in range(50):
                if not is_running:
                    break
                time.sleep(0.1)

    print("[Bridge] Connection manager finished. All target sensors are connected.", file=sys.stderr)

def main():
    global is_running
    parser = argparse.ArgumentParser()
    parser.add_argument('--sensors', nargs='+', help='List of id:port pairs')
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

    cm_thread = threading.Thread(target=connection_manager, args=(sensor_specs, serials, threads), daemon=True)
    cm_thread.start()

    print(f"[Bridge] Connection manager started in background. Waiting for stdin EOF...", file=sys.stderr)
    sys.stderr.flush()
    
    try:
        while True:
            line = sys.stdin.readline()
            if not line:
                break
            line = line.strip()
            if line == "START":
                print("[Bridge] Received START command. Starting measurement on all sensors...", file=sys.stderr)
                for sid, s in serials:
                    start_measurement(s)
            elif line == "STOP":
                print("[Bridge] Received STOP command. Stopping measurement on all sensors...", file=sys.stderr)
                for sid, s in serials:
                    stop_sensor(s)
    except KeyboardInterrupt:
        pass

    print("[Bridge] Shutting down...", file=sys.stderr)
    is_running = False
    
    def _close_ser(sid, s):
        try:
            stop_sensor(s)
            s.close()
            print(f"[Bridge] Sensor {sid} closed.", file=sys.stderr)
        except:
            pass

    shutdown_threads = []
    for sensor_id, ser in serials:
        st = threading.Thread(target=_close_ser, args=(sensor_id, ser))
        st.start()
        shutdown_threads.append(st)
        
    for st in shutdown_threads:
        st.join(timeout=3.0)

    print("[Bridge] Exited cleanly.", file=sys.stderr)

if __name__ == "__main__":
    main()
