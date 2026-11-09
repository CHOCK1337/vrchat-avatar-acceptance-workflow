"""Bounded, cacheable downloads of public BOOTH product images (stdlib only).

Import :func:`download_product_image`; do not reconstruct an original URL from a
thumbnail. Network operations run in disposable child processes so that DNS,
TLS, response headers and slow-drip bodies share a real wall-clock deadline.
"""
from __future__ import annotations

import hashlib
import http.client
import json
import math
import os
import socket
import subprocess
import sys
import tempfile
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass
from email.utils import parsedate_to_datetime
from pathlib import Path
from typing import Literal


_MAX_BYTES = 8 * 1024 * 1024
_CACHE_BYTES = 64 * 1024 * 1024
_START_INTERVAL = 0.25
_DEFAULT_COOLDOWN = 30.0
_CACHE_LOCK = threading.Lock()
_SAFE_REASONS = {
    "invalid_url", "invalid_options", "original_unavailable", "timeout",
    "cooldown", "network_error", "http_error", "rate_limited", "redirect",
    "unsupported_type", "invalid_image", "too_large", "truncated",
    "invalid_response", "worker_error",
}


class ImageDownloadError(Exception):
    """A safe reason code; messages never contain URLs or response bodies."""

    def __init__(self, code: str, *, original_reason: str | None = None):
        self.code = code if code in _SAFE_REASONS else "worker_error"
        self.original_reason = original_reason
        note = self.code
        if original_reason:
            note = f"original: {original_reason}; thumbnail: {self.code}"
        super().__init__(note)


class _AttemptError(ImageDownloadError):
    def __init__(self, code: str, retry_after: str | None = None):
        super().__init__(code)
        self.retry_after = retry_after


@dataclass(frozen=True)
class ProductImageResult:
    data: bytes
    source: Literal["original", "thumbnail"]
    content_type: str
    elapsed_seconds: float
    note: str
    url: str
    cache_hit: bool
    cache_path: str | None = None


@dataclass(frozen=True)
class _Payload:
    data: bytes
    content_type: str
    retry_after: str | None = None


def _validate_url(url: str) -> None:
    if not isinstance(url, str) or not url or len(url) > 16384:
        raise ImageDownloadError("invalid_url")
    if any(ord(char) < 33 or ord(char) == 127 for char in url) or "\\" in url:
        raise ImageDownloadError("invalid_url")
    try:
        parts = urllib.parse.urlsplit(url)
        valid = (parts.scheme == "https" and parts.hostname == "booth.pximg.net"
                 and parts.port in (None, 443) and not parts.username
                 and not parts.password and "@" not in parts.netloc
                 and not parts.fragment and parts.path.startswith("/"))
    except ValueError:
        valid = False
    if not valid:
        raise ImageDownloadError("invalid_url")


def _remaining(deadline: float) -> float:
    value = deadline - time.monotonic()
    if value <= 0:
        raise _AttemptError("timeout")
    return value


class _Admission:
    """One process-wide limiter, including fallback attempts."""

    def __init__(self):
        self.condition = threading.Condition()
        self.active = 0
        self.next_start = 0.0
        self.cooldown_until = 0.0

    def acquire(self, deadline: float) -> None:
        with self.condition:
            while True:
                remaining = _remaining(deadline)
                now = time.monotonic()
                if self.cooldown_until >= deadline:
                    raise _AttemptError("cooldown")
                wait = max(self.next_start, self.cooldown_until) - now
                if self.active < 2 and wait <= 0:
                    self.active += 1
                    self.next_start = now + _START_INTERVAL
                    return
                self.condition.wait(min(remaining, wait) if wait > 0 else remaining)

    def release(self) -> None:
        with self.condition:
            self.active -= 1
            self.condition.notify_all()

    def cool_down(self, retry_after: str | None, *, required: bool = False) -> None:
        delay = _retry_delay(retry_after)
        if required:
            delay = max(delay, _DEFAULT_COOLDOWN)
        if delay <= 0:
            return
        with self.condition:
            self.cooldown_until = max(self.cooldown_until, time.monotonic() + delay)
            self.condition.notify_all()


