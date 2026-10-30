import SwiftUI
import SysMonitorCore

/// The Memory tab's lower half: what is running, grouped by name, with a
/// graph of the group picked; then the user's login agents (R-029).
struct ProcessSection: View {
    @ObservedObject var model: AppModel
    let actions: WindowActions?
    var theme: Theme
    var metrics: Metrics

    var body: some View {
        let m = metrics
        let t = model.strings
        let groups = ProcessGroups.build(model.snapshot.processes, filter: model.processFilter)

        VStack(alignment: .leading, spacing: m.len(8)) {
            HStack {
                Text(t("processes").uppercased())
                    .font(.system(size: m.type(Metrics.Font.label), weight: .semibold))
                    .foregroundStyle(theme.label.color)
                Text(t("process_count", String(model.snapshot.processes.count)))
                    .font(.system(size: m.type(Metrics.Font.small)))
                    .foregroundStyle(theme.label.color)
                Spacer()
                TextField(t("search"), text: $model.processFilter)
                    .textFieldStyle(.roundedBorder)
                    .font(.system(size: m.type(Metrics.Font.body)))
                    .frame(maxWidth: m.len(220))
            }

            if let picked = model.pickedProcess, model.histories.history(ProcessGroups.historyKey(picked)) != nil {
                ChartView(spec: ChartSpec(title: t("memory_of", picked), unit: .megabytes,
                                          series: [SeriesSpec(key: ProcessGroups.historyKey(picked), label: picked,
                                                              color: Accent.ram.rgb)]),
                          histories: model.histories, revision: model.historyRevision, theme: theme,
                          metrics: m, height: m.len(90))
            }

            LazyVStack(alignment: .leading, spacing: 0) {
                ForEach(groups) { group in
                    groupRow(group, m, t)
                    if model.expandedProcesses.contains(group.name) && group.members.count > 1 {
                        ForEach(group.members, id: \.pid) { p in
                            memberRow(p, m, t)
                        }
                    }
                    Divider().opacity(0.4)
                }
            }

            agents(m, t)
        }
        .padding(.horizontal, m.len(12))
        .padding(.vertical, m.len(10))
        .background(theme.control.color, in: RoundedRectangle(cornerRadius: m.len(10)))
    }

    private func groupRow(_ group: ProcessGroup, _ m: Metrics, _ t: Strings) -> some View {
        let open = model.expandedProcesses.contains(group.name)
        let picked = model.pickedProcess == group.name
        return HStack(spacing: m.len(8)) {
            Button {
                if open { model.expandedProcesses.remove(group.name) } else { model.expandedProcesses.insert(group.name) }
            } label: {
                Image(systemName: open ? "chevron.down" : "chevron.right")
                    .font(.system(size: m.type(Metrics.Font.tiny), weight: .bold))
                    .frame(width: m.len(14))
                    .foregroundStyle(theme.muted.color)
                    .opacity(group.members.count > 1 ? 1 : 0)
            }
            .buttonStyle(.plain)
            .disabled(group.members.count < 2)

            Text(group.name)
                .font(.system(size: m.type(Metrics.Font.body), weight: picked ? .semibold : .regular))
                .foregroundStyle(theme.text.color)
                .lineLimit(1)
            if group.members.count > 1 {
                Text("×\(group.members.count)")
                    .font(.system(size: m.type(Metrics.Font.small)))
                    .foregroundStyle(theme.label.color)
            }
            Spacer(minLength: m.len(8))
            Text(group.readable ? ProcessGroups.megabytes(group.megabytes) : t("no_access"))
                .font(.system(size: m.type(Metrics.Font.body)).monospacedDigit())
                .foregroundStyle(group.readable ? theme.text.color : theme.label.color)
            // The question counts what will be ended, not the protected too.
            let targets = group.members.filter { !ProcessControl.isProtected($0) }
            endButton(targets,
                      question: targets.count > 1
                          ? t("end_group_q", group.name, String(targets.count))
                          : t("end_pid_q", group.name, String(targets.first?.pid ?? group.members[0].pid)),
                      help: t(group.members.count > 1 ? "end_group" : "end_process"), m, t)
        }
        .padding(.vertical, m.len(4))
        .padding(.horizontal, m.len(4))
        .background(picked ? theme.chip.color : .clear, in: RoundedRectangle(cornerRadius: m.len(5)))
        .contentShape(Rectangle())
        .onTapGesture {
            guard group.readable else { return }
            model.pickedProcess = picked ? nil : group.name
        }
    }

