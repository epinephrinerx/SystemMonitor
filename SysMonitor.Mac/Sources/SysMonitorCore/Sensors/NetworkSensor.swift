import Darwin
import SystemConfiguration

/// Wi-Fi and Ethernet adapters and their byte counters.
///
/// SystemConfiguration says which interfaces are hardware a person would call
/// an adapter, and what to call them; that leaves out the dozen virtual ones
/// macOS keeps (awdl, llw, utun, anpi, bridge). The counters come from the
/// routing socket's 64-bit `if_data64`: the 32-bit counters `getifaddrs`
/// hands out wrap at 4 GB, which a busy link passes in minutes.
package enum NetworkSensor {
    package struct Hardware: Equatable, Sendable {
        package var bsdName: String
        package var name: String
        package var wireless: Bool
    }

    package struct Counters: Equatable, Sendable {
        package var received: UInt64
        package var sent: UInt64
        package var baudRate: UInt64
        package var running: Bool
    }

    /// Ethernet and Wi-Fi interfaces. Static while nothing is plugged in, so
    /// read on the slow cadence.
    package static func hardware() -> [Hardware] {
        guard let all = SCNetworkInterfaceCopyAll() as? [SCNetworkInterface] else { return [] }
        return all.compactMap { interface in
            guard let bsd = SCNetworkInterfaceGetBSDName(interface) as String?,
                  let type = SCNetworkInterfaceGetInterfaceType(interface) as String? else { return nil }
            let wireless = type == (kSCNetworkInterfaceTypeIEEE80211 as String)
            guard wireless || type == (kSCNetworkInterfaceTypeEthernet as String) else { return nil }
            let name = SCNetworkInterfaceGetLocalizedDisplayName(interface) as String? ?? bsd
            return Hardware(bsdName: bsd, name: name, wireless: wireless)
        }
    }

    /// Interfaces holding a routable address. Up-and-running is not enough on
    /// its own: the Thunderbolt ports are up as bridge members with nothing
    /// on the other end.
    package static func connected() -> Set<String> {
        var result = Set<String>()
        var list: UnsafeMutablePointer<ifaddrs>?
        guard getifaddrs(&list) == 0, let first = list else { return result }
        defer { freeifaddrs(list) }
        for entry in sequence(first: first, next: { $0.pointee.ifa_next }) {
            guard let address = entry.pointee.ifa_addr else { continue }
            let name = String(cString: entry.pointee.ifa_name)
            switch Int32(address.pointee.sa_family) {
            case AF_INET:
                result.insert(name)
            case AF_INET6:
                let linkLocal = address.withMemoryRebound(to: sockaddr_in6.self, capacity: 1) {
                    $0.pointee.sin6_addr.__u6_addr.__u6_addr8.0 == 0xfe
                        && ($0.pointee.sin6_addr.__u6_addr.__u6_addr8.1 & 0xc0) == 0x80
                }
                if !linkLocal {
                    result.insert(name)
                }
            default:
                break
            }
        }
        return result
    }

    /// Byte counters for every interface, by BSD name.
    package static func counters() -> [String: Counters] {
        var mib: [Int32] = [CTL_NET, PF_ROUTE, 0, 0, NET_RT_IFLIST2, 0]
        var length = 0
        guard sysctl(&mib, UInt32(mib.count), nil, &length, nil, 0) == 0, length > 0 else { return [:] }
        // Room for an interface that appears between the two calls.
        length += length / 8
        var buffer = [UInt8](repeating: 0, count: length)
        guard sysctl(&mib, UInt32(mib.count), &buffer, &length, nil, 0) == 0 else { return [:] }

        var result: [String: Counters] = [:]
        buffer.withUnsafeBytes { raw in
            var offset = 0
            while offset + MemoryLayout<if_msghdr>.size <= length {
                let header = raw.loadUnaligned(fromByteOffset: offset, as: if_msghdr.self)
                let messageLength = Int(header.ifm_msglen)
                guard messageLength > 0 else { break }
                if Int32(header.ifm_type) == RTM_IFINFO2,
                   offset + MemoryLayout<if_msghdr2>.size <= length {
                    let info = raw.loadUnaligned(fromByteOffset: offset, as: if_msghdr2.self)
                    var name = [CChar](repeating: 0, count: Int(IF_NAMESIZE))
                    if if_indextoname(UInt32(info.ifm_index), &name) != nil {
                        let flags = info.ifm_flags
                        result[String(cString: name)] = Counters(
                            received: info.ifm_data.ifi_ibytes,
                            sent: info.ifm_data.ifi_obytes,
                            baudRate: UInt64(info.ifm_data.ifi_baudrate),
                            running: flags & IFF_UP != 0 && flags & IFF_RUNNING != 0)
                    }
                }
                offset += messageLength
            }
        }
        return result
    }
}