_ADMISSION = _Admission()


def _retry_delay(value: str | None) -> float:
    if not value:
        return 0.0
    try:
        delay = float(value)
        if not math.isfinite(delay):
            return _DEFAULT_COOLDOWN
        return max(0.0, delay)
    except (ValueError, TypeError):
        try:
            return max(0.0, parsedate_to_datetime(value).timestamp() - time.time())
        except (ValueError, TypeError, OverflowError):
            return _DEFAULT_COOLDOWN


def _check_image(data: bytes, content_type: str) -> None:
    """Cheap framing checks; this is not a full image decoder."""
    if not content_type.startswith("image/"):
        raise _AttemptError("unsupported_type")
    if not data:
        raise _AttemptError("truncated")
    if content_type in ("image/jpeg", "image/jpg"):
        if not data.startswith(b"\xff\xd8\xff"):
            raise _AttemptError("invalid_image")
        if not data.endswith(b"\xff\xd9"):
            raise _AttemptError("truncated")
    elif content_type == "image/png":
        if not data.startswith(b"\x89PNG\r\n\x1a\n"):
            raise _AttemptError("invalid_image")
        if not data.endswith(b"\x00\x00\x00\x00IEND\xaeB`\x82"):
            raise _AttemptError("truncated")
    elif content_type == "image/webp":
        if len(data) < 12 or data[:4] != b"RIFF" or data[8:12] != b"WEBP":
            raise _AttemptError("invalid_image")
        if int.from_bytes(data[4:8], "little") + 8 != len(data):
            raise _AttemptError("truncated")
    elif content_type == "image/gif":
        if data[:6] not in (b"GIF87a", b"GIF89a"):
            raise _AttemptError("invalid_image")
        if not data.endswith(b";"):
            raise _AttemptError("truncated")


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def _read_response(response, max_bytes: int, deadline: float,
                   accepted_types: tuple[str, ...] | None) -> _Payload:
    content_type = (response.headers.get("Content-Type") or "").split(";", 1)[0].strip().lower()
    retry_after = response.headers.get("Retry-After")
    if not content_type.startswith("image/") or (accepted_types and content_type not in accepted_types):
        raise _AttemptError("unsupported_type", retry_after)
    if (response.headers.get("Content-Encoding") or "identity").lower() != "identity":
        raise _AttemptError("invalid_response", retry_after)
    lengths = response.headers.get_all("Content-Length", [])
    expected = None
    if lengths:
        try:
            if any(not value.strip().isdigit() for value in lengths):
                raise ValueError
            numbers = {int(value.strip()) for value in lengths}
            if len(numbers) != 1:
                raise ValueError
            expected = numbers.pop()
        except ValueError:
            raise _AttemptError("invalid_response", retry_after) from None
        if expected > max_bytes:
            raise _AttemptError("too_large", retry_after)
    chunks = []
    total = 0
    while True:
        remaining = _remaining(deadline)
        # read1 avoids read(n)'s loop of many successful tiny socket reads.
        # The parent process deadline also covers headers and DNS resolution.
        try:
            response.fp.raw._sock.settimeout(remaining)
        except AttributeError:
            pass
        chunk = response.read1(min(64 * 1024, max_bytes + 1 - total))
        _remaining(deadline)
        if not chunk:
            break
        total += len(chunk)
        if total > max_bytes:
            raise _AttemptError("too_large", retry_after)
        chunks.append(chunk)
    if expected is not None and total != expected:
        raise _AttemptError("truncated", retry_after)
    data = b"".join(chunks)
    _check_image(data, content_type)
    return _Payload(data, content_type, retry_after)


