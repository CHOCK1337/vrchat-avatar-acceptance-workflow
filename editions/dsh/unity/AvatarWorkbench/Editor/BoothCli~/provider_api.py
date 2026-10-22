"""Provider-neutral text requests, model discovery and verified image admission.

No keys, model catalogs or probe images are persisted. Capability failures keep
image input closed; only a random synthetic image is used before admission.
"""
import base64
import hashlib
import ipaddress
import json
import re
import secrets
import socket
import struct
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
import zlib

_CACHE = {}
_LOCK = threading.RLock()


class ProviderError(RuntimeError):
    def __init__(self, message, *, code=None, image_rejected=False, retry_payload=None):
        super().__init__(message)
        self.code = code
        self.image_rejected = image_rejected
        self.retry_payload = retry_payload


def guard_url(url):
    try:
        p = urllib.parse.urlsplit(str(url).strip())
        host = (p.hostname or "").lower()
        if p.scheme != "https" or not host or p.username or p.password or p.query or p.fragment:
            raise ValueError()
        if host == "localhost" or host.endswith((".local", ".internal")):
            raise ValueError()
        try:
            if not ipaddress.ip_address(host).is_global:
                raise ValueError()
        except ValueError:
            # A literal IP must be public; normal DNS names are accepted.
            if ":" in host or re.fullmatch(r"[0-9.]+", host):
                raise ProviderError("API URL 不得指向私网或保留地址")
    except (ValueError, TypeError):
        raise ProviderError("API URL 必须是无凭据、查询参数的 HTTPS 端点")
    if host == "localhost" or host.endswith((".local", ".internal")):
        raise ProviderError("API URL 不得指向本机或私网")
    return urllib.parse.urlunsplit((p.scheme, p.netloc, p.path.rstrip("/"), "", ""))


def endpoint(base_url, model=""):
    url = guard_url(base_url)
    p = urllib.parse.urlsplit(url)
    path = p.path
    if path.endswith("/messages") or p.hostname == "api.anthropic.com":
        root = url[:-9] if path.endswith("/messages") else url
        if not urllib.parse.urlsplit(root).path:
            root += "/v1"
        return "anthropic", root, root + "/messages", model
    if (("v1beta" in path and "/openai" not in path) or path.endswith(":generateContent")
            or (p.hostname == "generativelanguage.googleapis.com" and "/openai" not in path)):
        match = re.search(r"/models/([^/]*):generateContent$", path)
        if match:
            root = url[:url.index("/models/")]
            model = model or urllib.parse.unquote(match.group(1))
        else:
            root = url
        if not urllib.parse.urlsplit(root).path:
            root += "/v1beta"
        return "gemini", root, root + "/models/" + urllib.parse.quote(model, safe="-._") + ":generateContent", model
    root = url[:-17] if path.endswith("/chat/completions") else url
    return "openai", root, root + "/chat/completions", model


def headers(base_url, api_key, session_id=""):
    protocol, _, _, _ = endpoint(base_url)
    result = {"Content-Type": "application/json", "User-Agent": "booth-provider/1"}
    if protocol == "anthropic":
        result.update({"x-api-key": api_key, "anthropic-version": "2023-06-01"})
    elif protocol == "gemini":
        result["x-goog-api-key"] = api_key
    else:
        result["Authorization"] = "Bearer " + api_key
    host = urllib.parse.urlsplit(base_url).hostname or ""
    if session_id or host == "opencode.ai" or host.endswith(".opencode.ai"):
        result["x-opencode-session"] = session_id or "booth-" + secrets.token_hex(8)
    return result


def _parts(content, protocol):
    if isinstance(content, str):
        return [{"text": content}] if protocol == "gemini" else [{"type": "text", "text": content}]
    result = []
    for part in content or []:
        if part.get("type") == "text":
            result.append({"text": part["text"]} if protocol == "gemini" else dict(part))
        elif part.get("type") == "image_url":
            image = part["image_url"]["url"]
            match = re.fullmatch(r"data:([^;]+);base64,(.+)", image, re.S)
            if not match:
                raise ProviderError("原生多模态接口只接收已下载的图片数据")
            mime, data = match.groups()
            if protocol == "gemini":
                result.append({"inlineData": {"mimeType": mime, "data": data}})
            else:
                result.append({"type": "image", "source": {"type": "base64", "media_type": mime, "data": data}})
    return result


def prepare(base_url, payload, api_key, session_id=""):
    protocol, _, url, _ = endpoint(base_url, payload.get("model", ""))
    wire = dict(payload)
    if protocol == "anthropic":
        wire = {"model": payload["model"], "max_tokens": payload.get("max_tokens", 2000),
                "messages": [{"role": m["role"], "content": _parts(m["content"], protocol)}
                             for m in payload["messages"] if m["role"] != "system"]}
        system = [m["content"] for m in payload["messages"] if m["role"] == "system"]
        if system:
            wire["system"] = "\n".join(system)
        if "temperature" in payload:
            wire["temperature"] = payload["temperature"]
    elif protocol == "gemini":
        wire = {"contents": [{"role": "model" if m["role"] == "assistant" else "user",
                             "parts": _parts(m["content"], protocol)}
                            for m in payload["messages"] if m["role"] != "system"],
                "generationConfig": {"maxOutputTokens": payload.get("max_tokens", 2000)}}
        system = [m["content"] for m in payload["messages"] if m["role"] == "system"]
        if system:
            wire["systemInstruction"] = {"parts": [{"text": "\n".join(system)}]}
        if "temperature" in payload:
            wire["generationConfig"]["temperature"] = payload["temperature"]
        if payload.get("response_format", {}).get("type") == "json_object":
            wire["generationConfig"]["responseMimeType"] = "application/json"
    return url, wire, headers(base_url, api_key, session_id)


