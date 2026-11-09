"""Small deterministic evidence/terminology helpers; no framework or AI calls."""
import hashlib
import json
import re
import unicodedata

NEGATIVE = re.compile(r"対応して(?:いません|おりません)|非対応|未対応|not\s+(?:compatible|supported)|不(?:兼容|支持)", re.I)
NEGATED_REQUEST = re.compile(r"不要|不想|不需要|不找|排除|别(?:给|要|找)?|除外|不要な|いらない|without|exclude|not\s", re.I)


def positive_seeds(query, seeds):
    query = unicodedata.normalize("NFKC", str(query or "")).casefold()
    clauses = re.split(r"[，,。;；\n]|但(?:是)?|而是|但是", query)
    found = []
    for seed in seeds:
        for clause in clauses:
            at = clause.find(seed.casefold())
            if at < 0:
                continue
            prefix = clause[:at]
            suffix = clause[at + len(seed):]
            if NEGATED_REQUEST.search(prefix) or re.match(r"\s*(?:不要|不需要|不要な|いらない)", suffix):
                continue
            found.append(seed)
            break
    return found


def excerpt(description, terms=(), max_chars=900):
    text = str(description or "")
    windows = []
    for term in terms:
        for match in list(re.finditer(re.escape(str(term)), text, re.I))[:2]:
            windows.append((max(0, match.start()-100), min(len(text), match.end()+180)))
    if not windows:
        windows = [(0, min(len(text), 600))]
        if len(text) > 600:
            windows.append((max(600, len(text)-250), len(text)))
    pieces = []
    previous_end = -1
    for start, end in sorted(windows):
        start = max(start, previous_end)
        if start < end:
            pieces.append(text[start:end])
            previous_end = end
    return "\n…\n".join(pieces)[:max_chars]


def source_fields(item):
    tags = [t.get("name", "") if isinstance(t, dict) else str(t)
            for t in item.get("tags") or []]
    return {"name": str(item.get("name") or ""),
            "category": str(item.get("category") or ""),
            "tags": " / ".join(tags),
            "description": str(item.get("_desc") or "")}


def candidate_lines(entries, terms=()):
    result = []
    for number, item in enumerate(entries, 1):
        fields = source_fields(item)
        payload = dict(fields, item_id=str(item["id"]),
                       detail_status=item.get("detail_status", "unknown"))
        payload["description"] = excerpt(fields["description"], terms)
        payload["source_hash"] = hashlib.sha256(json.dumps(fields, sort_keys=True,
                                        ensure_ascii=False).encode()).hexdigest()
        result.append(f"{number}. " + json.dumps(payload, ensure_ascii=False))
    return result


def grounded_evaluation(data, entries, required_terms=()):
    """Keep only cited, available evidence. Relatedness remains a model judgement.

    This validates provenance and negative constraints; it does not turn literal
    term mentions into a guarantee of compatibility.
    """
    claims = data.get("evidence")
    claims = claims if isinstance(claims, list) else []
    by_id = {str(item["id"]): (i, item) for i, item in enumerate(entries, 1)}
    accepted = set()
    blocked = set()
    for claim in claims[:24]:
        if not isinstance(claim, dict):
            continue
        pair = by_id.get(str(claim.get("item_id") or ""))
        if not pair:
            continue
        number, item = pair
        fields = source_fields(item)
        quote = str(claim.get("quote") or "").strip()
        field = claim.get("field")
        state = claim.get("status")
        if field not in fields or not quote or quote not in fields[field]:
            continue
        if state == "unsupported":
            item["relevance_status"] = "unsupported"
            item["relevance_evidence"] = {"field": field, "quote": quote[:240]}
            blocked.add(str(number))
            accepted.discard(str(number))
            continue
        if state != "related" or str(number) in blocked:
            continue
        if required_terms:
            if field != "description" or item.get("detail_status") != "available":
                continue
            statements = [s for s in re.split(r"[。！？\n]", fields["description"])
                          if any(str(term).casefold() in s.casefold() for term in required_terms)]
            if any(NEGATIVE.search(statement) for statement in statements):
                item["relevance_status"] = "unsupported"
                item["relevance_evidence"] = {"field": "description", "quote": next(
                    statement for statement in statements if NEGATIVE.search(statement))[:240]}
                blocked.add(str(number))
                accepted.discard(str(number))
                continue
            if not any(str(term).casefold() in quote.casefold() for term in required_terms):
                continue
        if NEGATIVE.search(quote):
            item["relevance_status"] = "unsupported"
            item["relevance_evidence"] = {"field": field, "quote": quote[:240]}
            blocked.add(str(number))
            accepted.discard(str(number))
            continue
        item["relevance_status"] = "related"
        item["relevance_evidence"] = {"field": field, "quote": quote[:240]}
        accepted.add(str(number))
    hits = [str(hit) for hit in data.get("hits") or [] if str(hit) in accepted]
    for item in entries:
        item.setdefault("relevance_status", "unknown")
    result = dict(data, hits=list(dict.fromkeys(hits))[:6])
    if result.get("verdict") == "ok" and len(result["hits"]) < 3:
        result.update(verdict="retry", reason="带来源的相关证据不足三条，未能确认")
    return result


def evidence_label(item):
    state = item.get("relevance_status")
    if state not in ("related", "unsupported", "unknown"):
        return ""
    labels = {"related": "相关候选", "unsupported": "评估提示不满足要求", "unknown": "相关性未核实"}
    evidence = item.get("relevance_evidence") or {}
    fields = {"name": "商品名", "category": "品类", "tags": "标签", "description": "商品说明"}
    quote = " ".join(str(evidence.get("quote") or "").split())[:160]
    suffix = f" · {fields.get(evidence.get('field'), '来源')}原文：{quote}" if quote else ""
    return labels[state] + suffix


def promote_evidence(entries):
    order = {"related": 0, "unknown": 1, "unsupported": 2}
    return sorted(entries, key=lambda item: order.get(item.get("relevance_status"), 1))


def structured_options(model, *, evaluation=False):
    """Known GLM text-model controls; leave other providers/models unchanged.

    GLM 5.3 cannot disable thinking. Use its documented low effort and enough
    output headroom so reasoning cannot consume the whole JSON allowance.
    """
    model = str(model or "").lower()
    if re.fullmatch(r"glm-5\.3(?:-flash|-[a-z0-9]+)?", model):
        return {"thinking": {"type": "enabled"}, "reasoning_effort": "low",
                "response_format": {"type": "json_object"},
                "max_tokens": 4096 if evaluation else 2000}
    if model in {"glm-5.2", "glm-5.1", "glm-5", "glm-4.7", "glm-4.7-flash",
                 "glm-4.7-flashx", "glm-4.6", "glm-4.5", "glm-4.5-air",
                 "glm-4.5-x", "glm-4.5-airx", "glm-4.5-flash"}:
        return {"thinking": {"type": "disabled"},
                "response_format": {"type": "json_object"}}
    return {}