def _download_http(url: str, max_bytes: int, timeout: float,
                   accepted_types: tuple[str, ...] | None) -> _Payload:
    _validate_url(url)
    deadline = time.monotonic() + timeout
    # Respect the caller's normal proxy configuration, retain default TLS
    # certificate verification, and never follow a redirect.
    opener = urllib.request.build_opener(urllib.request.ProxyHandler(), _NoRedirect())
    request = urllib.request.Request(url, headers={
        "Referer": "https://booth.pm/", "Accept": "image/*",
        "Accept-Encoding": "identity", "User-Agent": "booth-cli-image/1.0",
    })
    retry_after = None
    try:
        with opener.open(request, timeout=_remaining(deadline)) as response:
            retry_after = response.headers.get("Retry-After")
            if response.status != 200:
                raise _AttemptError("invalid_response", retry_after)
            return _read_response(response, max_bytes, deadline, accepted_types)
    except urllib.error.HTTPError as exc:
        retry_after = exc.headers.get("Retry-After") if exc.headers else None
        status = exc.code
        exc.close()
        code = "rate_limited" if status in (429, 503) else ("redirect" if 300 <= status < 400 else "http_error")
        raise _AttemptError(code, retry_after) from None
    except _AttemptError as exc:
        if exc.retry_after is None:
            exc.retry_after = retry_after
        raise
    except (TimeoutError, socket.timeout):
        raise _AttemptError("timeout", retry_after) from None
    except http.client.IncompleteRead:
        raise _AttemptError("truncated", retry_after) from None
    except urllib.error.URLError as exc:
        code = "timeout" if isinstance(exc.reason, TimeoutError) else "network_error"
        raise _AttemptError(code, retry_after) from None
    except (OSError, http.client.HTTPException):
        raise _AttemptError("network_error", retry_after) from None


def _parent_watch() -> None:
    # The caller deliberately leaves stdin open. Killing a bridge/caller closes
    # the write handle, interrupting an otherwise still-running network request.
    try:
        os.read(sys.stdin.fileno(), 1)
    finally:
        os._exit(2)


def _worker_main() -> int:
    try:
        line = sys.stdin.buffer.readline(32769)
        if len(line) > 32768 or not line.endswith(b"\n"):
            raise _AttemptError("worker_error")
        config = json.loads(line)
        threading.Thread(target=_parent_watch, daemon=True).start()
        payload = _download_http(config["url"], config["max_bytes"], config["timeout"],
                                 tuple(config["accepted_types"]) if config["accepted_types"] else None)
        header = {"ok": True, "content_type": payload.content_type,
                  "retry_after": payload.retry_after}
        data = payload.data
    except ImageDownloadError as exc:
        header = {"ok": False, "code": exc.code, "retry_after": getattr(exc, "retry_after", None)}
        data = b""
    except Exception:
        header = {"ok": False, "code": "worker_error"}
        data = b""
    sys.stdout.buffer.write(json.dumps(header, ensure_ascii=True).encode("ascii") + b"\n" + data)
    sys.stdout.buffer.flush()
    return 0