def content(data):
    if data.get("choices"):
        value = data["choices"][0].get("message") or {}
        text = value.get("content") or value.get("reasoning_content") or ""
        if isinstance(text, list):
            return "".join(p.get("text", "") for p in text if isinstance(p, dict))
        return str(text)
    if isinstance(data.get("content"), list):
        return "".join(p.get("text", "") for p in data["content"] if p.get("type") == "text")
    candidates = data.get("candidates") or []
    if candidates:
        return "".join(p.get("text", "") for p in candidates[0].get("content", {}).get("parts", [])
                       if not p.get("thought"))
    raise ProviderError("AI 接口没有返回可读取的文本；请检查端点协议")


def compatible_retry(payload, code, error):
    """Narrow OpenAI compatibility fallback, only for an explicitly rejected option."""
    if code not in (400, 422):
        return None
    error = str(error).lower()
    if not any(word in error for word in ("unsupported", "not supported", "not allowed", "unknown parameter")):
        return None
    result = dict(payload)
    if "max_tokens" in error and "max_completion_tokens" in error and "max_tokens" in result:
        result["max_completion_tokens"] = result.pop("max_tokens")
    elif "temperature" in error and "temperature" in result:
        result.pop("temperature")
    elif "response_format" in error and "response_format" in result:
        result.pop("response_format")
    else:
        return None
    return result


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


_OPENER = urllib.request.build_opener(_NoRedirect)


def request_json(url, request_headers, payload=None, timeout=15):
    guard_url(url)
    body = json.dumps(payload, ensure_ascii=False).encode() if payload is not None else None
    req = urllib.request.Request(url, data=body, headers=request_headers,
                                 method="POST" if payload is not None else "GET")
    try:
        with _OPENER.open(req, timeout=timeout) as response:
            raw = response.read(2_000_001)
            if len(raw) > 2_000_000:
                raise ValueError()
            data = json.loads(raw.decode("utf-8"))
        if not isinstance(data, dict):
            raise ValueError()
        return data
    except urllib.error.HTTPError as exc:
        message = exc.read(8192).decode("utf-8", "replace").lower()
        rejected = exc.code in (400, 415, 422) and any(
            t in message for t in ("image", "vision", "multimodal", "图片", "识图"))
        raise ProviderError("API 请求被拒绝（HTTP %s）" % exc.code,
                            code=exc.code, image_rejected=rejected,
                            retry_payload=compatible_retry(payload or {}, exc.code, message)) from None
    except (OSError, ValueError, urllib.error.URLError):
        raise ProviderError("API 检测暂不可用，请检查端点、权限或网络") from None


def _key(base_url, api_key, suffix):
    # In-memory, credential-scoped cache; no key or key hash is persisted.
    return (guard_url(base_url), hashlib.sha256(api_key.encode()).digest(), suffix)


def catalog(base_url, api_key, timeout=15):
    key = _key(base_url, api_key, "models")
    with _LOCK:
        cached = _CACHE.get(key)
        if cached and cached[0] > time.monotonic():
            return cached[1]
        protocol, root, _, _ = endpoint(base_url)
        data = request_json(root + "/models", headers(base_url, api_key), timeout=timeout)
        raw = data.get("models" if protocol == "gemini" else "data") or []
        models = []
        for item in raw:
            if not isinstance(item, dict):
                continue
            name = str(item.get("id") or item.get("name") or "")
            if name.startswith("models/"):
                name = name[7:]
            if not name or any(t in name.lower() for t in ("embedding", "embed-", "whisper", "tts-", "rerank", "dall-e")):
                continue
            methods = item.get("supportedGenerationMethods")
            if methods is not None and "generateContent" not in methods:
                continue
            models.append(dict(item, id=name))
        if not models:
            raise ProviderError("接口没有可用的文本模型，请填写 AI_MODEL")
        _CACHE[key] = (time.monotonic()+600, models)
        return models


def resolve_model(base_url, api_key, model="", timeout=15):
    _, _, _, embedded = endpoint(base_url, model)
    if embedded and embedded != "auto":
        return embedded
    try:
        models = catalog(base_url, api_key, timeout=timeout)
    except ProviderError:
        raise ProviderError("无法自动发现模型；请检查 API/URL，或补填 AI_MODEL") from None
    selected = next((m for m in models if m.get("is_default")), None)
    if selected is None:
        selected = next((m for m in models if declared_vision(m) is True), models[0])
    return selected["id"]


