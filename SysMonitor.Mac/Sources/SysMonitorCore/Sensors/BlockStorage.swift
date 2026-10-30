import Foundation
import IOKit

/// The physical device behind a volume, and its I/O counters, from the IOKit
/// registry.
///
/// A volume's media object sits some way below the device. For APFS the path
/// up runs volume -> synthesized container disk -> container scheme ->
/// physical partition -> whole physical disk -> `IOBlockStorageDriver`, so
/// the walk goes up the service plane until it reaches the driver, which
/// keeps the counters, and remembers the last whole disk it passed, which is
/// the physical one.
package enum BlockStorage {
    package static func device(forBSDName bsdName: String) -> PhysicalDevice? {
        guard !bsdName.isEmpty,
              let matching = IOBSDNameMatching(kIOMainPortDefault, 0, bsdName) else { return nil }
        // IOServiceGetMatchingService consumes the matching dictionary.
        var entry = IOServiceGetMatchingService(kIOMainPortDefault, matching)
        var wholeDisk = ""

        while entry != 0 {
            if IOObjectConformsTo(entry, "IOMedia") != 0,
               property(entry, "Whole") as? Bool == true,
               let name = property(entry, "BSD Name") as? String {
                wholeDisk = name
            }
            if IOObjectConformsTo(entry, "IOBlockStorageDriver") != 0 {
                defer { IOObjectRelease(entry) }
                return describe(driver: entry, wholeDisk: wholeDisk.isEmpty ? bsdName : wholeDisk)
            }
            var parent: io_registry_entry_t = 0
            let result = IORegistryEntryGetParentEntry(entry, kIOServicePlane, &parent)
            IOObjectRelease(entry)
            entry = result == KERN_SUCCESS ? parent : 0
        }
        return nil
    }

    /// Lifetime bytes read and written, or nil when the driver has gone.
    package static func counters(driverID: UInt64) -> (read: UInt64, written: UInt64)? {
        guard driverID != 0, let matching = IORegistryEntryIDMatching(driverID) else { return nil }
        let driver = IOServiceGetMatchingService(kIOMainPortDefault, matching)
        guard driver != 0 else { return nil }
        defer { IOObjectRelease(driver) }
        guard let stats = property(driver, "Statistics") as? [String: Any],
              let read = (stats["Bytes (Read)"] as? NSNumber)?.uint64Value,
              let written = (stats["Bytes (Write)"] as? NSNumber)?.uint64Value else { return nil }
        return (read, written)
    }

    private static func describe(driver: io_registry_entry_t, wholeDisk: String) -> PhysicalDevice {
        var device = PhysicalDevice(id: wholeDisk)
        var id: UInt64 = 0
        if IORegistryEntryGetRegistryEntryID(driver, &id) == KERN_SUCCESS {
            device.driverID = id
        }

        // The device characteristics live on the driver's provider: the NVMe,
        // AHCI, USB or disk-image device object.
        var provider: io_registry_entry_t = 0
        guard IORegistryEntryGetParentEntry(driver, kIOServicePlane, &provider) == KERN_SUCCESS else {
            return device
        }
        defer { IOObjectRelease(provider) }

        let characteristics = property(provider, "Device Characteristics") as? [String: Any] ?? [:]
        let proto = property(provider, "Protocol Characteristics") as? [String: Any] ?? [:]

        device.model = trimmed(characteristics["Product Name"])
        device.vendor = trimmed(characteristics["Vendor Name"])
        device.serial = trimmed(characteristics["Serial Number"])
        device.revision = trimmed(characteristics["Product Revision Level"])
        device.media = media(characteristics["Medium Type"] as? String)
        device.bus = bus(proto["Physical Interconnect"] as? String)
        device.isInternal = (proto["Physical Interconnect Location"] as? String) == "Internal"
        return device
    }

    package static func media(_ mediumType: String?) -> String {
        switch mediumType {
        case "Solid State": return "SSD"
        case "Rotational": return "HDD"
        default: return "Disk"
        }
    }

    /// IOKit's interconnect names, shortened where the short form is the one
    /// people use.
    package static func bus(_ interconnect: String?) -> String {
        switch interconnect {
        case nil: return ""
        case "PCI-Express", "PCI": return "PCIe"
        case "Virtual Interface": return "Disk Image"
        case let name?: return name
        }
    }

    private static func property(_ entry: io_registry_entry_t, _ key: String) -> Any? {
        IORegistryEntryCreateCFProperty(entry, key as CFString, kCFAllocatorDefault, 0)?
            .takeRetainedValue()
    }

    private static func trimmed(_ value: Any?) -> String {
        (value as? String)?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
    }
}
