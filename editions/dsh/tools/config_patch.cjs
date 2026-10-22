// DSH YAML expressions are parsed as data and never executed.
const fs = require('node:fs'), path = require('node:path');
const {createRequire} = require('node:module');
const yaml = createRequire(path.resolve(__dirname, '../runtime/setup/package.json'))('js-yaml');
const schema = yaml.DEFAULT_SCHEMA.extend([new yaml.Type('tag:yaml.org,2002:js', {kind:'scalar', construct:v=>v})]);
const BEGIN = '# BEGIN AVATAR_WORKBENCH_DSH_V1', END = '# END AVATAR_WORKBENCH_DSH_V1';
try {
  const input = JSON.parse(fs.readFileSync(0, 'utf8')), original = input.original;
  const count = s => original.split(s).length - 1;
  if(count(BEGIN) !== count(END) || count(BEGIN) > 1) throw Error('MANAGED_BLOCK_MALFORMED');
  const nl = original.includes('\r\n') ? '\r\n' : '\n';
  const addition = BEGIN + nl + '- insert: ' + JSON.stringify(input.rows) + nl + END;
  let updated;
  if(count(BEGIN)) {
    const a = original.indexOf(BEGIN), b = original.indexOf(END);
    if(b < a) throw Error('MANAGED_BLOCK_MALFORMED');
    updated = original.slice(0, a) + addition + original.slice(b + END.length);
  } else {
    if(/^[\s\uFEFF]*[\[{]/.test(original)) throw Error('FLOW_STYLE_PROFILE: requires a reviewed local merge');
    updated = original + (original.endsWith('\n') ? '' : nl) + nl + addition + nl;
  }
  const parsed = yaml.load(updated, {schema});
  if(!Array.isArray(parsed)) throw Error('PROFILE_MUST_BE_A_LIST');
  const ids = [], servers = [];
  function walk(rows) {
    for(const row of rows) {
      if(!row || typeof row !== 'object') throw Error('INVALID_PROFILE_ROW');
      if(row.id) ids.push(row.id);
      if(row.name === '@deepseek-ai/dsh-mcp-client' && row.config?.serverName) servers.push(row.config.serverName);
      if(Array.isArray(row.insert)) walk(row.insert);
    }
  }
  walk(parsed);
  for(const id of input.rows.map(r=>r.id))
    if(ids.filter(x=>x===id).length !== 1) throw Error('EXISTING_UNMANAGED_ID: '+id+'; merge the existing entry');
  for(const name of ['avatarWorkbench','unityMCP'])
    if(servers.filter(x=>x===name).length !== 1) throw Error('EXISTING_MCP_NAMESPACE: '+name+'; reuse the existing connection');
  process.stdout.write(JSON.stringify({updated}));
} catch(error) {
  const safe = /^(MANAGED_|FLOW_STYLE_|PROFILE_|INVALID_|EXISTING_)/.test(error.message);
  process.stderr.write(safe ? error.message : 'PROFILE_PARSE_FAILED: original file left unchanged');
  process.exitCode = 2;
}
