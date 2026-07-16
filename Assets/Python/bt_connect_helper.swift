import Foundation
import IOBluetooth

class SDPQueryHandler: NSObject {
    var isDone = false
    var isSuccess = false
    
    @objc func sdpQueryComplete(_ device: IOBluetoothDevice!, status: IOReturn) {
        isDone = true
        isSuccess = (status == 0)
    }
}

guard CommandLine.arguments.count >= 3 else { exit(1) }
let macAddress = CommandLine.arguments[1]
let devPath = CommandLine.arguments[2]

if FileManager.default.fileExists(atPath: devPath) {
    fputs("[BT] Port \(devPath) already exists.\n", stderr)
    print("OK")
    exit(0)
}

guard let device = IOBluetoothDevice(addressString: macAddress) else {
    print("FAIL:not_found")
    exit(1)
}

fputs("[BT] Attempting connection to \(device.nameOrAddress ?? macAddress)...\n", stderr)

// 1. 強制的に開く
let connResult = device.openConnection()
fputs("[BT] openConnection: \(connResult)\n", stderr)

// エラーがNotPermittedでも無視してRFCOMMを試す
if let services = device.services as? [IOBluetoothSDPServiceRecord] {
    for svc in services {
        var channelID: BluetoothRFCOMMChannelID = 0
        if svc.getRFCOMMChannelID(&channelID) == 0 && channelID != 0 {
            fputs("[BT] Found RFCOMM channel \(channelID). Opening directly...\n", stderr)
            var channel: IOBluetoothRFCOMMChannel? = nil
            let openResult = device.openRFCOMMChannelSync(&channel, withChannelID: channelID, delegate: nil)
            fputs("[BT] openRFCOMMChannelSync: \(openResult)\n", stderr)
            
            for _ in 0..<20 {
                if FileManager.default.fileExists(atPath: devPath) {
                    let _ = channel?.close()
                    print("OK")
                    exit(0)
                }
                Thread.sleep(forTimeInterval: 0.5)
            }
            let _ = channel?.close()
        }
    }
}

// サービスが見つからなかったらSDPしてみる
let handler = SDPQueryHandler()
let sdpResult = device.performSDPQuery(handler)
fputs("[BT] performSDPQuery: \(sdpResult)\n", stderr)

let sdpDeadline = Date().addingTimeInterval(10)
while !handler.isDone && Date() < sdpDeadline {
    RunLoop.current.run(mode: .default, before: Date(timeIntervalSinceNow: 0.1))
}

if let services = device.services as? [IOBluetoothSDPServiceRecord] {
    for svc in services {
        var channelID: BluetoothRFCOMMChannelID = 0
        if svc.getRFCOMMChannelID(&channelID) == 0 && channelID != 0 {
            fputs("[BT] Found RFCOMM channel \(channelID) after SDP. Opening...\n", stderr)
            var channel: IOBluetoothRFCOMMChannel? = nil
            let openResult = device.openRFCOMMChannelSync(&channel, withChannelID: channelID, delegate: nil)
            fputs("[BT] openRFCOMMChannelSync: \(openResult)\n", stderr)
            
            for _ in 0..<20 {
                if FileManager.default.fileExists(atPath: devPath) {
                    let _ = channel?.close()
                    print("OK")
                    exit(0)
                }
                Thread.sleep(forTimeInterval: 0.5)
            }
            let _ = channel?.close()
        }
    }
}

print("FAIL:timeout")
exit(1)
