// Actual stdio MCP handshake and read-only discovery, no Avatar writes or model calls.
import fs from 'node:fs/promises';
import path from 'node:path';
import {createRequire} from 'node:module';
import {pathToFileURL} from 'node:url';
const root=path.resolve(import.meta.dirname,'..');
const req=createRequire(path.join(root,'runtime/unity-mcp/package.json'));
const {Client}=await import(pathToFileURL(req.resolve('@modelcontextprotocol/sdk/client/index.js')));
const {StdioClientTransport}=await import(pathToFileURL(req.resolve('@modelcontextprotocol/sdk/client/stdio.js')));
const argv=process.argv.slice(2);
const option=(key,fallback)=>argv.includes(key)?argv[argv.indexOf(key)+1]:fallback;
const configFile=path.resolve(option('--config',path.join(root,'local/connection.json')));
const output=option('--output',path.join(root,'local/doctor-result.json'));
const report={format:'avatar-workbench-doctor-v1',checked_at:new Date().toISOString(),
  stages:{},avatar_modified:false,model_called:false,visual_acceptance:'NOT_RUN',sdk_build:'NOT_RUN'};
let config;
function textValue(result) {
  if(result.isError) throw Error('MCP_TOOL_RETURNED_ERROR');
  const text=result.content?.find(x=>x.type==='text')?.text;
  try{return JSON.parse(text);}catch{return {message:text};}
}
async function withServer(label,command,args,callback) {
  const client=new Client({name:'avatar-workbench-doctor',version:'0.3.5'});
  const env={};
  for(const key of ['LOCALAPPDATA','UNITYMCP_REGISTRY_DIR']) if(process.env[key]) env[key]=process.env[key];
  const transport=new StdioClientTransport({command,args,env,stderr:'pipe'});
  try {
    await client.connect(transport,{timeout:15000});
    const tools=await client.listTools({}, {timeout:15000});
    report.stages[label]={status:'PASS',tools:tools.tools.map(t=>t.name)};
    await callback(client);
  } finally {await client.close();}
}
try {
  config=JSON.parse(await fs.readFile(configFile,'utf8'));
  report.project=config.project;
  await withServer('workbench_mcp',config.python,
    [path.join(root,'adapter/workbench_mcp.py'),'--config',configFile],async client=>{
      const state=textValue(await client.callTool({name:'workbench_status',arguments:{}},undefined,{timeout:15000}));
      report.stages.routing={status:state.routing_ready?'PASS':'FAIL',issues:state.routing_issues,
        task_exists:state.task_exists,context_id:state.context_id};
      report.stages.catalog={status:state.catalog_available?'AVAILABLE':'NOT_CONFIGURED',
        note:'Import/index local material sources in the Workbench. No disk scan was performed.'};
    });
  await withServer('unity_mcp',config.node,[config.unity_mcp],async client=>{
    if(argv.includes('--no-unity')) {report.stages.unity_project={status:'NOT_RUN'};return;}
    const found=textValue(await client.callTool({name:'list_unity_instances',arguments:{
      comment:'Read-only package connection check; identify the exact configured Unity project.'}},undefined,{timeout:15000}));
    const same=p=>path.resolve(p).toLowerCase()===path.resolve(config.project).toLowerCase();
    const matches=(found.instances||[]).filter(i=>same(i.projectPath));
    if(matches.length!==1) {
      report.stages.unity_project={status:'NOT_READY',matches:matches.length,
        note:'Open the configured project and wait for compilation; UnityMCP > Debug Window should show Listening.'};return;
    }
    const instance=matches[0];
    report.stages.unity_project={status:'PASS',instance:instance.instanceId,unity_version:instance.unityVersion};
    const code=`using System; using System.Linq; using UnityEditor; using UnityEngine;
public class EditorCommand { public static object Execute() {
var loaded=AppDomain.CurrentDomain.GetAssemblies().SelectMany(a=>{try{return a.GetTypes();}catch{return new Type[0];}});
return new { workbench_loaded=loaded.Any(t=>t.FullName=="AvatarWorkbench.WorkbenchWindow"),
compiling=EditorApplication.isCompiling, updating=EditorApplication.isUpdating, playing=EditorApplication.isPlaying,
project=System.IO.Path.GetFullPath(Application.dataPath+"/..") }; } }`;
    const raw=textValue(await client.callTool({name:'execute_editor_command',arguments:{instance:instance.instanceId,
      comment:'Read loaded Workbench assembly and compilation state only; no scene or asset edits.',code,timeoutMs:15000}},undefined,{timeout:20000}));
    const nested=raw.result;
    if(!nested?.executionSuccess) throw Error('UNITY_READ_ONLY_PROBE_FAILED');
    let state=nested.result;
    if(typeof state==='string') state=JSON.parse(state);
    report.stages.workbench_loaded={status:state.workbench_loaded&&!state.compiling&&!state.updating?'PASS':'NOT_READY',...state};
  });
  const all=Object.values(report.stages);
  report.status=all.some(x=>['FAIL','NOT_READY'].includes(x.status))?'NEEDS_ATTENTION':
    argv.includes('--no-unity')?'MCP_PROTOCOL_PASSED_UNITY_NOT_CHECKED':'CONNECTION_READY';
} catch(error) {
  report.status='FAILED';
  report.error={name:error.name,message:String(error.message).slice(0,500)};
  report.next='Read docs/TROUBLESHOOTING.md. Do not paste account keys or full private profile files.';
}
await fs.mkdir(path.dirname(output),{recursive:true});
await fs.writeFile(output,JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify(report,null,2));
if(['FAILED','NEEDS_ATTENTION'].includes(report.status)) process.exitCode=2;
