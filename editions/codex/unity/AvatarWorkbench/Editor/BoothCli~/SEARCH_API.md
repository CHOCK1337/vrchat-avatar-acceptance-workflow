# 独立搜索转换接口

仅为工作台内部接入说明。用户在 Unity 的搜索设置选择模式，不需要手工编辑 JSON。

`WorkbenchBooth.SearchAsync(query, category, maxPrice, sort, page, token, translation = null, queryPlan = null)` 保留原调用兼容；参数从主线程传入。

`translation` 只传白名单字段：

```json
{"mode":"local"}
```

`local` 为默认常见词典；`keyword` 按原文搜索；`api` 为用户主动选择的独立服务。API 模式接受 `base_url`、`api_key`、`model`。例如服务地址可填 `https://api.deepseek.com`；模型填用户正在使用的名称，或留空让上游从 `/models` 查询默认/可用文本模型。此适配不预设某个 DeepSeek 模型，也不读取 Codex 或全局 AI 配置。

成功结果的 `data.query_plan` 为可持久化白名单：

```json
{
  "version": 1,
  "original_query": "Milfy 双马尾",
  "keywords": ["ミルフィ ツインテール"],
  "source": "local",
  "translated": true,
  "note": "仅使用本地常见词典转换；不理解完整长句，结果仍需核对卖家说明。"
}
```

这里是本地词典示例，不能当作商业 API 在线验证。`keywords` 是实际发送给 BOOTH 的检索词，最多 3 项；不会将接口 URL、模型配置或密钥返回给 UI。多词各取当前页后去重交错合并，最终最多 24 项；`total` 在合并时为 `null`，避免将重复商品累计成虚假总数。

翻页时调用相同原文与条件，传入上次 `queryPlan`，同时 `translation = null`。后端校验 `version`、原文、来源、字段类型和关键词数量后复用，完全不调用独立 API。搜索文字或搜索设置变化时，请清空旧方案；旧方案与新原文不一致会明确失败。

API 配置只留在内存和 stdin 中。不得把 `translation` 序列化到工作台选择、草稿、反馈或任务文件；只能保存 `query_plan` 白名单。没有密钥时明确报错，不静默切换其他服务。鉴权失败、无效方案、限流和超时均作为错误返回，不产生虚假的搜索成功。
