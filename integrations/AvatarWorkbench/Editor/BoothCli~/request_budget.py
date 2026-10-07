"""Cross-process BOOTH admission spacing and bounded query wire budgets.

All processes for one host must use the same local DB. HTTP/cache work happens
outside transactions. Failed storage never silently disables the limiter.
"""
import contextlib
import hashlib
import math
import os
import re
import sqlite3
import time
from pathlib import Path

DB_PATH = Path.home() / ".booth-cli" / "request_budget.sqlite3"
_CONTEXT = None


class BudgetError(Exception):
    pass


def _path():
    return Path(os.environ.get("BOOTH_REQUEST_BUDGET_DB") or DB_PATH).expanduser()


def _connect():
    path = _path()
    conn = None
    try:
        path.parent.mkdir(parents=True, exist_ok=True)
        conn = sqlite3.connect(str(path), timeout=0.5, isolation_level=None)
        conn.execute("CREATE TABLE IF NOT EXISTS admission "
                     "(id INTEGER PRIMARY KEY, epoch REAL, last_mono REAL, "
                     "cooldown_wall REAL, cooldown_mono REAL)")
        conn.execute("INSERT OR IGNORE INTO admission VALUES (1, 0, 0, 0, 0)")
        conn.execute("CREATE TABLE IF NOT EXISTS queries "
                     "(id TEXT PRIMARY KEY, maximum INTEGER, used INTEGER, deadline REAL)")
        return conn
    except (OSError, sqlite3.Error) as exc:
        if conn is not None:
            conn.close()
        raise BudgetError("共享请求预算不可用，请检查缓存目录权限") from exc


def validate_context(value):
    if value is None:
        return None
    if not isinstance(value, dict):
        raise BudgetError("请求预算 context 必须为对象")
    request_id = value.get("request_id")
    maximum = value.get("max_requests")
    deadline = value.get("deadline")
    if not isinstance(request_id, str) or not re.fullmatch(r"[A-Za-z0-9_-]{8,64}", request_id):
        raise BudgetError("request_id 格式错误")
    if isinstance(maximum, bool) or not isinstance(maximum, int) or not 1 <= maximum <= 100:
        raise BudgetError("max_requests 必须为 1-100 的整数")
    if isinstance(deadline, bool) or not isinstance(deadline, (int, float)) or not math.isfinite(deadline):
        raise BudgetError("deadline 必须为有限时间戳")
    if deadline > time.time() + 900:
        raise BudgetError("查询 deadline 不得超过 15 分钟")
    return {"request_id": request_id, "max_requests": maximum, "deadline": float(deadline)}


@contextlib.contextmanager
def query_context(value):
    global _CONTEXT
    previous = _CONTEXT
    _CONTEXT = validate_context(value)
    try:
        yield _CONTEXT
    finally:
        _CONTEXT = previous


def context():
    return _CONTEXT


def _clock_state(row, wall, mono):
    epoch, last, cool_wall, cool_mono = row
    current_epoch = wall - mono
    # Wall-clock adjustments must not shorten a live Retry-After cooldown.
    # A monotonic rollback indicates a reboot; its old monotonic values cannot
    # be reused, so carry the remaining wall-clock cooldown into the new era.
    reboot = bool(last and mono < last)
    correction = bool(epoch and abs(current_epoch - epoch) > 10)
    if reboot:
        last = mono
        cool_mono = mono + max(0, cool_wall - wall)
    elif correction:
        cool_wall = wall + max(0, cool_mono - mono)
    return current_epoch, last, cool_wall, cool_mono, reboot or correction


