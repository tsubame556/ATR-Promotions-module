#!/usr/bin/env python3
"""
macOS用 TSND151 Bluetoothペアリングツール
Mac特有の「ペアリング成功しても /dev/cu.* が生成されない」OSバグを自動検知し、
確実なペアリングをサポートします。
"""

import subprocess
import time
import sys
import os

def check_blueutil():
    paths = ["/opt/homebrew/bin/blueutil", "/usr/local/bin/blueutil", "blueutil"]
    for p in paths:
        try:
            subprocess.run([p, "--version"], capture_output=True)
            return p
        except Exception:
            pass
    return None

def find_tsnd_devices(blueutil):
    print("周辺のTSND151センサーを検索しています... (約10秒かかります)")
    try:
        res = subprocess.run([blueutil, "--inquiry"], capture_output=True, text=True, timeout=15)
    except subprocess.TimeoutExpired:
        print("検索がタイムアウトしました。")
        return []

    devices = []
    for line in res.stdout.split('\n'):
        if "TSND151" in line:
            # Format usually: address: 00:07:80:47:ea:dc, not connected, not favourite, not paired, name: "TSND151-AP09182352"
            parts = line.split(',')
            mac = parts[0].split(' ')[1].strip()
            name = "TSND151-Unknown"
            for p in parts:
                if "name:" in p:
                    name = p.split('name:')[1].strip().strip('"')
            devices.append((mac, name))
    return devices

def check_port_created(name, timeout=10):
    print(f"[{name}] /dev/cu.* ポートの生成を待機しています...")
    start = time.time()
    while time.time() - start < timeout:
        for f in os.listdir('/dev'):
            if "cu.TSND" in f and name in f:
                print(f"[{name}] ポートが生成されました: /dev/{f}")
                return True
        time.sleep(0.5)
    return False

def pair_device(blueutil, mac, name):
    print(f"\n--- {name} ({mac}) のペアリングを開始します ---")
    
    # Check if already paired
    res = subprocess.run([blueutil, "--is-paired", mac], capture_output=True, text=True)
    if res.stdout.strip() == "1":
        print(f"[{name}] 既にペアリングされています。ポートを確認します...")
        if check_port_created(name, timeout=3):
            print(f"[{name}] 問題ありません。正常に使用可能です。")
            return
        else:
            print(f"[{name}] ⚠️ ペアリング済みですがポートが存在しません。OSのバグ(ゴースト化)の可能性があります。")
            print(f"[{name}] 一度ペアリングを強制解除します...")
            subprocess.run([blueutil, "--unpair", mac])
            time.sleep(2)
            
    print(f"[{name}] ペアリング要求を送信しています...")
    try:
        res = subprocess.run([blueutil, "--pair", mac], timeout=15, capture_output=True, text=True)
        if res.returncode != 0:
            print(f"[{name}] ペアリングに失敗しました: {res.stderr.strip()}")
            return
    except subprocess.TimeoutExpired:
        print(f"[{name}] ペアリング要求がタイムアウトしました。")
        return
        
    print(f"[{name}] ベースバンドでのペアリングが完了しました。")
    time.sleep(1)
    
    if check_port_created(name):
        print(f"\n✅ [{name}] ペアリングとポート生成が【完全に成功】しました！")
    else:
        print(f"\n❌ [{name}] エラー: MacのOSがシリアルポートの生成に失敗しました。")
        print("原因: Macの com.apple.Bluetooth.plist にSPPプロファイルが正しく記録されなかったためです。")
        print("対策: ゴースト化を防ぐため、自動的にペアリングを解除します。")
        subprocess.run([blueutil, "--unpair", mac])
        print("---")
        print("【手動での解決手順】")
        print("1. MacのBluetoothを一度オフにし、数秒待ってからオンに戻してください。")
        print("2. センサーの電源を一度切り、再度電源を入れてください。")
        print("3. その後、もう一度このツールを実行してペアリングを再試行してください。")

def main():
    blueutil = check_blueutil()
    if not blueutil:
        print("エラー: blueutil がインストールされていません。")
        print("ターミナルで 'brew install blueutil' を実行してください。")
        sys.exit(1)
        
    devices = find_tsnd_devices(blueutil)
    if not devices:
        print("TSND151センサーが見つかりませんでした。センサーの電源が入っているか確認してください。")
        sys.exit(0)
        
    print(f"\n{len(devices)}台のセンサーが見つかりました:")
    for i, (mac, name) in enumerate(devices):
        print(f"{i+1}: {name} ({mac})")
        
    print("\n操作を選択してください:")
    print("0: すべてペアリングする")
    print("1~: 指定した番号のセンサーをペアリングする")
    print("q: 終了")
    
    try:
        choice = input("入力 > ").strip()
        if choice.lower() == 'q':
            sys.exit(0)
            
        choice = int(choice)
        if choice == 0:
            for mac, name in devices:
                pair_device(blueutil, mac, name)
                time.sleep(2)
        elif 1 <= choice <= len(devices):
            mac, name = devices[choice-1]
            pair_device(blueutil, mac, name)
        else:
            print("無効な入力です。")
    except ValueError:
        print("数値を入力してください。")
    except KeyboardInterrupt:
        print("\n中断しました。")

if __name__ == "__main__":
    main()