    private func memberRow(_ p: ProcessEntry, _ m: Metrics, _ t: Strings) -> some View {
        HStack(spacing: m.len(8)) {
            Text("PID \(p.pid)")
                .font(.system(size: m.type(Metrics.Font.small)).monospacedDigit())
                .foregroundStyle(theme.label.color)
                .padding(.leading, m.len(26))
            Spacer()
            Text(p.footprint.map { ProcessGroups.megabytes(Double($0) / 1_048_576) } ?? t("no_access"))
                .font(.system(size: m.type(Metrics.Font.small)).monospacedDigit())
                .foregroundStyle(theme.muted.color)
            endButton(ProcessControl.isProtected(p) ? [] : [p], question: t("end_pid_q", p.name, String(p.pid)),
                      help: t("end_process"), m, t)
        }
        .padding(.vertical, m.len(2))
        .padding(.horizontal, m.len(4))
    }

    /// A stop button, or a padlock where there is nothing this user may end.
    @ViewBuilder
    private func endButton(_ targets: [ProcessEntry], question: String, help: String,
                           _ m: Metrics, _ t: Strings) -> some View {
        if targets.isEmpty {
            Image(systemName: "lock.fill")
                .font(.system(size: m.type(Metrics.Font.small)))
                .foregroundStyle(theme.label.color)
                .frame(width: m.len(22))
                .help(t("protected_hint"))
        } else {
            Button {
                guard actions?.confirm(question, detail: t("end_note"), action: t("end_process")) == true else { return }
                let failures = model.end(targets)
                if let (p, result) = failures.first {
                    actions?.inform(t("end_failed", "\(p.name) (PID \(p.pid))", model.reason(result)))
                }
            } label: {
                Image(systemName: "xmark.circle")
                    .font(.system(size: m.type(Metrics.Font.body)))
                    .foregroundStyle(Palette.hot.color)
                    .frame(width: m.len(22))
            }
            .buttonStyle(.plain)
            .help(help)
        }
    }

    @ViewBuilder
    private func agents(_ m: Metrics, _ t: Strings) -> some View {
        let agents = model.snapshot.launchAgents
        let running = Set(model.snapshot.processes.map(\.path).filter { !$0.isEmpty })

        VStack(alignment: .leading, spacing: m.len(4)) {
            HStack(alignment: .firstTextBaseline) {
                Text(t("launch_agents").uppercased())
                    .font(.system(size: m.type(Metrics.Font.label), weight: .semibold))
                    .foregroundStyle(theme.label.color)
                Text(t("launch_agents_hint"))
                    .font(.system(size: m.type(Metrics.Font.small)))
                    .foregroundStyle(theme.label.color)
            }
            .padding(.top, m.len(10))

            if agents.isEmpty {
                Text(t("no_agents"))
                    .font(.system(size: m.type(Metrics.Font.small)))
                    .foregroundStyle(theme.muted.color)
            }
            ForEach(agents, id: \.file) { agent in
                VStack(alignment: .leading, spacing: m.len(1)) {
                    HStack {
                        Circle().fill(running.contains(agent.program) ? Palette.ok.color : theme.barEmpty.color)
                            .frame(width: m.len(7), height: m.len(7))
                        Text(agent.label)
                            .font(.system(size: m.type(Metrics.Font.body)))
                            .foregroundStyle(theme.text.color)
                            .lineLimit(1)
                        Spacer()
                        Text(agentState(agent, running: running.contains(agent.program), t))
                            .font(.system(size: m.type(Metrics.Font.small)))
                            .foregroundStyle(theme.label.color)
                    }
                    Text(agent.readable ? agent.program : t("agent_unreadable"))
                        .font(.system(size: m.type(Metrics.Font.small)))
                        .foregroundStyle(theme.label.color)
                        .lineLimit(1)
                        .truncationMode(.middle)
                        .textSelection(.enabled)
                        .padding(.leading, m.len(15))
                }
                .padding(.vertical, m.len(2))
            }
        }
    }

    private func agentState(_ a: LaunchAgent, running: Bool, _ t: Strings) -> String {
        var parts = [t(running ? "running" : "not_running")]
        if a.runAtLoad { parts.append(t("run_at_load")) }
        if a.keepAlive { parts.append(t("keep_alive")) }
        if a.disabled { parts.append(t("disabled")) }
        return parts.joined(separator: " · ")
    }
}
