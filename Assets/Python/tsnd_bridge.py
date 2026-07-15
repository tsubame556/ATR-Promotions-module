import argparse
import socket
import serial
import threading
import time
import sys

UDP_IP = "127.0.0.1"
UDP_PORT = 5000

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
is_running = True

def add_bcc(cmd):
    bcc = 0
    for b in cmd:
        bcc ^= b
    cmd.append(bcc)
    return cmd

def send_cmd(ser, cmd_code, params):
    cmd = [0x9A, cmd_code] + params
    cmd = add_bcc(cmd)
    ser.write(bytes(cmd))
    # 応答ACKを受け取ってバッファをクリア
    time.sleep(0.1)
    if ser.in_waiting > 0:
        ser.read(ser.in_waiting)

def init_sensor(ser):
    # Time
    send_cmd(ser, 0x11, [26, 7, 15, 12, 0, 0, 0, 0])
    time.sleep(0.2)
    # AGS
    send_cmd(ser, 0x16, [10, 1, 0])
    time.sleep(0.2)
    # Quat
    send_cmd(ser, 0x55, [10, 1, 0])
    time.sleep(0.2)
    # Start (3秒後)
    send_cmd(ser, 0x13, [0, 0, 1, 1, 0, 0, 3, 0, 0, 1, 1, 0, 0, 0])
    time.sleep(0.2)

def stop_sensor(ser):
    send_cmd(ser, 0x15, [])

def read_loop(ser, sensor_id):
    buffer = bytearray()
    while is_running:
        try:
            if ser.in_waiting > 0:
                data = ser.read(ser.in_waiting)
                buffer.extend(data)
                
                # パケット解析
                parse_idx = 0
                while len(buffer) - parse_idx >= 3:
                    if buffer[parse_idx] == 0x9A:
                        valid_len = -1
                        max_search = min(100, len(buffer) - parse_idx)
                        for length in range(3, max_search + 1):
                            bcc = 0
                            for i in range(length - 1):
                                bcc ^= buffer[parse_idx + i]
                            if bcc == buffer[parse_idx + length - 1]:
                                valid_len = length
                                break
                        
                        if valid_len != -1:
                            packet = buffer[parse_idx : parse_idx + valid_len]
                            cmd = packet[1]
                            
                            # データパケット(0x8A)の場合のみUnityへ転送
                            if cmd == 0x8A:
                                # 先頭にsensor_id(1バイト)を付与して送信
                                udp_payload = bytes([sensor_id]) + packet
                                sock.sendto(udp_payload, (UDP_IP, UDP_PORT))
                            
                            parse_idx += valid_len
                        else:
                            if len(buffer) - parse_idx > 100:
                                parse_idx += 1
                            else:
                                break # データ不足
                    else:
                        parse_idx += 1
                
                if parse_idx > 0:
                    buffer = buffer[parse_idx:]
            else:
                time.sleep(0.01)
        except Exception as e:
            print(f"[Bridge] Error on sensor {sensor_id}: {e}", file=sys.stderr)
            break

def main():
    global is_running
    parser = argparse.ArgumentParser()
    parser.add_argument('--ports', nargs='+', help='List of serial ports')
    args = parser.parse_args()

    if not args.ports:
        print("[Bridge] No ports specified.", file=sys.stderr)
        sys.exit(1)

    threads = []
    serials = []

    for i, port in enumerate(args.ports):
        sensor_id = i + 1
        print(f"[Bridge] Connecting to Sensor {sensor_id} on {port}...")
        try:
            ser = serial.Serial(port, 115200, timeout=0.1)
            serials.append(ser)
            init_sensor(ser)
            print(f"[Bridge] Sensor {sensor_id} initialized.")
            
            t = threading.Thread(target=read_loop, args=(ser, sensor_id))
            t.daemon = True
            t.start()
            threads.append(t)
        except Exception as e:
            print(f"[Bridge] Failed to connect {port}: {e}", file=sys.stderr)

    print("[Bridge] All sensors running. Waiting for stdin EOF to exit...")
    sys.stdout.flush()
    
    try:
        # Unity側でProcessが終了（または標準入力が閉じられた）場合、速やかに終了する
        while True:
            line = sys.stdin.readline()
            if not line:
                break
    except KeyboardInterrupt:
        pass

    print("[Bridge] Exiting... Stopping sensors.")
    is_running = False
    for ser in serials:
        try:
            stop_sensor(ser)
            time.sleep(0.2)
            ser.close()
        except:
            pass
    print("[Bridge] Exited cleanly.")

if __name__ == "__main__":
    main()
