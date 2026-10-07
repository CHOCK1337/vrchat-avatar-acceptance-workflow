"""Avatar Workbench's bounded stdin/stdout adapter for vendored booth-cli.

Only public keyword search, explicit text-to-keyword planning and item metadata
are exposed. No login, purchase, file download, image search or .env loading.
"""
import argparse
import contextlib
import io
import hashlib
import json
import os
from pathlib import Path
import re
import struct
import sys
import time
import urllib.parse
import uuid

# -I isolates Python's environment and user site; import only our sibling files.
sys.path.insert(0, str(Path(__file__).resolve().parent))
import booth
import request_budget
import workbench_query

COMMIT = "ebfa333b9110d5c0f803a7448035b518fe3d2625"
MAX_INPUT = 65536
MAX_DESCRIPTION = 20000


def thumbnail_url(value):
    """Request BOOTH's resized image endpoint, never cache an original image."""
    if not value:
        return None
    p = urllib.parse.urlsplit(str(value))
    if p.scheme != "https" or p.hostname != "booth.pximg.net" or p.port not in (None, 443):
        return None
    path = re.sub(r"^/c/[^/]+/", "/", p.path)
    if not path.startswith("/") or ".." in path.split("/"):
        return None
    if p.path.startswith("/c/320x320_a2/") and "_base_resized." not in path:
        path = re.sub(r"(\.[A-Za-z0-9]+)$", r"_base_resized\1", path)
    # This is BOOTH's real search-card preset. Preserve the server's filename,
    # including _base_resized; stripping it can turn a valid cover into a 403.
    return urllib.parse.urlunsplit(("https", "booth.pximg.net", "/c/300x300_a2_g5" + path, "", ""))


def image_dimensions(data):
    """Read PNG/JPEG dimensions before Unity allocates a texture."""
    if data.startswith(b"\x89PNG\r\n\x1a\n") and len(data) >= 24 and data[12:16] == b"IHDR":
        return struct.unpack(">II", data[16:24])
    if data.startswith(b"\xff\xd8"):
        pos = 2
        while pos + 4 <= len(data):
            if data[pos] != 0xFF:
                break
            while pos < len(data) and data[pos] == 0xFF:
                pos += 1
            if pos >= len(data):
                break
            marker = data[pos]
            pos += 1
            if marker in (0xD8, 0xD9, 0x01) or 0xD0 <= marker <= 0xD7:
                continue
            if pos + 2 > len(data):
                break
            size = struct.unpack(">H", data[pos:pos + 2])[0]
            if size < 2 or pos + size > len(data):
                break
            if marker in (0xC0, 0xC1, 0xC2, 0xC3, 0xC5, 0xC6, 0xC7, 0xC9, 0xCA, 0xCB, 0xCD, 0xCE, 0xCF) and size >= 8:
                height, width = struct.unpack(">HH", data[pos + 3:pos + 7])
                return width, height
            if marker == 0xDA:
                break
            pos += size
    raise ValueError("商品图片没有可读取的 PNG/JPEG 尺寸信息")


def checked_dimensions(data):
    width, height = image_dimensions(data)
    if width <= 0 or height <= 0 or width > 8192 or height > 8192 or width * height > 32 * 1024 * 1024:
        raise ValueError("图片尺寸超出安全显示范围（%s×%s）" % (width, height))
    return width, height


def image_reason(code, original=True):
    name = "原图" if original else "缩略图"
    reasons = {
        "original_unavailable": "商品没有提供原图地址", "timeout": name + "下载超时（最多%s秒）" % (12 if original else 10),
        "cooldown": "图片服务正在冷却，请稍后重试", "rate_limited": "图片服务正在限流，请稍后重试",
        "network_error": name + "网络请求失败", "http_error": name + "服务器未能提供图片",
        "redirect": name + "地址发生重定向，已停止跟随", "unsupported_type": name + "格式不受 Unity 图片预览支持",
        "invalid_image": name + "数据无法识别", "too_large": name + "超过 8 MiB 下载限制",
        "truncated": name + "下载不完整", "invalid_url": name + "地址不在官方图片范围内",
    }
    return reasons.get(code, name + "暂时无法读取")


def download_image_result(helper, original, fallback, image_dir):
    try:
        return helper.download_product_image(original, fallback, cache_dir=image_dir,
            original_timeout=12.0, thumbnail_timeout=10.0, max_bytes=8 * 1024 * 1024,
            accepted_types=("image/jpeg", "image/png"))
    except helper.ImageDownloadError as exc:
        pieces = [image_reason(exc.original_reason)] if exc.original_reason else []
        pieces.append(image_reason(exc.code, original=not bool(exc.original_reason)))
        raise ValueError("；".join(pieces) + "。可稍后重试或打开 BOOTH 商品页。") from None


