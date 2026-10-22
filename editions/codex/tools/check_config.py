"""Validate effective registrations without emitting private configuration."""
import argparse
import json
from pathlib import Path
import sys
from install import parse_toml, read

def check(path):
    config=read(path)
    for label,expected in config['mcp_servers'].items():
        source=Path(expected['config_source'])
        current=parse_toml(source.read_bytes(),source).get('mcp_servers',{}).get(expected['name'])
        if not isinstance(current,dict): raise ValueError('MCP_REGISTRATION_MISSING: '+label)
        if current.get('enabled') is False: raise ValueError('MCP_DISABLED: '+label)
        for field in ('command','args','cwd'):
            if current.get(field)!=expected.get(field): raise ValueError('MCP_CONFIG_CHANGED: '+label+'; rerun Setup preview')
        if current.get('env') or current.get('env_vars') or current.get('enabled_tools') or current.get('disabled_tools'):
            raise ValueError('MCP_SETTINGS_CHANGED: '+label+'; review effective configuration')
    if not Path(config['skill_path']).is_file(): raise ValueError('SKILL_MISSING')
    return {'status':'PASS','note':'Registration files checked; desktop project trust and loaded chat tools require user-session verification.'}

if __name__=='__main__':
    sys.stdout.reconfigure(encoding='utf-8'); sys.stderr.reconfigure(encoding='utf-8')
    p=argparse.ArgumentParser(); p.add_argument('config',type=Path)
    try: print(json.dumps(check(p.parse_args().config)))
    except (ValueError,OSError,KeyError) as error:
        print(str(error),file=sys.stderr); raise SystemExit(2)
