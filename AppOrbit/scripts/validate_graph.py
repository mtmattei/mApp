#!/usr/bin/env python3
"""Integrity checks for an App Graph (AppOrbit/graph/app-graph.schema.json).

Standard library only. Checks the things a renderer relies on:
  - unique node ids, well-formed ids
  - every edge endpoint exists; relation allowed for the endpoint types
  - every node and edge carries evidence; inferred evidence has a rationale
  - every source ref names a known file and a line inside its excerpt
  - every component-instance has exactly one instance-of or a containing host
  - every screen has a preview and belongs to a feature and uses a view model
  - every binds-to / invokes edge targets a member exposed by a view model
  - the entry screen exists

Usage: python scripts/validate_graph.py graph/orderly.graph.json
Exit code 1 on any error. Warnings do not fail the run.
"""
from __future__ import annotations

import json
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path

ID_RE = re.compile(r"^[a-z]+(\.[a-z0-9-]+)+$")


def load_rules(schema_path: Path) -> dict:
    schema = json.loads(schema_path.read_text(encoding="utf-8"))
    return schema["x-relationRules"], set(schema["$defs"]["node"]["properties"]["type"]["enum"])


def main(argv: list[str]) -> int:
    if len(argv) < 2:
        print(__doc__)
        return 2
    graph_path = Path(argv[1])
    schema_path = graph_path.parent / "app-graph.schema.json"
    graph = json.loads(graph_path.read_text(encoding="utf-8"))
    rules, node_types = load_rules(schema_path)

    errors: list[str] = []
    warnings: list[str] = []

    files = {f["id"]: f for f in graph.get("files", [])}
    nodes = {n["id"]: n for n in graph["nodes"]}

    dupes = [k for k, c in Counter(n["id"] for n in graph["nodes"]).items() if c > 1]
    for d in dupes:
        errors.append(f"duplicate node id {d}")

    def check_source(owner: str, ref: dict | None, required: bool = False) -> None:
        if ref is None:
            if required:
                errors.append(f"{owner}: missing source ref")
            return
        f = files.get(ref.get("file"))
        if f is None:
            errors.append(f"{owner}: source file {ref.get('file')!r} is not in files[]")
            return
        start, count = f["startLine"], len(f["lines"])
        for key in ("line", "endLine"):
            if key in ref and not (start <= ref[key] < start + count):
                errors.append(f"{owner}: {key} {ref[key]} is outside {f['path']} excerpt ({start}-{start + count - 1})")

    def check_evidence(owner: str, ev: dict | None) -> None:
        if not ev:
            errors.append(f"{owner}: missing evidence")
            return
        if ev.get("kind") == "inferred" and not ev.get("rationale"):
            errors.append(f"{owner}: inferred evidence needs a rationale")
        if ev.get("kind") == "inferred" and ev.get("confidence", 1) >= 1:
            warnings.append(f"{owner}: inferred evidence with confidence 1.0 looks wrong")
        check_source(owner, ev.get("source"))

    for nid, n in nodes.items():
        if not ID_RE.match(nid):
            errors.append(f"node id {nid!r} is not of the form kind.segment[.segment]")
        if n["type"] not in node_types:
            errors.append(f"{nid}: unknown type {n['type']}")
        check_evidence(nid, n.get("evidence"))
        check_source(nid, n.get("source"), required=n["type"] not in ("feature",))
        if n["type"] == "screen" and not n.get("preview", {}).get("parts"):
            errors.append(f"{nid}: screen has no preview parts")
        if n["type"] == "component-instance" and not n.get("preview", {}).get("bounds"):
            warnings.append(f"{nid}: instance has no preview bounds; it will not be selectable in the preview")

    out = defaultdict(list)
    inc = defaultdict(list)
    for i, e in enumerate(graph["edges"]):
        tag = f"edge[{i}] {e.get('from')} -{e.get('relation')}-> {e.get('to')}"
        a, b = nodes.get(e.get("from")), nodes.get(e.get("to"))
        if a is None:
            errors.append(f"{tag}: unknown from")
        if b is None:
            errors.append(f"{tag}: unknown to")
        rule = rules.get(e.get("relation"))
        if rule is None:
            errors.append(f"{tag}: unknown relation")
        elif a and b:
            if a["type"] not in rule["from"]:
                errors.append(f"{tag}: {a['type']} cannot be the source of {e['relation']}")
            if b["type"] not in rule["to"]:
                errors.append(f"{tag}: {b['type']} cannot be the target of {e['relation']}")
        check_evidence(tag, e.get("evidence"))
        if a and b:
            out[a["id"]].append(e)
            inc[b["id"]].append(e)

    def rel(nid: str, relation: str, direction: str = "out") -> list[dict]:
        pool = out[nid] if direction == "out" else inc[nid]
        return [e for e in pool if e["relation"] == relation]

    for nid, n in nodes.items():
        t = n["type"]
        if t == "screen":
            if len(rel(nid, "belongs-to")) != 1:
                errors.append(f"{nid}: screen must belong to exactly one feature")
            if len(rel(nid, "uses-viewmodel")) != 1:
                errors.append(f"{nid}: screen must use exactly one view model")
        if t == "component-instance":
            if not rel(nid, "contains", "in"):
                errors.append(f"{nid}: instance is not contained by a screen or instance")
            if len(rel(nid, "instance-of")) > 1:
                errors.append(f"{nid}: instance has more than one definition")
        if t == "component" and not rel(nid, "instance-of", "in"):
            warnings.append(f"{nid}: component definition has no instances")
        if t in ("property", "command"):
            if len(rel(nid, "exposes", "in")) != 1:
                errors.append(f"{nid}: member must be exposed by exactly one view model")
        if t == "route":
            if len(rel(nid, "navigates-to")) != 1:
                errors.append(f"{nid}: route must navigate to exactly one screen")
            if not rel(nid, "entered-via", "in"):
                errors.append(f"{nid}: route has no origin (entered-via)")
        if t == "state" and not rel(nid, "has-state", "in"):
            errors.append(f"{nid}: state is not attached to any owner")
        if t == "feature" and not rel(nid, "belongs-to", "in"):
            warnings.append(f"{nid}: feature has no screens")

    for e in graph["edges"]:
        if e["relation"] in ("binds-to", "invokes") and not e.get("label"):
            warnings.append(f"{e['from']} -{e['relation']}-> {e['to']}: no label (which property/command slot?)")

    if graph.get("entry") not in nodes or nodes[graph["entry"]]["type"] != "screen":
        errors.append("entry must name a screen")

    counts = Counter(n["type"] for n in graph["nodes"])
    print(f"{graph['name']}: {len(nodes)} nodes, {len(graph['edges'])} edges, {len(files)} files")
    print("  " + ", ".join(f"{k} {v}" for k, v in sorted(counts.items())))
    for w in warnings:
        print(f"  warn  {w}")
    for err in errors:
        print(f"  ERROR {err}")
    print("OK" if not errors else f"{len(errors)} error(s)")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
