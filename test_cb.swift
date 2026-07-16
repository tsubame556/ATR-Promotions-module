import CoreBluetooth

class Delegate: NSObject, CBCentralManagerDelegate {
    func centralManagerDidUpdateState(_ central: CBCentralManager) {
        print("State: \(central.state.rawValue)")
        exit(0)
    }
}
let delegate = Delegate()
let manager = CBCentralManager(delegate: delegate, queue: nil)
RunLoop.current.run(until: Date(timeIntervalSinceNow: 5))
