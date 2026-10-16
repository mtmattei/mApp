#!/usr/bin/env python3
"""Generates a large, valid app graph for stress testing App Orbit.

  python scripts/stress_graph.py <features> <screens-per-feature> [out.json]

Every screen gets a view model (members, dependencies, an inferred link now and then), a dozen
instances (some nested, some using shared components), states with transitions, and routes to
other screens. Sources point into generated files, so the graph passes scripts/validate_graph.py.
Deterministic: the same arguments always produce the same graph.
"""
from __future__ import annotations

import json
import random
import sys
from pathlib import Path

WORDS = ["Account", "Invoice", "Ledger", "Shipment", "Route", "Ticket", "Asset", "Contract", "Survey", "Booking",
         "Roster", "Payment", "Claim", "Order", "Stock", "Visit", "Report", "Policy", "Device", "Session"]


def main(features: int, per_feature: int, out: Path) -> None:
    rnd = random.Random(features * 1000 + per_feature)
    files, nodes, edges = [], [], []

    def file(fid: str, path: str, lines: list[str]) -> None:
        files.append({"id": fid, "path": path, "language": "xaml" if path.endswith(".xaml") else "csharp", "startLine": 1, "lines": lines})

    def ev(fid: str | None = None, line: int | None = None, kind: str = "declared", conf: float = 1.0, why: str | None = None) -> dict:
        e = {"kind": kind, "confidence": conf}
        if fid:
            e["source"] = {"file": fid, "line": line}
        if why:
            e["rationale"] = why
        return e

    def node(nid, ntype, name, fid=None, line=None, **extra) -> str:
        n = {"id": nid, "type": ntype, "name": name, "evidence": ev(fid, line)}
        if fid:
            n["source"] = {"file": fid, "line": line}
        n.update(extra)
        nodes.append(n)
        return nid

    def edge(a, rel, b, fid=None, line=None, label=None, evidence=None) -> None:
        e = {"from": a, "relation": rel, "to": b, "evidence": evidence or ev(fid, line)}
        if label:
            e["label"] = label
        edges.append(e)

    # shared components
    comps = []
    for c in range(6):
        cid = f"component.shared-{c}"
        fid = f"f.comp-{c}"
        file(fid, f"Stress/Controls/Shared{c}.xaml", [f"<UserControl x:Class=\"Stress.Controls.Shared{c}\">", "  <Grid />", "</UserControl>"])
        comps.append(node(cid, "component", f"Shared{c}", fid, 1, properties={"uno": {"type": "UserControl", "class": f"Stress.Controls.Shared{c}"}}))

    app_lines = ["private static void RegisterRoutes(IViewRegistry views, IRouteRegistry routes)", "{"]
    screens: list[str] = []
    screen_routes: list[tuple[str, int]] = []
    origins: dict[str, list[str]] = {}
    for f in range(features):
        fname = f"{WORDS[f % len(WORDS)]}{'' if f < len(WORDS) else f // len(WORDS) + 1}"
        fid_feature = node(f"feature.f{f}", "feature", fname, summary=f"Generated feature {f}.")
        nodes[-1]["evidence"] = ev(kind="derived", conf=0.9)
        for s in range(per_feature):
            k = f"f{f}s{s}"
            sname = f"{fname}{['List', 'Detail', 'Edit', 'Review', 'History', 'Settings', 'Search', 'Summary'][s % 8]}{'' if s < 8 else s}"
            app_lines.append(f"    new ViewMap<{sname}Page, {sname}ViewModel>(),")
            reg_line = len(app_lines)
            app_lines.append(f"    new (\"{sname}\", View: views.FindByView<{sname}Page>()),")
            route_line = len(app_lines)
            xf, vf = f"f.{k}-xaml", f"f.{k}-vm"
            n_inst = 12
            xaml = [f"<Page x:Class=\"Stress.{sname}Page\">"] + [f"  <Element{i} x:Name=\"E{i}\" />" for i in range(n_inst)] + \
                   [f"  <State{i} />" for i in range(5)] + ["</Page>"]
            file(xf, f"Stress/Presentation/{sname}Page.xaml", xaml)
            n_members = 20
            vm_lines = [f"public partial record {sname}Model" + " {"] + [f"    public IFeed<int> Member{i} => Feed.Async(Load{i});" for i in range(n_members)] + ["}"]
            file(vf, f"Stress/Presentation/{sname}Model.cs", vm_lines)
            parts = []
            for i in range(n_inst):
                y = 0.04 + i * 0.078
                parts.append({"kind": ["heading", "text", "button", "input", "list", "badge"][i % 6], "rect": {"x": 0.07, "y": round(y, 3), "w": 0.86, "h": 0.06}, "label": f"Element {i}"})
            sid = node(f"screen.{k}", "screen", sname, xf, 1, summary=f"Generated screen {k}.",
                       properties={"route": sname, "uno": {"type": "Page", "class": f"Stress.{sname}Page"}}, preview={"parts": parts})
            nodes[-1]["evidence"] = ev("f.app", route_line)
            screens.append(sid)
            screen_routes.append((sid, route_line))
            edge(sid, "belongs-to", fid_feature, evidence=ev(kind="derived", conf=0.9))
            vm = node(f"vm.{k}", "viewmodel", f"{sname}ViewModel", vf, 1, properties={"uno": {"pattern": "MVUX", "model": f"{sname}Model"}})
            edge(sid, "uses-viewmodel", vm, "f.app", reg_line)
            members = []
            for i in range(n_members):
                is_cmd = i % 7 == 6
                mid = f"{'cmd' if is_cmd else 'prop'}.{k}.m{i}"
                long = "WithAVeryLongDescriptiveNameThatKeepsGoing" if i == 3 else ""
                node(mid, "command" if is_cmd else "property", f"Member{i}{long}", vf, 2 + i,
                     properties={"clrType": "ICommand (generated)" if is_cmd else "IFeed<int>"})
                edge(vm, "exposes", mid, vf, 2 + i)
                members.append((mid, is_cmd))
            props = [m for m, c in members if not c]
            cmds = [m for m, c in members if c]
            for i in range(1, len(props)):
                if rnd.random() < 0.35:
                    edge(props[i], "depends-on", props[rnd.randrange(i)], vf, 2)
            insts = []
            for i in range(n_inst):
                iid = f"inst.{k}.e{i}"
                p = parts[i]["rect"]
                node(iid, "component-instance", f"Element {i}", xf, 2 + i, properties={"uno": {"type": "Control", "xName": f"E{i}"}}, preview={"bounds": p})
                host = sid if i % 4 != 3 else insts[-1]
                edge(host, "contains", iid, xf, 2 + i)
                if i % 5 == 4:
                    edge(iid, "instance-of", comps[rnd.randrange(len(comps))], xf, 2 + i)
                if i % 3 == 0 and cmds:
                    edge(iid, "invokes", rnd.choice(cmds), xf, 2 + i, label="Command")
                else:
                    edge(iid, "binds-to", rnd.choice(props), xf, 2 + i, label=rnd.choice(["Text", "IsEnabled", "ItemsSource", "Visibility"]))
                insts.append(iid)
            sts = []
            for j in range(5):
                stid = f"state.{k}.s{j}"
                node(stid, "state", ["Loading", "Empty", "Error", "Populated", "Offline"][j], xf, 2 + n_inst + j,
                     properties={"trigger": f"condition {j}", "uno": {"mechanism": "generated"}},
                     preview={"overrides": [{"kind": "alert", "rect": parts[j]["rect"], "label": f"State {j}"}]})
                edge(sid, "has-state", stid, xf, 2 + n_inst + j)
                if j:
                    edge(sts[-1], "transitions-to", stid, xf, 2 + n_inst + j, label="next")
                edge(stid, "depends-on", rnd.choice(props), xf, 2 + n_inst + j)
                sts.append(stid)
            origins[sid] = insts
    app_lines.append("}")
    file("f.app", "Stress/App.xaml.cs", app_lines)

    # inferred links between neighbouring screens' first members
    for a, b in zip(screens, screens[1:]):
        if rnd.random() < 0.15:
            edge(f"prop.{a.split('.', 1)[1]}.m0", "depends-on", f"prop.{b.split('.', 1)[1]}.m1",
                 evidence=ev(kind="inferred", conf=round(0.4 + rnd.random() * 0.5, 2), why="Both read the same generated service."))

    # routes: each screen to two others (cycles included)
    route_line_of = dict(screen_routes)
    made: set[str] = set()
    for i, sid in enumerate(screens):
        for t in (1, rnd.randrange(len(screens))):
            target = screens[(i + t) % len(screens)]
            if target == sid:
                continue
            rid = f"route.r{i}-{t}"
            if rid in made:
                continue
            made.add(rid)
            origin = origins[sid][2]
            src = next(n for n in nodes if n["id"] == origin)["source"]
            node(rid, "route", f"{sid.split('.')[1]} → {target.split('.')[1]}", src["file"], src["line"], properties={"request": target, "mechanism": "Navigation.Request"})
            edge(origin, "entered-via", rid, src["file"], src["line"], label="click")
            edge(rid, "navigates-to", target, "f.app", route_line_of[target])

    graph = {"schemaVersion": "0.1", "graphId": f"stress-{features}x{per_feature}", "name": f"Stress {features}×{per_feature}",
             "description": f"Generated stress graph: {features} features, {features * per_feature} screens.",
             "entry": screens[0], "files": files, "nodes": nodes, "edges": edges, "unresolved": []}
    out.write_text(json.dumps(graph), encoding="utf-8")
    print(f"wrote {out}: {len(nodes)} nodes, {len(edges)} edges, {len(files)} files, {out.stat().st_size // 1024} KB")


if __name__ == "__main__":
    f, s = int(sys.argv[1]), int(sys.argv[2])
    main(f, s, Path(sys.argv[3]) if len(sys.argv) > 3 else Path(f"stress-{f}x{s}.graph.json"))
