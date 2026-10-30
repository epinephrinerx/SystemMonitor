import Foundation

/// Mounted volumes and how full they are.
package enum DiskSensor {
    private static let keys: [URLResourceKey] = [
        .volumeNameKey, .volumeTotalCapacityKey, .volumeAvailableCapacityKey,
        .volumeAvailableCapacityForImportantUsageKey, .volumeIsLocalKey,
        .volumeIsRemovableKey, .volumeIsReadOnlyKey, .volumeIsBrowsableKey,
    ]

    /// Every volume a person would recognise: what Finder lists, without the
    /// system's hidden ones (Preboot, VM, the sealed system snapshot's
    /// siblings under /System/Volumes).
    package static func volumes() -> [Disk] {
        let urls = FileManager.default.mountedVolumeURLs(
            includingResourceValuesForKeys: keys, options: [.skipHiddenVolumes]) ?? []
        return urls.compactMap(volume)
    }

    private static func volume(_ url: URL) -> Disk? {
        guard let values = try? url.resourceValues(forKeys: Set(keys)),
              values.volumeIsBrowsable != false,
              let total = values.volumeTotalCapacity, total > 0 else { return nil }

        var disk = Disk(mountPoint: url.path)
        disk.name = values.volumeName ?? ""
        disk.isNetwork = values.volumeIsLocal == false
        disk.isRemovable = values.volumeIsRemovable ?? false
        disk.isReadOnly = values.volumeIsReadOnly ?? false

        var fs = statfs()
        if statfs(url.path, &fs) == 0 {
            disk.fileSystem = withUnsafeBytes(of: fs.f_fstypename) { String(cString: $0.bindMemory(to: CChar.self).baseAddress!) }
            let from = withUnsafeBytes(of: fs.f_mntfromname) { String(cString: $0.bindMemory(to: CChar.self).baseAddress!) }
            disk.bsdName = from.hasPrefix("/dev/") ? String(from.dropFirst(5)) : ""
        }

        // Finder's figure counts purgeable space as free, and so does this,
        // so the two agree. A network share does not compute it, and asking
        // can stall, so it gets the plain figure.
        let available: Int64
        if !disk.isNetwork, let important = values.volumeAvailableCapacityForImportantUsage {
            available = important
        } else {
            available = Int64(values.volumeAvailableCapacity ?? 0)
        }
        return space(disk, total: Int64(total), available: available)
    }

    /// Decimal gigabytes, the way Finder reports a volume.
    package static func space(_ disk: Disk, total: Int64, available: Int64) -> Disk {
        var disk = disk
        let free = min(max(available, 0), total)
        let used = total - free
        disk.totalGb = Double(total) / 1e9
        disk.usedGb = Double(used) / 1e9
        disk.usage = total == 0 ? 0 : Int((Double(used) * 100 / Double(total)).rounded())
        return disk
    }
}
