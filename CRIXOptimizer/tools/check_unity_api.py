#!/usr/bin/env python3
"""Checks that every Unity engine member CRIXOptimizer.dll references is declared in a given Unity version.

Gorilla Tag ships Unity engine assemblies that cannot be redistributed, and Unity's reference assemblies for
Unity 6 are not on nuget.org, so this compares the DLL's member references (listed with `monodis`) against
Unity's published C# reference source (github.com/Unity-Technologies/UnityCsReference) at the game's version.
It checks names (every referenced Unity type, nested types and delegates included, is declared; every member is
declared in its type's files) and reports [Obsolete] members.
It cannot prove binary signatures; tools/RefCheck does that against real assemblies when you have them.

Usage:
  git clone --depth 1 --filter=blob:none --sparse --branch 6000.1.9f1 \\
      https://github.com/Unity-Technologies/UnityCsReference ucs && (cd ucs && git sparse-checkout set Runtime Modules)
  python3 tools/check_unity_api.py bin/Release/CRIXOptimizer.dll ucs
"""
import collections
import os
import re
import subprocess
import sys


def member_refs(dll):
    out = subprocess.run(["monodis", "--memberref", dll], capture_output=True, text=True, check=True).stdout
    refs = set()
    for m in re.finditer(r"Resolved: \[(UnityEngine[^\]]*)\]([^\n]+)\n\tSignature: ([^\n]+)", out):
        refs.add((m.group(1), m.group(2).strip().split("(")[0]))
    return sorted(refs)


def type_refs(dll):
    out = subprocess.run(["monodis", "--typeref", dll], capture_output=True, text=True, check=True).stdout
    return sorted(set(m.group(1) for m in re.finditer(r"^\d+: \[UnityEngine[^\]]*\](\S+)$", out, re.M)))


def simple_name(type_name):
    # Nested types are written Outer/Inner; generic arity as Name`1.
    return type_name.split("/")[-1].split(".")[-1].split("`")[0]


def main():
    if len(sys.argv) != 3:
        print(__doc__)
        return 2
    dll, src = sys.argv[1], sys.argv[2]
    files = {}
    for root, _, names in os.walk(src):
        for name in names:
            if name.endswith(".cs"):
                path = os.path.join(root, name)
                with open(path, encoding="utf-8", errors="ignore") as f:
                    files[path] = f.read()
    declared = collections.defaultdict(list)
    delegates = set()
    for path, text in files.items():
        for m in re.finditer(r"\b(?:class|struct|enum|interface)\s+(\w+)", text):
            declared[m.group(1)].append(path)
        for m in re.finditer(r"\bdelegate\s+[\w<>\[\],.?]+\s+(\w+)\s*[<(]", text):
            declared[m.group(1)].append(path)
            delegates.add(m.group(1))

    missing, obsolete, notes, checked = [], [], [], 0
    types = type_refs(dll)
    for full in types:
        if declared.get(simple_name(full)):
            continue
        # Generic delegates such as UnityAction<T0,T1> come from generated files that are not in UnityCsReference;
        # accept them when Unity's published API itself uses them with the same number of type arguments.
        arity = int(full.split("`")[1]) if "`" in full.split("/")[-1] else 0
        name = simple_name(full)
        use = re.compile(r"\b" + re.escape(name) + r"<([^<>;(){}]*)>\s+\w+\s*[;{=]")
        used = arity > 0 and any(m.group(1).count(",") + 1 == arity for t in files.values() for m in use.finditer(t))
        if used:
            notes.append(f"{full}: declared in Unity's generated source; used by Unity's own published API")
        else:
            missing.append(f"{full}: type not declared")
    for asm, full in member_refs(dll):
        if full.endswith("..ctor"):
            type_name, member = full[: -len("..ctor")].rsplit(".", 1)[-1], ".ctor"
        else:
            type_name, member = full.rsplit(".", 2)[-2:]
        type_name = simple_name(type_name)
        checked += 1
        texts = [files[p] for p in declared.get(type_name, [])]
        if not texts:
            missing.append(f"{full}: type {type_name} not declared")
            continue
        if member == ".ctor" or member.startswith("op_") or (type_name in delegates and member in ("Invoke", "BeginInvoke", "EndInvoke")):
            continue  # constructors / operators of declared types; a delegate's members are implicit
        name = member.split("_", 1)[1] if re.match(r"^(get|set|add|remove)_", member) else member
        pattern = re.compile(r"(\[[^\]]*\]\s*)*[^\n;{}]*\b" + re.escape(name) + r"\b\s*(\{|=>|;|=|\(|<)")
        hits = [m for t in texts for m in pattern.finditer(t)]
        if not hits:
            missing.append(f"{full}: member {name} not declared in {type_name}")
            continue
        # An [Obsolete] attribute directly above (or on) the line that declares this member.
        decl = (r"\[(?:System\.)?Obsolete\(([^\n]*?)\)\][^\n]*\n?(?:[ \t]*\[[^\n]*\][ \t]*\n?)*[ \t]*"
                r"(?:\[[^\n]*\][ \t]*)*(?:(?:public|internal|protected|private|static|extern|override|virtual|new|unsafe)\s+)+"
                r"[\w<>\[\],.?]+\s+" + re.escape(name) + r"\s*(?:\{|=>|;|=|\()")
        for t in texts:
            m = re.search(decl, t)
            if m:
                obsolete.append(f"{full}: [Obsolete({m.group(1)[:90]})]")
                break
    print(f"checked {len(types)} Unity type references and {checked} Unity member references")
    for line in sorted(set(obsolete)):
        print("obsolete (still present): " + line)
    for line in notes:
        print("note: " + line)
    for line in missing:
        print("MISSING: " + line)
    print("RESULT: " + ("all referenced Unity members are declared" if not missing else f"{len(missing)} missing"))
    return 1 if missing else 0


if __name__ == "__main__":
    sys.exit(main())
