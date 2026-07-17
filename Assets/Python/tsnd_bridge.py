#!/usr/bin/env python3
"""
TSND151 UDP Bridge - 公式仕様書準拠版
センサとの通信はpyserialが行い、データをUDPでUnityへ転送する
"""
import argparse
import os
import socket
import serial
import struct
import threading
import time
import sys
import select

UDP_IP = "127.0.0.1"
UDP_PORT = 5000

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
is_running = True

# 公式仕様に準拠したレスポンスパラメータ長マップ
RESPONSE_ARG_LEN = {
    0x8F: 1,   # Command Response
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
    """コマンドを送信する"""
    packet = build_cmd(cmd_code, args)
    ser.write(packet)
    ser.flush()
    print(f"  [CMD] Sent 0x{cmd_code:02X} ({label}): {packet.hex()}", file=sys.stderr)

def wait_for_ack(ser, expected_cmd=0x8F, timeout=2.0):
    """特定の応答コマンドを待つ"""
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
                            if cmd == expected_cmd:
                                print(f"  [ACK] 0x{cmd:02X} OK", file=sys.stderr)
                                return True, pkt
                            else:
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
    print(f"  [ACK] Timeout waiting for 0x{expected_cmd:02X}", file=sys.stderr)
    return False, None

def init_sensor(ser):
    print(f"[Bridge] Initializing Sensor...", file=sys.stderr)
    # バッファクリア (公式サンプルの read(1000) 相当)
    ser.reset_input_buffer()
    ser.reset_output_buffer()
    time.sleep(0.1)

    # まず計測停止(0x15 0x00)を送る (計測中かもしれないので)
    send_cmd(ser, 0x15, [0x00], "stop_measure")
    # ここはタイムアウトしても良い (すでに停止している場合があるため)
    wait_for_ack(ser, timeout=1.0)
    
    # 1. 加速度/角速度計測設定 (0x16) -> OFF
    # p1=0(OFF), p2=0(送信なし), p3=0(記録なし)
    send_cmd(ser, 0x16, [0x00, 0x00, 0x00], "set_acc_gyro_off")
    ok, _ = wait_for_ack(ser)
    if not ok: return False
    
    # 2. クオータニオン計測設定 (0x55) -> ON (10ms=0x0A周期)
    # p1=10(10ms), p2=1(送信する), p3=0(記録なし)
    send_cmd(ser, 0x55, [0x0A, 0x01, 0x00], "set_quaternion_on")
    ok, _ = wait_for_ack(ser)
    if not ok: return False

    # 初期化完了後、即座に計測を開始してデータをストリーミングさせる
    start_measurement(ser)

    return True

def start_measurement(ser):
    # 計測開始 (0x13)
    # 14 bytes: [smode, Y, M, D, h, m, s, emode, Y, M, D, h, m, s]
    # 相対時間指定(0x00), 月日は 1 以上にする必要がある (0x01)
    start_flag = [0x00, 0, 1, 1, 0, 0, 0, 0x00, 0, 1, 1, 0, 0, 0]
    send_cmd(ser, 0x13, start_flag, "start_recording")

def stop_sensor(ser):
    # 計測停止 (0x15) -> パラメータは 0x00 固定
    send_cmd(ser, 0x15, [0x00], "stop")
    time.sleep(0.3)
    ser.reset_input_buffer()

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
                elif cmd == 0xBB or cmd == 0x94:
                    params = pkt[2:-1]
                    udp_payload = bytes([sensor_id, cmd]) + params
                    sock.sendto(udp_payload, (UDP_IP, UDP_PORT))
            
            if ser.in_waiting == 0:
                time.sleep(0.005)
        except Exception as e:
            print(f"[Bridge] Error on sensor {sensor_id}: {e}", file=sys.stderr)
            break

def connection_manager(sensor_specs, serials, threads):
    unconnected = list(sensor_specs)
    
    while is_running and unconnected:
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
                # pyserial が OS レベルで SPP 接続を確立する
                ser = serial.Serial(port, 115200, timeout=1.5, write_timeout=1.0)
                time.sleep(1.0) # ポートオープン後の安定化待ち
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
                print(f"[Bridge] Sensor {sensor_id} init attempt {attempt+1} failed.", file=sys.stderr)
                time.sleep(1)
                
            if init_success:
                print(f"[Bridge] Sensor {sensor_id} initialized successfully.", file=sys.stderr)
                serials.append((sensor_id, ser))
                t = threading.Thread(target=read_loop, args=(ser, sensor_id), daemon=True)
                t.start()
                threads.append(t)
            else:
                print(f"[Bridge] Sensor {sensor_id} init permanently failed.", file=sys.stderr)
                try:
                    ser.close()
                except:
                    pass
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
    args = parser.parse_args()

    sensor_specs = []
    if args.sensors:
        for spec in args.sensors:
            parts = spec.split(":", 1)
            if len(parts) == 2:
                sensor_specs.append((int(parts[0]), parts[1]))
    
    if not sensor_specs:
        print("[Bridge] No sensors specified.", file=sys.stderr)
        sys.exit(1)
        
    threads = []
    serials = []

    cm_thread = threading.Thread(target=connection_manager, args=(sensor_specs, serials, threads), daemon=True)
    cm_thread.start()

    # コマンド受信用ループ
    try:
        while True:
            rlist, _, _ = select.select([sys.stdin], [], [], 1.0)
            if rlist:
                line = sys.stdin.readline()
                if not line:
                    break
                
                cmd = line.strip().upper()
                if cmd == "START":
                    print("[Bridge] Received START command. Broadcasting start_measurement...", file=sys.stderr)
                    for sid, s in list(serials):
                        start_measurement(s)
                elif cmd == "STOP":
                    print("[Bridge] Received STOP command. Broadcasting stop_sensor...", file=sys.stderr)
                    for sid, s in list(serials):
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
