"""Workbench-only text search planning; no filesystem or credential persistence."""
import re
import urllib.parse


LOCAL_HINT = "本地词典只能转换常见短关键词，无法完整理解这段中文；请在搜索设置启用独立 API，或改用日文/英文关键词。"
SOURCES = ("local", "api", "keyword")
ALIASES = {
    "Milltina": "ミルティナ", "Milfy": "ミルフィ", "Manuka": "マヌカ",
    "Shinano": "しなの", "Rurune": "ルルネ", "Selestia": "セレスティア",
    "Chiffon": "シフォン", "Chocolat": "ショコラ", "Mizuki": "瑞希",
    "Shinra": "森羅", "Kikyo": "桔梗", "Airi": "愛莉", "Lime": "ライム", "Moe": "萌",
}
WORDS = {
    "双马尾": "ツインテール", "雙馬尾": "ツインテール", "单马尾": "ポニーテール", "單馬尾": "ポニーテール",
    "马尾": "ポニーテール", "馬尾": "ポニーテール", "短头发": "ショートヘア", "长头发": "ロングヘア",
    "短发": "ショートヘア", "短髮": "ショートヘア", "长发": "ロングヘア", "長髮": "ロングヘア",
    "头发": "ヘア", "頭髮": "ヘア", "发型": "髪型", "髮型": "髪型",
    "衣服": "衣装", "服装": "衣装", "服裝": "衣装", "连衣裙": "ワンピース", "連衣裙": "ワンピース",
    "短裙": "ミニスカート", "裙子": "スカート", "长裙": "ロングスカート", "長裙": "ロングスカート",
    "猫耳": "猫耳", "貓耳": "猫耳", "兔耳": "うさ耳", "耳朵": "耳", "尾巴": "尻尾",
    "妆容": "メイク", "妝容": "メイク", "化妆": "メイク", "眼妆": "アイメイク", "眼妝": "アイメイク",
    "美瞳": "アイテクスチャ", "瞳孔": "アイテクスチャ", "贴图": "テクスチャ", "貼圖": "テクスチャ",
    "饰品": "アクセサリー", "飾品": "アクセサリー", "道具": "小物", "插件": "ギミック",
    "鞋子": "靴", "高跟鞋": "ハイヒール", "运动鞋": "スニーカー", "靴子": "ブーツ", "袜子": "靴下",
    "丝袜": "ストッキング", "絲襪": "ストッキング", "卫衣": "パーカー", "衛衣": "パーカー",
    "制服": "制服", "泳装": "水着", "泳裝": "水着", "女仆": "メイド", "女僕": "メイド",
    "黑色": "黒", "白色": "白", "粉色": "ピンク", "红色": "赤", "紅色": "赤",
    "蓝色": "青", "藍色": "青", "银色": "銀", "銀色": "銀", "可爱": "かわいい", "可愛": "かわいい",
    "眼镜": "メガネ", "眼鏡": "メガネ", "墨镜": "サングラス", "项链": "ネックレス", "項鏈": "ネックレス",
    "蝴蝶结": "リボン", "蝴蝶結": "リボン", "法线贴图": "ノーマルマップ", "法線貼圖": "ノーマルマップ",
}
FILLERS = ("我想要", "我想找", "我想搜", "帮我找", "幫我找", "适用于", "適用於", "用的", "一个", "一個", "一款", "一套", "搜索", "搜尋", "找", "的")
NOTES = {
    "local": "仅使用本地常见词典转换；不理解完整长句，结果仍需核对卖家说明。",
    "api": "独立 API 已将本次搜索文字转换为关键词；结果与适配范围仍需核对卖家说明。",
    "keyword": "按输入原文搜索，没有进行翻译。",
}


def redact(value, api_key):
    text = str(value or "")
    if api_key:
        # Provider errors sometimes URL-escape the credential. Never surface it.
        for candidate in {str(api_key), urllib.parse.quote(str(api_key), safe=""), urllib.parse.quote_plus(str(api_key))}:
            if candidate:
                text = text.replace(candidate, "[已隐藏密钥]")
    return text


def validate_keywords(values, api_key="", maximum=3):
    if not isinstance(values, list) or not values:
        raise ValueError("搜索方案未返回有效关键词，请调整搜索文字后重试。")
    result = []
    for value in values:
        if not isinstance(value, str):
            raise ValueError("搜索方案中的关键词格式无效。")
        value = value.strip()
        if not value or len(value) > 200 or re.search(r"[\x00-\x1f\x7f]|https?://", value, re.I):
            raise ValueError("搜索方案中的关键词格式无效。")
        if api_key and redact(value, api_key) != value:
            raise ValueError("搜索服务返回了不适合检索的内容，已停止发送关键词。")
        if value not in result:
            result.append(value)
    return result[:maximum]


def plan_object(query, keywords, source, translated):
    return {"version": 1, "original_query": query, "keywords": keywords,
            "source": source, "translated": translated, "note": NOTES[source]}


def restore_plan(value, query):
    if not isinstance(value, dict) or value.get("version") != 1 or value.get("original_query") != query:
        raise ValueError("搜索方案与当前文字不一致，请重新点击搜索。")
    source = value.get("source")
    if source not in SOURCES or not isinstance(value.get("translated"), bool):
        raise ValueError("已保存的搜索方案无效，请重新点击搜索。")
    values = value.get("keywords")
    if not isinstance(values, list) or not 1 <= len(values) <= 3:
        raise ValueError("已保存的搜索方案无效，请重新点击搜索。")
    return plan_object(query, validate_keywords(values), source, value["translated"])


