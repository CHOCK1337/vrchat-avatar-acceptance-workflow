# Contributing

提交修改前请保持以下边界：

1. 不提交模型、服装、纹理、付费素材、用户截图、账号凭据或本机绝对路径。
2. 不把静态检查、结构化 JSON 或 SDK Build Success 当作 VRChat 客户端运行证明。
3. 新增规则时同步更新模板、判定脚本和测试；失败条件默认 fail-closed。
4. 保留作者源文件；示例只能使用虚构标识和脱敏数据。
5. 运行完整测试：

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
python -m unittest discover -s scripts/tests -p 'test_*.py' -v
```
