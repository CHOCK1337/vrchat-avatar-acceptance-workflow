#!/usr/bin/env python3
"""Optional local HTML for existing images only. No capture, network or acceptance."""
from __future__ import annotations
import argparse
import base64
import html
import json
from pathlib import Path
import sys
from collaboration_lite import load_state, candidate, atomic_text, file_hash, read_json

MIME={'.png':'image/png','.jpg':'image/jpeg','.jpeg':'image/jpeg','.webp':'image/webp'}


def render(task_path: Path, manifest: dict, output: Path) -> dict:
    tp,task,project,state=load_state(task_path)
    if not state['choice']['html_preview']:
        raise ValueError('HTML was not selected; view the same original images directly')
    items=manifest.get('items')
    if not isinstance(items,list) or len(items)>60:
        raise ValueError('Use up to 60 existing selected captures; do not collect an extra batch')
    if not items:raise ValueError('No existing images supplied; do not create fake review evidence')
    current=candidate(task)
    if not current['id'] or current['revision'] is None or not current['sha256']:
        raise ValueError('Record the current candidate before presenting a candidate-bound gallery')
    output=Path(output).resolve()
    if output.suffix.lower()!='.html':raise ValueError('Output must be .html')
    if output.is_relative_to(project/'Assets') or output.is_relative_to(project/'Packages'):
        raise ValueError('Place the review HTML outside Unity Assets/Packages')
    total=0;cards=[]
    for i,item in enumerate(items):
        p=Path(item['path']).resolve(strict=True)
        if not p.is_file() or p.suffix.lower() not in MIME:raise ValueError('Only PNG/JPEG/WebP originals are accepted')
        size=p.stat().st_size;total+=size
        if size>8*1024*1024 or total>64*1024*1024:raise ValueError('Gallery input too large; select relevant originals, do not reshoot/recompress evidence')
        raw=p.read_bytes()
        kind=item.get('capture_kind','unknown')
        same=item.get('candidate')==current
        group='current-controls' if same and kind=='actual_controls' else 'current-diagnostic' if same and kind=='direct_values' else 'reference'
        label={'current-controls':'当前候选 · 实际控制采集','current-diagnostic':'当前候选 · 直接置值诊断，不证明菜单','reference':'历史 / 参考 / 来源未确认'}[group]
        resource=html.escape(str(item.get('resource','未标资源')))
        state_text=html.escape(str(item.get('state','未标状态')))
        cap=html.escape(label)
        uri='data:'+MIME[p.suffix.lower()]+';base64,'+base64.b64encode(raw).decode('ascii')
        cards.append(f'<article data-group="{group}"><button class="open" type="button"><img src="{uri}" alt="{resource} · {state_text}"></button><div class="body"><span class="tag">{cap}</span><h2>{resource}</h2><p>{state_text}</p><details><summary>原文件与身份</summary><p>{html.escape(str(p))}</p><code>{file_hash(p)}</code><pre>{html.escape(json.dumps(item.get("candidate"),ensure_ascii=False,indent=2))}</pre></details></div></article>')
    content='''<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data:; style-src 'unsafe-inline'; script-src 'unsafe-inline'; connect-src 'none'; base-uri 'none'; form-action 'none'"><title>当前模型 · 原图审阅</title><style>
    *{box-sizing:border-box}body{margin:0;background:#f3f4f7;color:#202530;font:15px/1.55 system-ui,sans-serif}main{max-width:1220px;margin:auto;padding:32px 22px}h1{font-size:28px;margin:4px 0 12px}header p{max-width:940px}#candidate{overflow-wrap:anywhere;font:12px/1.6 ui-monospace,monospace}.notice{background:#fff5d9;padding:12px 16px;border-left:4px solid #bf8733;border-radius:4px}nav{display:flex;gap:8px;flex-wrap:wrap;margin:24px 0}button{cursor:pointer;font:inherit}nav button{padding:9px 14px;background:white;border:1px solid #cbd0da;border-radius:8px}nav button.selected{background:#26364a;color:white}#grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(275px,1fr));gap:18px}article{background:white;box-shadow:0 3px 12px #14234012;border-radius:12px;overflow:hidden}.open{display:block;padding:0;border:0;width:100%;background:#171a20}.open img{display:block;width:100%;height:290px;object-fit:contain}.body{padding:16px}h2{font-size:18px;margin:8px 0}p{margin:8px 0}.tag{font-size:12px;color:#4a5e76}code,pre{font-size:11px;overflow-wrap:anywhere;white-space:pre-wrap}details p{word-break:break-all;font-size:12px}dialog{border:0;border-radius:10px;padding:14px;max-width:95vw;max-height:95vh;background:#171a20;color:white}dialog::backdrop{background:#000b}dialog img{display:block;max-width:90vw;max-height:80vh;object-fit:contain}dialog button{float:right;margin-bottom:10px}footer{margin-top:24px;color:#626d7c;font-size:13px}
    </style></head><body><main><header><p>本地文件 · 不联网 · 不自动判定通过</p><h1>当前模型 · 原图审阅</h1><p id="candidate">CANDIDATE_TEXT</p><div class="notice">仅复用已有图片。画面、输入轨迹与原验收规范共同判断结果；生成此页面不等于模型合格，也不授权上传。直接置值图只用于诊断。</div></header><nav><button data-filter="all" class="selected">全部</button><button data-filter="current-controls">实际控制</button><button data-filter="current-diagnostic">直接置值诊断</button><button data-filter="reference">参考 / 历史</button></nav><section id="grid">CARDS</section><footer>点击图片查看完整分辨率。反馈请回到 Codex 指明资源、状态和实际问题；不需要重新做测试报告。</footer></main><dialog id="viewer"><button id="close" type="button">关闭</button><img alt="完整原图"></dialog><script>
    const dialog=document.querySelector('#viewer');document.querySelectorAll('.open').forEach(b=>b.onclick=()=>{dialog.querySelector('img').src=b.querySelector('img').src;dialog.showModal()});document.querySelector('#close').onclick=()=>dialog.close();document.querySelectorAll('[data-filter]').forEach(b=>b.onclick=()=>{document.querySelectorAll('[data-filter]').forEach(x=>x.classList.toggle('selected',x===b));document.querySelectorAll('article').forEach(a=>a.hidden=b.dataset.filter!=='all'&&a.dataset.group!==b.dataset.filter)});
    </script></body></html>'''
    content=content.replace('CANDIDATE_TEXT',html.escape(json.dumps(current,ensure_ascii=False))).replace('CARDS',''.join(cards))
    atomic_text(output,content)
    return {'status':'HTML_WRITTEN','path':str(output),'images':len(items),'candidate':current,
            'new_captures':0,'avatar_acceptance':False,'network_requests':0,'browser_opened':False}


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--task',type=Path,required=True);p.add_argument('--manifest',type=Path,required=True);p.add_argument('--output',type=Path,required=True)
    a=p.parse_args()
    try:out=render(a.task,read_json(a.manifest),a.output)
    except (ValueError,OSError,KeyError,TypeError) as e:
        print(json.dumps({'status':'HTML_SKIPPED','error':str(e),'next':'Use the existing original images; do not block the original assembly workflow.'},ensure_ascii=False));return 2
    print(json.dumps(out,ensure_ascii=False,indent=2));return 0

if __name__=='__main__':raise SystemExit(main())
