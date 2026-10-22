#!/usr/bin/env python3
"""Emit a bounded, read-only UnityMCP C# query; never launches or edits Unity."""
import argparse
import json
from pathlib import Path
import sys

def render(avatar, parameters, menu_root="", outfit_roots=(), body_renderers=()):
    if not avatar.strip():
        raise ValueError("Provide the exact avatar hierarchy path")
    for relative in (*outfit_roots, *body_renderers):
        if not relative.strip() or relative.startswith("/") or any(p in (".", "..") for p in relative.split("/")):
            raise ValueError("Outfit/body paths must be exact nonempty paths relative to the avatar")
    literals = {
        "__AVATAR_LITERAL__": json.dumps(avatar, ensure_ascii=True),
        "__PARAMETER_LITERALS__": ", ".join(json.dumps(p, ensure_ascii=True) for p in parameters),
        "__MENU_ROOT_LITERAL__": json.dumps(menu_root, ensure_ascii=True),
        "__OUTFIT_ROOT_LITERALS__": ", ".join(json.dumps(p, ensure_ascii=True) for p in outfit_roots),
        "__BODY_RENDERER_LITERALS__": ", ".join(json.dumps(p, ensure_ascii=True) for p in body_renderers),
    }
    template = Path(__file__).with_name("ma_state_probe.cs").read_text(encoding="utf-8")
    for token, literal in literals.items():
        template = template.replace(token, literal)
    return template

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--avatar", required=True)
    parser.add_argument("--exclusive-parameter", action="append", default=[])
    parser.add_argument("--menu-root", default="", help="Exact path relative to the avatar for this task's custom menu")
    parser.add_argument("--outfit-root", action="append", default=[], help="Exact affected garment root; inspect actual meshes and its MA Shape Changer targets")
    parser.add_argument("--body-renderer", action="append", default=[], help="Exact affected body SkinnedMeshRenderer path; report nonzero shapes without classifying their purpose")
    args = parser.parse_args()
    sys.stdout.reconfigure(encoding="utf-8")
    print(render(args.avatar, args.exclusive_parameter, args.menu_root, args.outfit_root, args.body_renderer))

if __name__ == "__main__":
    main()
