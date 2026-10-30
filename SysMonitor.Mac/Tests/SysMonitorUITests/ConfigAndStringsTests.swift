import Foundation
import Testing
@testable import SysMonitorUI

struct ConfigTests {
    private func decode(_ json: String) throws -> AppConfig {
        try JSONDecoder().decode(AppConfig.self, from: Data(json.utf8))
    }

    @Test func defaultsAreLightEnglishWidgetSized() {
        let c = AppConfig()
        #expect(c.theme == "light")
        #expect(c.lang == "en")
        #expect(!c.alwaysOnTop)
        #expect(c.snap)
        #expect(c.closeAction == "tray")
        #expect(c.speed == "balanced")
        #expect(c.widgetW == 270 && c.widgetH == 104)
        #expect(c.overallW == 630 && c.overallH == 480)
        #expect(c.widgetX == nil)
    }

    @Test func missingKeysTakeTheirDefaults() throws {
        let c = try decode(#"{"theme":"dark"}"#)
        #expect(c.theme == "dark")
        #expect(c.lang == "en")
        #expect(c.showCpu)
    }

    @Test func aWrongFieldLosesOnlyItself() throws {
        let c = try decode(#"{"opacity":"lots","lang":"th","showRam":false}"#)
        #expect(c.opacity == 1)
        #expect(c.lang == "th")
        #expect(!c.showRam)
    }

    @Test func outOfRangeValuesAreBroughtBack() throws {
        let c = try decode(#"""
            {"opacity":0.01,"fontScale":9,"widgetW":5000,"widgetH":1,"overallW":10,
             "lang":"fr","theme":"neon","cpuMode":"x","closeAction":"explode","speed":"warp"}
            """#)
        #expect(c.opacity == 0.35)
        #expect(c.fontScale == 1.6)
        #expect(c.widgetW == 900 && c.widgetH == 64)
        #expect(c.overallW == 470)
        #expect(c.lang == "en" && c.theme == "light" && c.cpuMode == "separated")
        #expect(c.closeAction == "tray" && c.speed == "balanced")
    }

    @Test func savedAndLoadedAreTheSame() {
        let dir = temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: dir) }
        let store = ConfigStore(url: dir.appendingPathComponent("nested/config.json"))
        var c = AppConfig()
        c.lang = "th"
        c.theme = "dark"
        c.opacity = 0.7
        c.widgetX = 100.5
        c.widgetY = -20
        c.diskMode = "total"
        store.save(c)
        #expect(store.load() == c)
    }

    @Test func anUnreadableFileIsKeptAsideNotLost() throws {
        let dir = temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: dir) }
        Diag.redirect(to: dir.appendingPathComponent("test.log"))
        defer { Diag.redirect(to: nil) }
        let url = dir.appendingPathComponent("config.json")
        try Data("{ not json".utf8).write(to: url)

        #expect(ConfigStore(url: url).load() == AppConfig())
        let kept = try String(contentsOf: url.appendingPathExtension("bad"), encoding: .utf8)
        #expect(kept == "{ not json")
    }

    @Test func noFileIsDefaults() {
        let url = temporaryDirectory().appendingPathComponent("absent.json")
        #expect(ConfigStore(url: url).load() == AppConfig())
    }
}

struct StringsTests {
    @Test func thaiAndEnglishHaveTheSameKeys() {
        #expect(Set(Strings.en.keys) == Set(Strings.th.keys))
    }

    @Test func placeholdersAreFilled() {
        let t = Strings("en")
        #expect(t("disk_warn", "Data", "75") == "⚠ Data is running low (75% used) · click to manage storage")
    }

    @Test func unknownLanguageIsEnglishAndUnknownKeyIsItself() {
        #expect(Strings("fr")("settings") == "Settings")
        #expect(Strings("th")("settings") == "ตั้งค่า")
        #expect(Strings("en")("no_such_key") == "no_such_key")
    }

    @Test func logTimestampsUseTheGregorianYear() {
        // 2026-10-31 must not come out as 2569 under a Thai locale.
        let date = Date(timeIntervalSince1970: 1_793_404_800)
        #expect(Diag.stamp(date).hasPrefix("2026-"))
    }
}

struct PaletteTests {
    @Test func loadAndTemperatureThresholdsAreSeparate() {
        #expect(Palette.loadWarmAt == 70 && Palette.loadHotAt == 90)
        #expect(Palette.warmAt == 65 && Palette.hotAt == 80)
    }

    @Test func metersKeepTheirColourUntilBusy() {
        #expect(Palette.loadColor(69, .ram) == Accent.ram.rgb)
        #expect(Palette.loadColor(70, .ram) == Palette.warm)
        #expect(Palette.loadColor(89, .disk) == Palette.warm)
        #expect(Palette.loadColor(90, .disk) == Palette.hot)
    }

    @Test func temperatureLights() {
        #expect(Palette.tempColor(nil) == nil)
        #expect(Palette.tempColor(64) == Palette.ok)
        #expect(Palette.tempColor(65) == Palette.warm)
        #expect(Palette.tempColor(80) == Palette.hot)
        #expect(Palette.tempBadge(nil, .light) == Theme.light.tag)
    }

    @Test func themesMatchTheWindowsBuild() {
        #expect(Theme.light.panel == Rgb(0xffffff))
        #expect(Theme.dark.panel == Rgb(0x0f172a))
        #expect(Accent.cpu.rgb == Rgb(0x3b82f6))
    }
}

struct MetricsTests {
    @Test func fontSettingIsClampedAndBothScalesApply() {
        let m = Metrics(window: 2, font: 5)
        #expect(m.font == 1.6)
        #expect(m.type(10) == 32)
        #expect(m.len(10) == 20)
        #expect(Metrics(window: .nan, font: 0).window == 1)
    }

    @Test func scaleFollowsTheTighterAxis() {
        #expect(Metrics.widgetScale(CGSize(width: 540, height: 208)) == 2)
        #expect(Metrics.overallScale(CGSize(width: 630, height: 240)) == 0.5)
    }
}