def _run_worker(url: str, max_bytes: int, deadline: float,
                accepted_types: tuple[str, ...] | None) -> _Payload:
    config = {"url": url, "max_bytes": max_bytes, "timeout": _remaining(deadline),
              "accepted_types": accepted_types}
    flags = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0
    try:
        process = subprocess.Popen([sys.executable, "-u", str(Path(__file__).resolve()), "--image-worker"],
                                   stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                   stderr=subprocess.DEVNULL, creationflags=flags)
    except OSError:
        raise _AttemptError("worker_error") from None
    input_pipe = process.stdin
    try:
        # communicate() normally closes stdin. Retain our handle until the
        # worker is reaped so EOF exclusively indicates parent death/cleanup.
        process.stdin = None
        input_pipe.write(json.dumps(config, ensure_ascii=True).encode("ascii") + b"\n")
        input_pipe.flush()
        try:
            output, _ = process.communicate(timeout=_remaining(deadline))
        except subprocess.TimeoutExpired:
            raise _AttemptError("timeout") from None
        _remaining(deadline)
        if process.returncode != 0 or b"\n" not in output:
            raise _AttemptError("worker_error")
        line, data = output.split(b"\n", 1)
        if len(line) > 4096 or len(data) > max_bytes:
            raise _AttemptError("invalid_response")
        try:
            header = json.loads(line)
            if not header["ok"]:
                raise _AttemptError(header.get("code", "worker_error"), header.get("retry_after"))
            content_type = header["content_type"]
            _check_image(data, content_type)
            if accepted_types and content_type not in accepted_types:
                raise _AttemptError("unsupported_type")
            return _Payload(data, content_type, header.get("retry_after"))
        except (ValueError, KeyError, TypeError):
            raise _AttemptError("worker_error") from None
    except (OSError, BrokenPipeError):
        raise _AttemptError("worker_error") from None
    finally:
        if process.poll() is None:
            process.kill()
        # Also join communicate()'s Windows pipe-reader threads after a timeout.
        process.communicate()
        try:
            input_pipe.close()
        except OSError:
            pass
        if process.stdout:
            process.stdout.close()


def _cache_paths(cache_dir, url: str):
    root = Path(cache_dir).expanduser().resolve()
    key = hashlib.sha256(url.encode("utf-8")).hexdigest()
    return root, root / (key + ".img"), root / (key + ".json")


def _load_cache(cache_dir, url: str, max_bytes: int,
                accepted_types: tuple[str, ...] | None):
    if cache_dir is None:
        return None
    try:
        _, image, metadata = _cache_paths(cache_dir, url)
        with _CACHE_LOCK:
            if image.is_symlink() or metadata.is_symlink() or metadata.stat().st_size > 4096:
                return None
            size = image.stat().st_size
            if not 0 < size <= max_bytes:
                return None
            with metadata.open("rb") as handle:
                info = json.loads(handle.read(4097))
            if info.get("size") != size or (accepted_types and info.get("content_type") not in accepted_types):
                return None
            with image.open("rb") as handle:
                data = handle.read(max_bytes + 1)
            if len(data) != size or hashlib.sha256(data).hexdigest() != info.get("sha256"):
                return None
            _check_image(data, info["content_type"])
            os.utime(image, None)
            return _Payload(data, info["content_type"]), str(image)
    except (OSError, ValueError, KeyError, TypeError, AttributeError, ImageDownloadError):
        return None


def _prune_cache(root: Path, keep: Path) -> None:
    entries = []
    total = 0
    for image in root.glob("*.img"):
        if len(image.stem) != 64 or any(c not in "0123456789abcdef" for c in image.stem):
            continue
        if image.is_symlink():
            continue
        try:
            stat = image.stat()
            metadata = image.with_suffix(".json")
            size = stat.st_size + (metadata.stat().st_size if metadata.exists() else 0)
            total += size
            entries.append((stat.st_mtime_ns, image, metadata, size))
        except OSError:
            continue
    for _, image, metadata, size in sorted(entries):
        if total <= _CACHE_BYTES:
            break
        if image == keep:
            continue
        image.unlink(missing_ok=True)
        metadata.unlink(missing_ok=True)
        total -= size


def _store_cache(cache_dir, url: str, payload: _Payload) -> str | None:
    if cache_dir is None:
        return None
    temporary = []
    try:
        root, image, metadata = _cache_paths(cache_dir, url)
        with _CACHE_LOCK:
            root.mkdir(parents=True, exist_ok=True)
            info = {"size": len(payload.data), "sha256": hashlib.sha256(payload.data).hexdigest(),
                    "content_type": payload.content_type}
            for target, data in ((image, payload.data), (metadata, json.dumps(info).encode("utf-8"))):
                with tempfile.NamedTemporaryFile(dir=root, prefix="image-", suffix=".tmp", delete=False) as handle:
                    temporary.append(Path(handle.name))
                    handle.write(data)
                os.replace(temporary[-1], target)
            _prune_cache(root, image)
            return str(image)
    except OSError:
        return None
    finally:
        for path in temporary:
            try:
                path.unlink(missing_ok=True)
            except OSError:
                pass