def product_image(request, root):
    import image_download
    item_id = str(request.get("item_id") or "")
    if not re.fullmatch(r"[0-9]{1,12}", item_id):
        raise ValueError("商品 ID 无效")
    original, fallback = None, thumbnail_url(request.get("thumbnail_url"))
    metadata_note = ""
    previous = booth.MAX_ATTEMPTS, booth.HTTP_TIMEOUT, booth.FAST_FAIL
    try:
        # A missing metadata cache must not consume the full original/thumbnail
        # download budget with repeated 30-second metadata requests.
        booth.MAX_ATTEMPTS, booth.HTTP_TIMEOUT, booth.FAST_FAIL = 1, 3.0, True
        raw = booth.fetch_item(item_id, "ja")
        if not isinstance(raw, dict):
            raise booth.BoothError("商品原图资料格式无效")
        if raw.get("is_adult"):
            raise ValueError("此商品不在工作台的全年龄范围内，请在 BOOTH 网站查看")
        first = next(iter(raw.get("images") or []), {}) or {}
        original = first.get("original") or None  # Exact server value, never guessed from a filename.
        fallback = fallback or thumbnail_url(first.get("resized"))
    except booth.BoothError as exc:
        message = str(exc)
        if "年龄确认" in message:
            raise
        metadata_note = "商品原图资料请求超时" if "timed out" in message.lower() or "超时" in message else "暂时无法取得商品原图地址"
    except json.JSONDecodeError:
        metadata_note = "商品原图资料暂时无法读取"
    finally:
        booth.MAX_ATTEMPTS, booth.HTTP_TIMEOUT, booth.FAST_FAIL = previous
    image_dir = root / "product-images"
    result = download_image_result(image_download, original, fallback, image_dir)
    elapsed = float(result.elapsed_seconds)
    if result.source == "original":
        note = "已从本机缓存读取商品原图。" if result.cache_hit else "已读取商品页提供的原图地址，未猜测或改写 URL。"
    else:
        reason = re.search(r"original: ([a-z_]+)", str(result.note or ""))
        note = image_reason(reason.group(1) if reason else "original_unavailable") + "；已回退压缩缩略图。"
    try:
        width, height = checked_dimensions(result.data)
    except ValueError as exc:
        if result.source != "original" or not fallback:
            raise
        reason = str(exc)
        result = download_image_result(image_download, None, fallback, image_dir)
        elapsed += float(result.elapsed_seconds)
        width, height = checked_dimensions(result.data)
        note = reason + "；已回退压缩缩略图。"
    if metadata_note and result.source == "thumbnail":
        note = metadata_note + "；已显示压缩缩略图。"
    path = Path(result.cache_path).resolve() if result.cache_path else None
    if path is None or not path.is_relative_to(image_dir.resolve()) or not path.is_file():
        image_dir.mkdir(parents=True, exist_ok=True)
        path = image_dir / (hashlib.sha256((str(result.url) + str(result.source)).encode()).hexdigest() + ".img")
        temporary = path.with_suffix(".tmp-" + uuid.uuid4().hex)
        try:
            temporary.write_bytes(result.data)
            temporary.replace(path)
        finally:
            if temporary.exists():
                temporary.unlink()
    return {"path": str(path), "source": result.source, "note": note,
        "elapsed_seconds": elapsed, "content_type": result.content_type,
        "cache_hit": bool(result.cache_hit), "width": width, "height": height}