def wait(interval=1.0, max_wait=60.0):
    if not math.isfinite(interval) or interval < 0:
        raise BudgetError("请求间隔必须为非负有限数")
    started = time.monotonic()
    local_deadline = started + max_wait
    query = context()
    if query and query["deadline"] <= time.time():
        raise BudgetError("查询已超过总截止时间")
    conn = _connect()
    try:
        while True:
            wall, mono = time.time(), time.monotonic()
            remaining = local_deadline - mono
            if query:
                remaining = min(remaining, query["deadline"] - wall)
            if remaining <= 0:
                raise BudgetError("请求排队或冷却超时，请稍后重试")
            try:
                conn.execute("BEGIN IMMEDIATE")
                # Sample after taking the write lock. A waiting process must
                # not mistake another process's later admission for a reboot.
                wall, mono = time.time(), time.monotonic()
                remaining = min(remaining, local_deadline - mono)
                if remaining <= 0:
                    raise BudgetError("请求排队或冷却超时，请稍后重试")
                row = conn.execute("SELECT epoch,last_mono,cooldown_wall,cooldown_mono "
                                   "FROM admission WHERE id=1").fetchone()
                epoch, last, cool_wall, cool_mono, reset = _clock_state(row, wall, mono)
                if query:
                    conn.execute("INSERT OR IGNORE INTO queries VALUES (?, ?, 0, ?)",
                                 (query["request_id"], query["max_requests"], query["deadline"]))
                    # An explicit round-two context can extend a query's cap,
                    # never its original deadline or consumed counter.
                    conn.execute("UPDATE queries SET maximum=MAX(maximum, ?) WHERE id=?",
                                 (query["max_requests"], query["request_id"]))
                    maximum, used, deadline = conn.execute(
                        "SELECT maximum,used,deadline FROM queries WHERE id=?",
                        (query["request_id"],)).fetchone()
                    if deadline <= wall:
                        raise BudgetError("查询已超过总截止时间")
                    if used >= maximum:
                        raise BudgetError("本次查询请求预算已用完，保留已取得的结果")
                    remaining = min(remaining, deadline-wall)
                due = max(last + interval if last else mono, cool_mono)
                if reset:
                    conn.execute("UPDATE admission SET epoch=?,last_mono=?,cooldown_wall=?,cooldown_mono=? WHERE id=1",
                                 (epoch, last, cool_wall, cool_mono))
                    conn.commit()
                elif due <= mono:
                    conn.execute("UPDATE admission SET epoch=?,last_mono=? WHERE id=1", (epoch, mono))
                    if query:
                        conn.execute("UPDATE queries SET used=used+1 WHERE id=?", (query["request_id"],))
                    conn.execute("DELETE FROM queries WHERE deadline < ?", (wall-3600,))
                    conn.commit()
                    return mono
                else:
                    conn.rollback()
                delay = max(0, due - mono)
                if delay >= remaining:
                    raise BudgetError("服务正在冷却或排队超过查询截止时间，请稍后重试")
                time.sleep(min(max(delay, 0.001), 0.2))
            except sqlite3.OperationalError as exc:
                conn.rollback()
                if "locked" not in str(exc).lower() and "busy" not in str(exc).lower():
                    raise BudgetError("共享请求预算数据库错误") from exc
                time.sleep(min(0.02, remaining))
            except BaseException:
                conn.rollback()
                raise
    finally:
        conn.close()


def cooldown(seconds):
    if not math.isfinite(seconds) or seconds < 0:
        raise BudgetError("冷却时间无效")
    conn = _connect()
    try:
        conn.execute("BEGIN IMMEDIATE")
        wall, mono = time.time(), time.monotonic()
        row = conn.execute("SELECT epoch,last_mono,cooldown_wall,cooldown_mono "
                           "FROM admission WHERE id=1").fetchone()
        epoch, last, old_wall, old_mono, _ = _clock_state(row, wall, mono)
        conn.execute("UPDATE admission SET epoch=?,last_mono=?,cooldown_wall=?,cooldown_mono=? WHERE id=1",
                     (epoch, last or mono, max(old_wall, wall+seconds), max(old_mono, mono+seconds)))
        conn.commit()
    except sqlite3.Error as exc:
        conn.rollback()
        raise BudgetError("无法保存共享冷却，请稍后重试") from exc
    finally:
        conn.close()


def retry_allowed(seconds):
    query = context()
    if seconds > 60 or (query and seconds >= query["deadline"] - time.time()):
        raise BudgetError("服务冷却超过本次查询等待时间，请稍后重试")


def statistics():
    query = context()
    if not query:
        return None
    result = {"used": 0, "maximum": query["max_requests"]}
    if not _path().is_file():
        return result
    conn = _connect()
    try:
        row = conn.execute("SELECT used,maximum FROM queries WHERE id=?",
                           (query["request_id"],)).fetchone()
        return {"used": row[0], "maximum": row[1]} if row else result
    finally:
        conn.close()


def semantic_fingerprint():
    base = Path(__file__).resolve().parent
    digest = hashlib.sha256()
    for name in ("booth.py", "smart_search.py", "reverse_search.py", "request_budget.py", "search_evidence.py", "provider_api.py", "search_api.py"):
        path = base / name
        digest.update(name.encode() + b"\0")
        digest.update(path.read_bytes() if path.is_file() else b"missing")
    return digest.hexdigest()