def declared_vision(item):
    architecture = item.get("architecture")
    architecture = architecture if isinstance(architecture, dict) else {}
    values = item.get("input_modalities") or architecture.get("input_modalities")
    if isinstance(values, list):
        return any(x in values for x in ("image", "vision"))
    caps = item.get("capabilities")
    caps = caps if isinstance(caps, dict) else {}
    for field in ("vision", "image_input", "supports_vision"):
        value = caps.get(field, item.get(field))
        if isinstance(value, dict):
            value = value.get("supported")
        if isinstance(value, bool):
            return value
    return None


def _probe_image():
    colors = {"red": (255, 0, 0), "green": (0, 255, 0), "blue": (0, 0, 255),
              "yellow": (255, 255, 0), "magenta": (255, 0, 255), "cyan": (0, 255, 255)}
    expected = [secrets.choice(list(colors)) for _ in range(8)]
    rows = b"".join(b"\0" + b"".join(bytes(colors[expected[(y//32)*4+x//32]])
                                    for x in range(128)) for y in range(64))
    def chunk(kind, value):
        return struct.pack(">I", len(value)) + kind + value + struct.pack(">I", zlib.crc32(kind+value)&0xffffffff)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB",128,64,8,2,0,0,0)) + chunk(b"IDAT",zlib.compress(rows)) + chunk(b"IEND",b"")
    return "data:image/png;base64," + base64.b64encode(png).decode(), expected


def image_capability(base_url, api_key, model="", timeout=15):
    model = resolve_model(base_url, api_key, model, timeout=timeout)
    key = _key(base_url, api_key, "vision:"+model)
    with _LOCK:
        cached = _CACHE.get(key)
        if cached and cached[0] > time.monotonic():
            return dict(cached[1])
        state, reason = "unknown", "无法确认模型支持图片输入"
        try:
            try:
                item = next((m for m in catalog(base_url, api_key, timeout=timeout) if m["id"]==model), {})
            except ProviderError:
                item = {}
            if declared_vision(item) is False:
                state, reason = "unsupported", "接口声明该模型仅支持文字，不支持图片输入"
            else:
                image, expected = _probe_image()
                payload = {"model": model, "messages": [{"role":"user","content":[
                    {"type":"text","text":"Inspect this image of 8 solid-color tiles, 4 columns and 2 rows. Return ONLY JSON {\"colors\":[8 lowercase color names]} in row-major order. Names: red, green, blue, yellow, magenta, cyan."},
                    {"type":"image_url","image_url":{"url":image}}]}], "max_tokens":2000}
                try:
                    from .search_evidence import structured_options
                except ImportError:
                    from search_evidence import structured_options
                payload.update(structured_options(model))
                url, wire, auth = prepare(base_url, payload, api_key)
                for attempt in range(3):
                    try:
                        text = content(request_json(url, auth, wire, timeout=timeout))
                        break
                    except ProviderError as exc:
                        if exc.retry_payload is None or attempt == 2:
                            raise
                        wire = exc.retry_payload
                match = re.search(r"\{.*\}", text, re.S)
                actual = json.loads(match.group()).get("colors") if match else None
                if isinstance(actual, list) and [str(v).lower().strip() for v in actual] == expected:
                    state, reason = "supported", "随机合成图片能力检测通过"
                else:
                    reason = "图片检测未通过，当前只开放文字搜索"
        except ProviderError as exc:
            if exc.image_rejected:
                state, reason = "unsupported", "该模型拒绝图片输入，仅支持文字搜索"
            else:
                reason = "模型能力检测暂不可用，当前只开放文字搜索"
        except (ValueError, TypeError, AttributeError):
            reason = "模型未返回有效图片检测结果，当前只开放文字搜索"
        result = {"model":model, "state":state, "reason":reason}
        _CACHE[key] = (time.monotonic()+(3600 if state!="unknown" else 60), result)
        return dict(result)


def image_notice(result):
    return "图片功能未启用：模型「%s」%s。请使用文字搜索，或接入支持图片的多模态 API 并重新检测。" % (result.get("model") or "未配置", result["reason"])


def reject_image(base_url, api_key, model):
    """A real image rejection invalidates a previously successful probe."""
    result = {"model": model, "state": "unsupported", "reason": "该模型拒绝图片输入，仅支持文字搜索"}
    with _LOCK:
        _CACHE[_key(base_url, api_key, "vision:"+model)] = (time.monotonic()+3600, result)
    return result


def public_url(base_url):
    url = guard_url(base_url)
    parts = urllib.parse.urlsplit(url)
    try:
        addresses = socket.getaddrinfo(parts.hostname, parts.port or 443, proto=socket.IPPROTO_TCP)
        if not addresses or any(not ipaddress.ip_address(info[4][0]).is_global for info in addresses):
            raise ValueError()
    except (OSError, ValueError):
        raise ProviderError("检索端点必须解析到公网地址") from None
    return url


def exa_url(base_url):
    url = public_url(base_url)
    parts = urllib.parse.urlsplit(url)
    return url if parts.path.endswith("/search") else url + "/search"