def _attempt(url: str, cache_dir, timeout: float, max_bytes: int,
             accepted_types: tuple[str, ...] | None):
    deadline = time.monotonic() + timeout
    cached = _load_cache(cache_dir, url, max_bytes, accepted_types)
    if cached is not None:
        _remaining(deadline)
        payload, path = cached
        return payload, True, path
    _ADMISSION.acquire(deadline)
    try:
        try:
            payload = _run_worker(url, max_bytes, deadline, accepted_types)
        except _AttemptError as exc:
            _ADMISSION.cool_down(exc.retry_after, required=exc.code == "rate_limited")
            raise
        _ADMISSION.cool_down(payload.retry_after)
        return payload, False, _store_cache(cache_dir, url, payload)
    finally:
        _ADMISSION.release()


def download_product_image(original_url: str | None, thumbnail_url: str | None, *,
                           cache_dir=None, original_timeout: float = 12.0,
                           thumbnail_timeout: float = 10.0, max_bytes: int = _MAX_BYTES,
                           accepted_types: tuple[str, ...] | None = None) -> ProductImageResult:
    """Try the exact metadata original URL, then at most one thumbnail attempt.

    Each attempt's timeout includes admission waiting and network operations.
    A thumbnail has its own budget. Rate limits are shared by all callers in
    this Python process. Pass a dedicated cache directory, or None for no disk
    cache. ``accepted_types`` can restrict formats for the consuming decoder.
    """
    started = time.monotonic()
    if (isinstance(max_bytes, bool) or not isinstance(max_bytes, int) or not 1 <= max_bytes <= _MAX_BYTES
            or any(isinstance(value, bool) or not isinstance(value, (int, float))
                   or not math.isfinite(value) or value <= 0
                   for value in (original_timeout, thumbnail_timeout))):
        raise ImageDownloadError("invalid_options")
    if accepted_types is not None:
        if not isinstance(accepted_types, (tuple, list)) or not accepted_types or any(
                not isinstance(value, str) or not value.startswith("image/") for value in accepted_types):
            raise ImageDownloadError("invalid_options")
        accepted_types = tuple(value.lower() for value in accepted_types)
    if not original_url and not thumbnail_url:
        raise ImageDownloadError("invalid_url")
    for url in (original_url, thumbnail_url):
        if url:
            _validate_url(url)
    original_reason = "original_unavailable"
    if original_url:
        try:
            payload, cached, path = _attempt(original_url, cache_dir, original_timeout, max_bytes, accepted_types)
            return ProductImageResult(payload.data, "original", payload.content_type,
                                      time.monotonic() - started, "cache_hit" if cached else "original_downloaded",
                                      original_url, cached, path)
        except _AttemptError as exc:
            original_reason = exc.code
    # Do not repeat the same failing URL under a different source label.
    if not thumbnail_url or thumbnail_url == original_url:
        raise ImageDownloadError(original_reason) from None
    try:
        payload, cached, path = _attempt(thumbnail_url, cache_dir, thumbnail_timeout, max_bytes, accepted_types)
        note = f"original: {original_reason}; thumbnail: {'cache_hit' if cached else 'downloaded'}"
        return ProductImageResult(payload.data, "thumbnail", payload.content_type,
                                  time.monotonic() - started, note, thumbnail_url, cached, path)
    except _AttemptError as exc:
        raise ImageDownloadError(exc.code, original_reason=original_reason) from None


if __name__ == "__main__":
    if sys.argv[1:] != ["--image-worker"]:
        raise SystemExit("Import download_product_image from image_download; no public CLI is provided.")
    raise SystemExit(_worker_main())