def avatar_tokens(query):
    found = []
    for alias, canonical in ALIASES.items():
        canonical_match = (canonical in query if len(canonical) > 1 else bool(re.search(
            r"(?<![\u3040-\u30ff\u4e00-\u9fff])" + re.escape(canonical) + r"(?![\u3040-\u30ff\u4e00-\u9fff])", query)))
        if (re.search(r"(?<![A-Za-z0-9_])" + re.escape(alias) + r"(?![A-Za-z0-9_])", query, re.I)
                or canonical_match):
            if canonical not in found:
                found.append(canonical)
    return found


def local_plan(query):
    if len(query) > 100 or re.search(r"不要|不是|排除|除了|不想|不能|而是|但[是]?|或者|以及|和|或", query):
        raise ValueError(LOCAL_HINT)
    remaining = query.strip()
    tokens, translated = [], False
    aliases = list(ALIASES.items()) + [(v, v) for v in ALIASES.values()]
    replacements = sorted(list(WORDS.items()) + aliases, key=lambda item: len(item[0]), reverse=True)
    japanese_words = sorted(set(WORDS.values()), key=len, reverse=True)
    while remaining:
        # These separators only separate short keywords; no sentence meaning is inferred.
        separator = re.match(r"[\s,，、+＋:：]+", remaining)
        if separator:
            remaining = remaining[separator.end():]
            continue
        matched = False
        for source, target in replacements:
            if not remaining.casefold().startswith(source.casefold()):
                continue
            if source.isascii() and len(remaining) > len(source) and re.match(r"[A-Za-z0-9_]", remaining[len(source)]):
                continue
            tokens.append(target)
            translated |= source != target or remaining[:len(source)] != target
            remaining = remaining[len(source):]
            matched = True
            break
        if matched:
            continue
        for filler in sorted(FILLERS, key=len, reverse=True):
            if remaining.startswith(filler):
                remaining = remaining[len(filler):]
                matched = True
                break
        if matched:
            continue
        for word in japanese_words:
            if remaining.startswith(word):
                tokens.append(word)
                remaining = remaining[len(word):]
                matched = True
                break
        if matched:
            continue
        english = re.match(r"[A-Za-z][A-Za-z0-9_.-]*", remaining)
        if english:
            tokens.append(english.group())
            remaining = remaining[english.end():]
            continue
        japanese = re.match(r"[\u3040-\u30ff\u4e00-\u9fffー]+", remaining)
        if japanese and re.search(r"[\u3040-\u30ff]", japanese.group()) and len(japanese.group()) <= 35:
            # Mixed Chinese unknown prose is not admitted just because kana appears.
            if re.search(r"[这们说想找帮选比较应该需要适用于头发双马猫妆图贴裤裙衣服饰]", japanese.group()):
                raise ValueError(LOCAL_HINT)
            tokens.append(japanese.group())
            remaining = remaining[japanese.end():]
            continue
        raise ValueError(LOCAL_HINT)
    tokens = list(dict.fromkeys(tokens))
    if not tokens or len(tokens) > 8:
        raise ValueError(LOCAL_HINT)
    return plan_object(query, [" ".join(tokens)], "local", bool(translated))


def api_plan(query, config):
    # Importing these upstream modules uses stdlib only; pykakasi is optional at
    # import and is never invoked by plan_search. Never call ai_backend/.env hooks.
    import smart_search
    import provider_api
    key = config.get("api_key")
    base = config.get("base_url")
    model = config.get("model") or ""
    if not isinstance(key, str) or not key.strip():
        raise ValueError("独立 API 尚未填写密钥，请先打开搜索设置。")
    key = key.strip()
    if not isinstance(base, str) or not base.strip():
        raise ValueError("独立 API 尚未填写 HTTPS 服务地址，请先打开搜索设置。")
    if not isinstance(model, str) or len(model) > 200 or len(key) > 16384:
        raise ValueError("独立 API 配置格式无效。")
    if redact(query, key) != query:
        raise ValueError("搜索文字包含密钥，已停止发送；请只输入想找的素材。")
    try:
        base = provider_api.guard_url(base.strip())
        values, _description_terms, translated = smart_search.plan_search(
            query, base_url=base, api_key=key, model=model.strip(), timeout=20)
        keywords = validate_keywords(values, key)
    except (smart_search.AiError, provider_api.ProviderError) as exc:
        if isinstance(exc, smart_search.AiError) and not exc.code and exc.detail.startswith("方案输出无法解析"):
            raise ValueError("独立 API 返回的关键词方案无效，请更换模型或调整搜索文字。") from None
        message = smart_search.friendly_ai_error(exc).replace("AI_API_KEY", "搜索设置中的 API 密钥").replace("AI_MODEL", "搜索设置中的模型名称")
        raise ValueError(redact(message, key)) from None
    except ValueError:
        raise
    except Exception:
        # Do not echo unknown provider payloads or exception bodies.
        raise ValueError("独立 API 关键词转换失败，请检查搜索设置或稍后重试。") from None
    anchors = avatar_tokens(query)
    if len(anchors) == 1:
        anchor = anchors[0]
        useful = [term for term in keywords if term not in anchors and term.casefold() not in {k.casefold() for k, v in ALIASES.items() if v == anchor}]
        if useful:
            keywords = [term if anchor in term else anchor + " " + term for term in useful]
    return plan_object(query, validate_keywords(keywords, key), "api", bool(translated))


def make_plan(query, translation=None, previous=None):
    if previous is not None:
        return restore_plan(previous, query)
    config = translation if translation is not None else {"mode": "local"}
    if not isinstance(config, dict) or config.get("mode", "local") not in SOURCES:
        raise ValueError("搜索转换方式无效，请检查搜索设置。")
    mode = config.get("mode", "local")
    if mode == "api":
        return api_plan(query, config)
    if mode == "keyword":
        return plan_object(query, validate_keywords([query]), "keyword", False)
    return local_plan(query)
