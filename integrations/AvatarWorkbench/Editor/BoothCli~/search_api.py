"""Pluggable web-search wire contracts shared by CLI and Bot.

Adapters build requests and extract URLs. The callers handle transport; every
URL still passes endpoint and BOOTH-product validation. No SDK is required.
"""
import json
import re
import urllib.parse
from dataclasses import dataclass

try:
    from . import provider_api
except ImportError:
    import provider_api

MAX_RESPONSE_BYTES = 2_000_000


@dataclass(frozen=True)
class SearchRequest:
    provider: str
    url: str
    method: str
    headers: dict
    body: bytes | None


_ADAPTERS = {}
_HOSTS = {"api.exa.ai": "exa", "api.tavily.com": "tavily",
          "api.search.brave.com": "brave"}
_PATHS = {"exa": "/search", "tavily": "/search",
          "brave": "/res/v1/web/search", "searxng": "/search"}


def register_adapter(name, build, extract):
    """Register trusted Python callbacks: build(query, key), extract(response).

    build returns (method, fields, headers), using GET query fields or POST JSON.
    extract returns result URLs. Configuration never imports arbitrary code.
    """
    name = str(name).strip().lower()
    if not re.fullmatch(r"[a-z][a-z0-9_-]*", name) or name == "auto" or name in _ADAPTERS:
        raise ValueError("搜索适配器名称无效或已注册")
    if not callable(build) or not callable(extract):
        raise TypeError("搜索适配器需要请求与响应函数")
    _ADAPTERS[name] = (build, extract)


def _rows(data, path="results", field="url"):
    node = data
    for part in path.split("."):
        node = node.get(part) if isinstance(node, dict) else None
    if not isinstance(node, list):
        return []
    return [row[field] for row in node[:100] if isinstance(row, dict)
            and isinstance(row.get(field), str)]


def _bearer(key):
    return {"Authorization": "Bearer " + key} if key else {}


def _generic(query, key):
    return "POST", {"query": query + " site:booth.pm", "max_results": 8,
                    "include_domains": ["booth.pm"]}, _bearer(key)


def _exa(query, key):
    return "POST", {"query": query, "numResults": 8,
                    "includeDomains": ["booth.pm"]}, {"x-api-key": key}


def _tavily(query, key):
    return "POST", {"query": query, "max_results": 8,
                    "include_domains": ["booth.pm"], "include_answer": False}, _bearer(key)


def _brave(query, key):
    return "GET", {"q": query + " site:booth.pm", "count": 8}, {"X-Subscription-Token": key}


def _searxng(query, key):
    return "GET", {"q": query + " site:booth.pm", "format": "json"}, _bearer(key)


register_adapter("json", _generic, _rows)
register_adapter("exa", _exa, _rows)
register_adapter("tavily", _tavily, _rows)
register_adapter("brave", _brave, lambda data: _rows(data, "web.results"))
register_adapter("searxng", _searxng, _rows)


def prepare(keywords, base_url, api_key="", provider="auto"):
    """Auto selects known public hosts; unknown hosts use the JSON contract.

    Named providers can use alternative gateways. Custom/json URLs are exact
    endpoints; native providers accept their root or full search endpoint.
    """
    url = provider_api.guard_url(base_url)
    name = (provider or "auto").strip().lower()
    if name == "auto":
        name = _HOSTS.get(urllib.parse.urlsplit(url).hostname, "json")
    if name not in _ADAPTERS:
        raise provider_api.ProviderError("未知搜索协议，请选择已注册的 SEARCH_PROVIDER")
    if name in ("exa", "tavily", "brave") and not api_key:
        raise provider_api.ProviderError("所选搜索协议需要 SEARCH_API_KEY")
    suffix = _PATHS.get(name)
    path = urllib.parse.urlsplit(url).path
    if suffix and not path.endswith(suffix):
        # Versioned Brave roots may already contain part of its native route.
        if name == "brave" and path.endswith("/res/v1"):
            url += "/web/search"
        else:
            url += suffix
    url = provider_api.public_url(url)
    method, fields, headers = _ADAPTERS[name][0](str(keywords), api_key)
    method = str(method).upper()
    if method not in ("GET", "POST") or not isinstance(fields, dict) or not isinstance(headers, dict):
        raise provider_api.ProviderError("搜索适配器返回了无效请求")
    if any("\r" in str(value) or "\n" in str(value) for value in headers.values()):
        raise provider_api.ProviderError("搜索认证头无效")
    headers = {"Accept": "application/json", **headers}
    body = None
    if method == "GET":
        url += "?" + urllib.parse.urlencode(fields)
    else:
        headers["Content-Type"] = "application/json"
        body = json.dumps(fields, ensure_ascii=False).encode("utf-8")
    return SearchRequest(name, url, method, headers, body)


def parse_results(request, raw):
    if len(raw) > MAX_RESPONSE_BYTES:
        raise provider_api.ProviderError("搜索响应过大")
    try:
        data = json.loads(raw)
        if not isinstance(data, dict):
            raise ValueError()
        urls = _ADAPTERS[request.provider][1](data)
        if not isinstance(urls, (list, tuple)):
            raise ValueError()
    except (ValueError, TypeError, KeyError):
        raise provider_api.ProviderError("搜索接口返回格式无效") from None
    return item_ids_from_urls(urls[:100])


def item_ids_from_urls(urls):
    """Exact BOOTH product links only; no provider output can relax this check."""
    ids = []
    for url in urls:
        if not isinstance(url, str):
            continue
        try:
            parts = urllib.parse.urlsplit(url)
            host = (parts.hostname or "").lower()
            if (parts.scheme not in ("http", "https") or parts.username or parts.password
                    or parts.port not in (None, 80, 443)
                    or (host != "booth.pm" and not host.endswith(".booth.pm"))):
                continue
            match = re.fullmatch(r"/(?:[a-z]{2}/)?items/(\d+)/?", parts.path)
        except ValueError:
            continue
        if match:
            item_id = int(match.group(1))
            if item_id not in ids:
                ids.append(item_id)
    return ids