def run(request):
    if sys.version_info < (3, 10):
        raise ValueError("需要现有 Python 3.10 或更新版本；工作台不会自动安装")
    root = Path(str(request.get("cache_root", ""))).resolve()
    if not root.is_absolute() or tuple(p.casefold() for p in root.parts[-3:]) != ("library", "avatarworkbench", "booth"):
        raise ValueError("缓存目录必须位于当前工程 Library/AvatarWorkbench/Booth")
    root.mkdir(parents=True, exist_ok=True)
    booth.CACHE_PATH = root / "http-cache.sqlite3"
    request_budget.DB_PATH = root / "request-budget.sqlite3"
    os.environ["BOOTH_REQUEST_BUDGET_DB"] = str(request_budget.DB_PATH)
    action = request.get("action")
    ctx = {"request_id": uuid.uuid4().hex, "max_requests": 6, "deadline": time.time() + 65}
    with request_budget.query_context(ctx):
        if action == "search":
            query = str(request.get("query") or "").strip()
            if not query or len(query) > 200:
                raise ValueError("请输入 1–200 字的商品关键词")
            config = request.get("translation")
            secret = config.get("api_key") if isinstance(config, dict) else ""
            secret = secret.strip() if isinstance(secret, str) else ""
            if secret and workbench_query.redact(query, secret) != query:
                raise ValueError("搜索文字包含密钥，已停止发送；请只输入想找的素材。")
            category = str(request.get("category") or "")
            if category and category not in booth.CATEGORY_HINTS:
                raise ValueError("不支持的商品分类")
            sort = str(request.get("sort") or "new")
            if sort not in booth.SORTS:
                raise ValueError("不支持的排序方式")
            page = int(request.get("page", 1))
            if not 1 <= page <= 100:
                raise ValueError("页码超出范围")
            max_price = int(request.get("max_price", 0))
            if not 0 <= max_price <= 10000000:
                raise ValueError("价格上限超出范围")
            plan = workbench_query.make_plan(query, request.get("translation"), request.get("query_plan"))
            plan["keywords"] = workbench_query.validate_keywords(plan["keywords"], secret)
            batches, urls = [], []
            for keyword in plan["keywords"]:
                args = argparse.Namespace(query=[keyword], category=category or None,
                    event=None, lang="ja", sort=sort, type="digital", adult="exclude",
                    in_stock=False, min_price=None, max_price=max_price or None,
                    tag=[], vrc=True, or_word=[], exclude=[], page=page, pages=1,
                    limit=24, json=True)
                output = io.StringIO()
                with contextlib.redirect_stdout(output):
                    booth.cmd_search(args)
                batch = json.loads(output.getvalue())
                batches.append(batch)
                urls.append(booth.build_search_url(args))
            # Each keyword contributes in order. Do not fill all 24 slots from
            # the first query and silently discard every later query's results.
            items, seen = [], set()
            for index in range(24):
                for batch in batches:
                    if index >= len(batch.get("items") or []):
                        continue
                    item = batch["items"][index]
                    if item.get("is_adult") or item.get("id") in seen:
                        continue
                    seen.add(item.get("id"))
                    items.append(item)
                if len(items) >= 24:
                    break
            notes = list(dict.fromkeys(x.get("sort_note") for x in batches if x.get("sort_note")))
            if len(batches) > 1:
                notes.append("按各关键词结果交错合并，未估算重复商品的全站总数。")
            result = {"query": query, "page": page, "pages_fetched": sum(x.get("pages_fetched", 1) for x in batches),
                "total": batches[0].get("total") if len(batches) == 1 else None,
                "has_next": any(x.get("has_next") for x in batches), "sort_note": " ".join(notes) or None,
                "items": items[:24], "query_plan": plan, "source_urls": urls}
            result["count"] = len(result["items"])
            for item in result["items"]:
                item["image"] = thumbnail_url(item.get("image"))
                item["reference_only"] = True
                item["compatibility_status"] = "unverified"
            result["source_url"] = urls[0]
            result["currency"] = "JPY"
            result["keyword_search_only"] = True
            result["filters"] = {"type": "digital", "adult": "exclude", "tag": "VRChat"}
        elif action == "image":
            result = product_image(request, root)
        elif action == "detail":
            item_id = str(request.get("item_id") or "")
            if not re.fullmatch(r"[0-9]{1,12}", item_id):
                raise ValueError("商品 ID 无效")
            raw = booth.fetch_item(item_id, "ja")
            if raw.get("is_adult"):
                raise ValueError("此商品不在工作台的全年龄搜索范围内，请在 BOOTH 网站查看")
            result = booth.trim_item(raw, desc_len=MAX_DESCRIPTION)
            result["description_truncated"] = len(booth.clean_text(raw.get("description"))) > MAX_DESCRIPTION
            result["currency"] = "JPY"
            result["reference_only"] = True
            result["compatibility_status"] = "unverified"
            first_image = next(iter(raw.get("images") or []), {}) or {}
            result["thumbnail"] = thumbnail_url(first_image.get("resized") or first_image.get("original"))
        else:
            raise ValueError("工作台只提供 search、detail 与 image 操作")
        return {"ok": True, "action": action, "data": result, "error": None,
                "upstream_commit": COMMIT, "budget": request_budget.statistics()}


def execute(request):
    """The only public response boundary; credentials never enter its output."""
    config = request.get("translation") if isinstance(request, dict) else None
    key = config.get("api_key") if isinstance(config, dict) else ""
    key = key.strip() if isinstance(key, str) else ""
    try:
        result = run(request)
        # Defense in depth: all returned strings are scrubbed before crossing
        # the stdout boundary, including unexpected remote metadata or errors.
        def clean(value):
            if isinstance(value, dict):
                return {k: clean(v) for k, v in value.items()}
            if isinstance(value, list):
                return [clean(v) for v in value]
            return workbench_query.redact(value, key) if isinstance(value, str) else value
        return clean(result)
    except Exception as exc:
        return {"ok": False, "data": None, "error": workbench_query.redact(str(exc), key)[:1500]}


def main():
    try:
        source = sys.stdin.buffer.read(MAX_INPUT + 1)
        if len(source) > MAX_INPUT:
            raise ValueError("请求过大")
        request = json.loads(source.decode("utf-8-sig"))
        if not isinstance(request, dict):
            raise ValueError("请求必须是 JSON 对象")
        result = execute(request)
    except Exception as exc:
        result = {"ok": False, "data": None, "error": str(exc)[:1500]}
    sys.stdout.buffer.write(json.dumps(result, ensure_ascii=False, separators=(",", ":")).encode("utf-8"))
    sys.stdout.buffer.write(b"\n")


if __name__ == "__main__":
    main()
