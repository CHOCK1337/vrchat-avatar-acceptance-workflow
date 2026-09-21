"""Scoped, local feedback memory. Standard library only; no Unity/model/network calls.

ACTIVE means eligibility to suggest a recorded action under matching inputs. It
never means the avatar is accepted. Evidence is supplied by the caller, not
independently authenticated here. The normal route, permissions and acceptance
checks remain authoritative.
"""
from __future__ import annotations

from contextlib import contextmanager
import hashlib
import json
import os
from pathlib import Path
import re
import sqlite3
from typing import Any

SCHEMA_VERSION = 1
SCOPE_KEYS = ('project_id', 'operation', 'asset_digest', 'target_digest', 'provider',
              'provider_version', 'profile_digest', 'settings_digest', 'executor_version')
EVENT_KINDS = {'correction', 'requirement_change', 'preference', 'attempt', 'observation', 'capability_gap'}
PREFERENCES = {
    'report_style': {'brief', 'standard'},
    'menu_language': {'zh-CN', 'en', 'ja'},
    'new_main_outfit_default': {'first_successful', 'original'},
    'accessory_default': {'author', 'off', 'by_kind'},
    'menu_grouping': {'author_first', 'per_outfit'},
}
FORBIDDEN_ACTION_WORDS = {'upload', 'publish', 'delete', 'shell', 'exec', 'code', 'script',
                          'authority', 'permission', 'sandbox', 'policy', 'backup', 'test'}
FORBIDDEN_PARAMETER_KEYS = {'upload_authorized', 'private', 'cancelled', 'sandbox_mode',
                            'model', 'reasoning_effort', 'skip_checks', 'retry_limit',
                            'max_retries', 'command', 'script', 'code', 'shell', 'authority'}
VISIBLE_KINDS = {'visual', 'behavior', 'user_confirmation'}
MAX_IMPORT_BYTES = 1024 * 1024
MAX_DB_BYTES = 128 * 1024 * 1024
LIMIT_NOTE = 'Eligibility relies on recorded observations; not independent visual proof or upload authority.'


def dumps(data: Any) -> str:
    return json.dumps(data, ensure_ascii=False, sort_keys=True, separators=(',', ':'), allow_nan=False)


def digest(data: Any) -> str:
    return hashlib.sha256(dumps(data).encode('utf-8')).hexdigest()


def redact(text: str) -> str:
    """Best-effort minimization, not a complete DLP or anonymization guarantee."""
    text = re.sub(r'\bsk-[A-Za-z0-9_-]{16,}', '[REDACTED_TOKEN]', text)
    text = re.sub(r'(?i)\b(Bearer)\s+[A-Za-z0-9._~+/=-]+', r'\1 [REDACTED_TOKEN]', text)
    text = re.sub(r'(?i)\b(authcookie|twofactorauth|password|api[_-]?key|access[_-]?token)\s*[:=]\s*[^\s,;]+',
                  r'\1=[REDACTED]', text)
    text = re.sub(r'[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}', '[REDACTED_EMAIL]', text)
    text = re.sub(r'(https?://)[^/\s:@]+:[^/\s@]+@', r'\1[REDACTED]@', text)
    return text


def clean(value: Any) -> Any:
    if isinstance(value, str): return redact(value)
    if isinstance(value, list): return [clean(v) for v in value]
    if isinstance(value, dict): return {str(k): clean(v) for k, v in value.items()}
    return value


def text(value: Any, field: str, maximum: int = 256) -> str:
    if not isinstance(value, str) or not value.strip() or len(value) > maximum:
        raise ValueError(f'{field} must be a nonempty string of at most {maximum} characters')
    return value


def normalize_scope(scope: Any) -> dict:
    if not isinstance(scope, dict) or set(scope) - set(SCOPE_KEYS):
        raise ValueError('scope must contain only the documented input identity fields')
    result = {k: text(v, 'scope.'+k) for k, v in scope.items()}
    for k in ('project_id', 'operation'):
        text(result.get(k), 'scope.'+k)
    return result


def complete_scope(scope: dict) -> bool:
    return all(k in scope and scope[k].strip().lower() not in {'unknown', 'uncertain', 'tbd', '?', 'n/a'}
               for k in SCOPE_KEYS)


def normalize_action(action: Any) -> dict:
    if not isinstance(action, dict) or set(action) != {'capability', 'parameters'}:
        raise ValueError('action needs capability and parameters only; never executable code')
    cap = text(action['capability'], 'action.capability', 120)
    if not re.fullmatch(r'[A-Za-z0-9_.@-]+', cap): raise ValueError('Invalid capability identifier')
    if set(re.split(r'[_.@-]+', cap.lower())) & FORBIDDEN_ACTION_WORDS:
        raise ValueError('Memory cannot recommend permission, publication, code, deletion or workflow-policy operations')
    if not isinstance(action['parameters'], dict): raise ValueError('parameters must be an object')
    def check(value: Any, depth: int = 0):
        if depth > 6: raise ValueError('Action parameters too deeply nested')
        if isinstance(value, dict):
            if {str(k).lower() for k in value} & FORBIDDEN_PARAMETER_KEYS:
                raise ValueError('Memory cannot change permissions, model, retry or acceptance policy')
            for v in value.values(): check(v, depth+1)
        elif isinstance(value, list):
            for v in value: check(v, depth+1)
        elif not isinstance(value, (str, int, float, bool, type(None))):
            raise ValueError('Only JSON parameter values are supported')
    check(action['parameters'])
    if len(dumps(action)) > 2500: raise ValueError('Action too large; keep only the relevant parameter delta')
    return action


def normalize_event(raw: Any) -> dict:
    if not isinstance(raw, dict): raise ValueError('event must be a JSON object')
    allowed = {'event_id', 'task_id', 'session_id', 'episode_id', 'resource_id', 'requirement_id',
               'kind', 'role', 'scope', 'text', 'attempt', 'observation', 'preference', 'capability_id'}
    if set(raw)-allowed: raise ValueError('Unknown event fields; do not import raw transcripts or task authority')
    if len(dumps(raw)) > 10000: raise ValueError('Event exceeds the compact-record budget')
    e = clean(raw)
    for k in ('event_id','task_id','episode_id','resource_id','requirement_id'):
        text(e.get(k), k)
    e['scope'] = normalize_scope(e.get('scope'))
    text(e.get('text'), 'text', 1500)
    if e.get('kind') not in EVENT_KINDS or e.get('role') not in {'user','assistant','tool'}:
        raise ValueError('Unsupported event kind/role')
    if e['kind'] in {'correction','preference','requirement_change'} and e['role'] != 'user':
        raise ValueError('Only explicit user input may be recorded as a correction, preference or new requirement')
    extra = {'attempt':'attempt', 'observation':'observation', 'preference':'preference',
             'capability_gap':'capability_id'}.get(e['kind'])
    if set(e).intersection({'attempt','observation','preference','capability_id'}) != ({extra} if extra else set()):
        raise ValueError('Event payload does not match its kind')
    if e['kind'] == 'attempt':
        a=e['attempt']
        if not isinstance(a,dict) or set(a) != {'attempt_id','before_candidate','after_candidate','change_ref','action'}:
            raise ValueError('attempt needs identities, change receipt and exact recorded action')
        for k in ('attempt_id','before_candidate','after_candidate','change_ref'): text(a.get(k), k, 600)
        a['action']=normalize_action(a.get('action'))
    if e['kind'] == 'observation':
        o=e['observation']
        if not isinstance(o,dict) or set(o) != {'kind','result','observed','candidate','attempt_id','evidence_ref'}:
            raise ValueError('observation needs kind,result,observed,candidate,attempt_id,evidence_ref')
        if o['kind'] not in VISIBLE_KINDS | {'numeric','structure','upload'}:
            raise ValueError('Unknown observation kind')
        if o['result'] not in {'ok','failed','unknown'} or not isinstance(o['observed'],bool):
            raise ValueError('Invalid observation result')
        if o['kind']=='user_confirmation' and e['role']!='user':
            raise ValueError('User confirmation must refer to actual user input')
        for k in ('candidate','attempt_id','evidence_ref'): text(o.get(k), k, 600)
    if e['kind']=='preference':
        p=e['preference']
        if not isinstance(p,dict) or set(p)!={'key','value'} or p['key'] not in PREFERENCES:
            raise ValueError('Only documented low-risk preferences are learnable; never authority or policy')
        if p['value'] not in PREFERENCES[p['key']]: raise ValueError('Unsupported preference value')
    if e['kind']=='capability_gap': text(e['capability_id'],'capability_id',120)
    return e


def default_home() -> Path:
    if os.environ.get('AA_LEARNING_HOME'): return Path(os.environ['AA_LEARNING_HOME']).expanduser()
    if os.name == 'nt': return Path(os.environ.get('LOCALAPPDATA', Path.home()/'AppData/Local'))/'AvatarAssembler/learning'
    return Path(os.environ.get('XDG_DATA_HOME', Path.home()/'.local/share'))/'avatarassembler/learning'


class LearningStore:
    def __init__(self, home: Path | None = None):
        original=(home if home is not None else default_home()).expanduser().absolute()
        if any(p.is_symlink() for p in [original, *original.parents]):
            raise ValueError('Symlink learning-store paths are not supported')
        self.home=original.resolve()
        prohibited={'assets','packages','library','temp','skills','assemble-vrchat-avatar'}
        if {p.lower() for p in self.home.parts} & prohibited:
            raise ValueError('Learning storage must be outside Unity assets and installed skills')
        self.db=self.home/'learning.sqlite3'

    @contextmanager
    def _db(self, write: bool=False):
        if not self.db.exists() and not write:
            yield None; return
        if self.db.is_symlink(): raise ValueError('Symlink database refused')
        if write:
            if self.db.exists() and self.db.stat().st_size > MAX_DB_BYTES:
                raise ValueError('Local memory reached 128 MiB; skip learning and manage retention separately')
            newhome=not self.home.exists()
            self.home.mkdir(parents=True,exist_ok=True)
            if newhome and os.name!='nt': self.home.chmod(0o700)
        connection=None
        try:
            location=str(self.db) if write else self.db.as_uri()+'?mode=ro'
            connection=sqlite3.connect(location,timeout=1.0,uri=not write)
            connection.row_factory=sqlite3.Row
            version=connection.execute('PRAGMA user_version').fetchone()[0]
            if version not in (0,SCHEMA_VERSION): raise ValueError('Unsupported memory schema; do not overwrite it')
            if write:
                connection.execute('PRAGMA secure_delete=ON')
                connection.executescript('''
                CREATE TABLE IF NOT EXISTS episodes (
                  task_id TEXT NOT NULL, episode_id TEXT NOT NULL, scope_hash TEXT NOT NULL,
                  resource_id TEXT NOT NULL, requirement_id TEXT NOT NULL, summarized_seq INTEGER NOT NULL DEFAULT 0,
                  PRIMARY KEY(task_id,episode_id));
                CREATE TABLE IF NOT EXISTS events (
                  seq INTEGER PRIMARY KEY AUTOINCREMENT, event_id TEXT UNIQUE NOT NULL,
                  task_id TEXT NOT NULL, episode_id TEXT NOT NULL, payload TEXT NOT NULL,
                  payload_hash TEXT NOT NULL);
                CREATE INDEX IF NOT EXISTS event_episode ON events(task_id,episode_id,seq);
                CREATE TABLE IF NOT EXISTS lessons (
                  lesson_id TEXT PRIMARY KEY, task_id TEXT NOT NULL, episode_id TEXT NOT NULL,
                  requirement_id TEXT NOT NULL, scope_hash TEXT NOT NULL, kind TEXT NOT NULL,
                  state TEXT NOT NULL, payload TEXT NOT NULL, updated_seq INTEGER NOT NULL);
                CREATE INDEX IF NOT EXISTS lesson_scope ON lessons(scope_hash,state);
                CREATE TABLE IF NOT EXISTS outcomes (
                  lesson_id TEXT NOT NULL,event_id TEXT NOT NULL,result TEXT NOT NULL,
                  PRIMARY KEY(lesson_id,event_id));
                ''')
                connection.execute(f'PRAGMA user_version={SCHEMA_VERSION}')
                if os.name!='nt': self.db.chmod(0o600)
                connection.execute('BEGIN IMMEDIATE')
            yield connection
            if write: connection.commit()
        except Exception:
            if connection and write: connection.rollback()
            raise
        finally:
            if connection: connection.close()

    def _record(self, c, e: dict) -> dict:
        encoded=dumps(e);h=digest(e)
        prior=c.execute('SELECT seq,payload_hash FROM events WHERE event_id=?',(e['event_id'],)).fetchone()
        if prior:
            if prior['payload_hash']!=h: raise ValueError('Event ID collision: original evidence cannot be overwritten')
            return {'status':'DUPLICATE','event_id':e['event_id'],'seq':prior['seq']}
        identity=(digest(e['scope']),e['resource_id'],e['requirement_id'])
        episode=c.execute('SELECT * FROM episodes WHERE task_id=? AND episode_id=?',(e['task_id'],e['episode_id'])).fetchone()
        if episode and tuple(episode[k] for k in ('scope_hash','resource_id','requirement_id'))!=identity:
            raise ValueError('Episode identity changed; use a new episode for a different resource, goal or environment')
        c.execute('INSERT OR IGNORE INTO episodes(task_id,episode_id,scope_hash,resource_id,requirement_id) VALUES(?,?,?,?,?)',
                  (e['task_id'],e['episode_id'],*identity))
        seq=c.execute('INSERT INTO events(event_id,task_id,episode_id,payload,payload_hash) VALUES(?,?,?,?,?)',
                      (e['event_id'],e['task_id'],e['episode_id'],encoded,h)).lastrowid
        # An explicit recurrence invalidates same-scope, same-goal recipes immediately,
        # even before the end-of-task consolidation hook runs.
        suspended=[]
        if e['kind']=='correction' or (e['kind']=='observation' and self._credible(e,'failed')):
            rows=c.execute("SELECT * FROM lessons WHERE scope_hash=? AND requirement_id=? AND kind='recipe' AND state='ACTIVE'",
                           (identity[0],e['requirement_id'])).fetchall()
            for row in rows:
                p=json.loads(row['payload']);p['state']='SUSPENDED';p['reason']='NEW_NEGATIVE_EVIDENCE';p['counterexample_event_id']=e['event_id']
                c.execute('UPDATE lessons SET state=?,payload=?,updated_seq=? WHERE lesson_id=?',
                          ('SUSPENDED',dumps(p),seq,row['lesson_id']))
                suspended.append(row['lesson_id'])
        return {'status':'RECORDED','event_id':e['event_id'],'seq':seq,'episode_id':e['episode_id'],'suspended_ids':suspended}

    def record(self, event: dict) -> dict:
        e=normalize_event(event)
        with self._db(True) as c: return self._record(c,e)

    def import_jsonl(self,path: Path) -> dict:
        if path.stat().st_size>MAX_IMPORT_BYTES: raise ValueError('Import at most 1 MiB of selected, normalized events per call')
        batch=[normalize_event(json.loads(line)) for line in path.read_text(encoding='utf-8-sig').splitlines() if line.strip()]
        if len(batch)>200: raise ValueError('Import at most 200 compact events; not an entire raw chat export')
        if not batch: return {'status':'NO_EVENTS','recorded':0,'duplicates':0}
        with self._db(True) as c:
            results=[self._record(c,e) for e in batch]
        return {'status':'IMPORTED','recorded':sum(x['status']=='RECORDED' for x in results),
                'duplicates':sum(x['status']=='DUPLICATE' for x in results)}

    @staticmethod
    def _events(c,task_id,episode_id):
        rows=c.execute('SELECT seq,payload FROM events WHERE task_id=? AND episode_id=? ORDER BY seq',(task_id,episode_id)).fetchall()
        return [dict(json.loads(r['payload']),seq=r['seq']) for r in rows]

    def events(self,task_id: str,episode_id: str,after: int=0,limit: int=12) -> dict:
        with self._db() as c:
            if c is None:return {'events':[]}
            rows=c.execute('SELECT seq,payload FROM events WHERE task_id=? AND episode_id=? AND seq>? ORDER BY seq LIMIT ?',
                           (task_id,episode_id,after,max(1,min(limit,24)))).fetchall()
            return {'events':[dict(json.loads(r['payload']),seq=r['seq']) for r in rows]}

    def pending(self,task_id: str,limit: int=3) -> dict:
        groups=[]
        with self._db() as c:
            if c is None:return {'episodes':[],'remaining':0}
            episodes=c.execute('SELECT * FROM episodes WHERE task_id=? ORDER BY episode_id',(task_id,)).fetchall()
            for ep in episodes:
                events=self._events(c,task_id,ep['episode_id'])
                newer=[e for e in events if e['seq']>ep['summarized_seq']]
                if not newer or not any(e['kind'] in {'correction','preference','capability_gap'} for e in events):continue
                # Keep LLM input bounded. Older full evidence remains addressable through events().
                selected=[];size=0
                for e in reversed(events):
                    small={k:v for k,v in e.items() if k not in {'scope','task_id','episode_id','resource_id','requirement_id'}}
                    small['text']=small['text'][:300]
                    n=len(dumps(small))
                    if len(selected)>=6 or size+n>1700:break
                    selected.insert(0,small);size+=n
                groups.append({'task_id':task_id,'episode_id':ep['episode_id'],
                               'resource_id':ep['resource_id'],'requirement_id':ep['requirement_id'],
                               'through_seq':events[-1]['seq'],'scope':events[0]['scope'],
                               'correction_count':sum(e['kind']=='correction' for e in events),
                               'events':selected,'omitted_events':len(events)-len(selected)})
        n=max(1,min(limit,3))
        return {'episodes':groups[:n],'remaining':max(0,len(groups)-n),
                'instruction':'Consolidate at most once per natural boundary. Omitted evidence may be read by ID/episode; do not invent it.'}

    @staticmethod
    def _credible(e: dict,result='ok') -> bool:
        o=e.get('observation',{})
        return (e['kind']=='observation' and o.get('kind') in VISIBLE_KINDS
                and o.get('result')==result and o.get('observed') is True
                and bool(o.get('evidence_ref')) and bool(e.get('text')))

    def learn(self,proposal: dict) -> dict:
        if not isinstance(proposal,dict) or set(proposal)!={'task_id','episode_id','through_seq','lessons'}:
            raise ValueError('Summary needs task_id,episode_id,through_seq,lessons only')
        items=proposal['lessons']
        if not isinstance(items,list) or len(items)>3:raise ValueError('At most three lessons per episode summary')
        results=[]
        with self._db(True) as c:
            events=self._events(c,proposal['task_id'],proposal['episode_id'])
            if not events or events[-1]['seq']!=proposal['through_seq']:
                raise ValueError('Stale summary: reread this episode once; do not overwrite new corrections')
            ep=c.execute('SELECT * FROM episodes WHERE task_id=? AND episode_id=?',(proposal['task_id'],proposal['episode_id'])).fetchone()
            if ep['summarized_seq']==proposal['through_seq']:
                return {'status':'ALREADY_CONSOLIDATED','lessons':[]}
            lookup={e['event_id']:e for e in events}
            scope=events[0]['scope']
            for raw in items:
                if not isinstance(raw,dict) or set(raw)-{'kind','summary','evidence_ids','action'}:
                    raise ValueError('Unsupported lesson fields; state is computed, never supplied by an agent')
                item=clean(raw);kind=item.get('kind')
                if kind not in {'recipe','avoid_attempt','preference','capability_gap'}:raise ValueError('Unsupported lesson kind')
                text(item.get('summary'),'summary',700)
                ids=item.get('evidence_ids')
                if not isinstance(ids,list) or not 1<=len(ids)<=24 or any(not isinstance(i,str) or i not in lookup for i in ids):
                    raise ValueError('Every lesson needs source event IDs from this exact episode')
                selected=[lookup[i] for i in ids]
                state='CANDIDATE';reason='EVIDENCE_INCOMPLETE';action=None;pref=None
                if kind in {'recipe','avoid_attempt'}:
                    action=normalize_action(item.get('action'))
                    attempts=[e for e in selected if e['kind']=='attempt']
                    if not attempts:raise ValueError('A reusable/failed action must exist in recorded operations, not be invented by a summary')
                    a=max(attempts,key=lambda e:e['seq'])
                    if dumps(a['attempt']['action'])!=dumps(action):raise ValueError('Proposed action differs from the recorded attempt')
                    at=a['attempt']
                    matching=[e for e in selected if e['kind']=='observation' and e['seq']>a['seq']
                              and e['observation']['attempt_id']==at['attempt_id'] and e['observation']['candidate']==at['after_candidate']]
                    negatives=[e for e in events if e['kind']=='correction' or self._credible(e,'failed')]
                    if complete_scope(scope):
                        if kind=='recipe':
                            good=[e for e in matching if self._credible(e)]
                            valid_change=at['before_candidate']!=at['after_candidate'] and bool(at['change_ref'])
                            latest_negative=max([e['seq'] for e in negatives],default=0)
                            if good and valid_change and latest_negative<a['seq']:
                                state='ACTIVE';reason='RECORDED_SCOPED_RESULT'
                            elif not valid_change:reason='NO_RECORDED_ARTIFACT_CHANGE'
                            elif latest_negative>=a['seq']:reason='UNRESOLVED_NEGATIVE_EVIDENCE'
                            else:reason='NO_MATCHING_VISIBLE_OBSERVATION'
                        else:
                            bad=[e for e in matching if self._credible(e,'failed')]
                            bad+=[e for e in selected if e['kind']=='correction' and e['seq']>a['seq']]
                            last_bad=max([e['seq'] for e in bad],default=0)
                            later_good=[e for e in events if self._credible(e) and e['seq']>last_bad
                                        and e.get('observation',{}).get('attempt_id')==at['attempt_id']
                                        and e.get('observation',{}).get('candidate')==at['after_candidate']]
                            if bad and not later_good:
                                state='ACTIVE';reason='RECORDED_FAILED_ATTEMPT'
                            elif later_good:reason='LATER_SUCCESS_CONTRADICTS_FAILURE_RULE'
                    else:reason='INPUT_IDENTITY_INCOMPLETE'
                elif kind=='preference':
                    preferences=[e for e in selected if e['kind']=='preference' and e['role']=='user']
                    if not preferences:raise ValueError('Preference requires explicit user event')
                    pref=max(preferences,key=lambda e:e['seq'])['preference'];state='ACTIVE';reason='EXPLICIT_USER_PREFERENCE'
                    # Project-scoped preferences never overwrite task settings; they are suggestions.
                    for old in c.execute("SELECT * FROM lessons WHERE kind='preference' AND state='ACTIVE'").fetchall():
                        p=json.loads(old['payload'])
                        if p['scope']['project_id']==scope['project_id'] and p.get('preference',{}).get('key')==pref['key']:
                            p['state']='RETIRED';p['reason']='SUPERSEDED_BY_EXPLICIT_USER_PREFERENCE'
                            c.execute("UPDATE lessons SET state='RETIRED',payload=? WHERE lesson_id=?",(dumps(p),old['lesson_id']))
                else:
                    if not any(e['kind']=='capability_gap' for e in selected):raise ValueError('Capability gap must have a source record')
                    state='ACTIVE';reason='BACKLOG_ONLY_NO_RUNTIME_DEVELOPMENT'
                lid='lesson-'+digest([proposal['task_id'],proposal['episode_id'],kind,action,pref,item['summary']])[:24]
                value={'lesson_id':lid,'kind':kind,'state':state,'summary':item['summary'],'scope':scope,
                       'requirement_id':ep['requirement_id'],'action':action,'preference':pref,'evidence_ids':ids,
                       'reason':reason,'evidence_limit':LIMIT_NOTE,'skip_current_acceptance':False}
                c.execute('INSERT INTO lessons VALUES(?,?,?,?,?,?,?,?,?) ON CONFLICT(lesson_id) DO UPDATE SET state=excluded.state,payload=excluded.payload,updated_seq=excluded.updated_seq',
                          (lid,proposal['task_id'],proposal['episode_id'],ep['requirement_id'],digest(scope),kind,state,dumps(value),proposal['through_seq']))
                results.append({'lesson_id':lid,'state':state,'reason':reason})
            c.execute('UPDATE episodes SET summarized_seq=? WHERE task_id=? AND episode_id=?',
                      (proposal['through_seq'],proposal['task_id'],proposal['episode_id']))
        return {'status':'CONSOLIDATED','lessons':results,'note':'No model, Unity, test, backup, code rewrite or upload has been executed.'}

    def recall(self,scope: dict,limit: int=3) -> dict:
        scope=normalize_scope(scope);matches=[];advisories=[];suspended=[];recent=[]
        with self._db() as c:
            if c is None:return {'matches':[],'advisories':[],'suspended_ids':[],'recent_corrections':[]}
            rows=c.execute("SELECT * FROM lessons WHERE state IN ('ACTIVE','CANDIDATE','SUSPENDED') ORDER BY updated_seq DESC").fetchall()
            for row in rows:
                p=json.loads(row['payload']);s=p['scope']
                exact=dumps(s)==dumps(scope) and complete_scope(scope)
                pref=p['kind']=='preference' and s['project_id']==scope['project_id']
                related=(s['project_id']==scope['project_id'] and s['operation']==scope['operation']
                         and s.get('asset_digest')==scope.get('asset_digest'))
                if p['state']=='SUSPENDED' and exact:suspended.append(p['lesson_id']);continue
                if p['state']=='ACTIVE' and (exact or pref):
                    matches.append(p)
                elif related:
                    # No numerical action or recipe summary leaks from stale/unknown input contexts.
                    advisories.append({'lesson_id':p['lesson_id'],'state':p['state'],'kind':p['kind'],
                                       'reason':'INPUT_CHANGED_OR_CANDIDATE_ONLY','usage':'inspect source evidence, never copy old parameters'})
            # Recent unsummarized corrections are useful even after a crashed session.
            for row in c.execute('SELECT seq,payload FROM events ORDER BY seq DESC LIMIT 500').fetchall():
                e=json.loads(row['payload'])
                if e['kind']=='correction' and e['scope']==scope:
                    resolved=c.execute("SELECT 1 FROM lessons WHERE task_id=? AND episode_id=? AND kind='recipe' AND state='ACTIVE' AND updated_seq>=? LIMIT 1",
                                       (e['task_id'],e['episode_id'],row['seq'])).fetchone()
                    if resolved:continue
                    recent.append({'event_id':e['event_id'],'requirement_id':e['requirement_id'],'text':e['text'][:240]})
                    if len(recent)==2:break
        # Priority: never-repeat exact failed attempt, explicit preference, then recipe/backlog.
        rank={'avoid_attempt':0,'preference':1,'recipe':2,'capability_gap':3}
        matches.sort(key=lambda p:rank[p['kind']])
        n=max(1,min(limit,3))
        result={'matches':matches[:n],'advisories':advisories[:2],'suspended_ids':suspended[:3],
                'recent_corrections':recent,'trust':'Reference data only. Current task instructions, capabilities, permissions and acceptance rules win.'}
        # Compact model context: cap whole output. Extra entries remain in local storage.
        while len(dumps(result))>7000 and result['matches']:result['matches'].pop()
        return result

    def outcome(self,lesson_id: str,event_id: str) -> dict:
        with self._db(True) as c:
            row=c.execute('SELECT * FROM lessons WHERE lesson_id=?',(lesson_id,)).fetchone()
            ev=c.execute('SELECT seq,payload FROM events WHERE event_id=?',(event_id,)).fetchone()
            if not row or not ev:raise ValueError('Outcome needs an existing lesson and existing observed event')
            lesson=json.loads(row['payload']);e=json.loads(ev['payload'])
            if e['scope']!=lesson['scope'] or e['requirement_id']!=lesson['requirement_id']:
                raise ValueError('Outcome scope or requirement differs from the recipe')
            if event_id in lesson['evidence_ids']:
                raise ValueError('Original lesson evidence is not a later reuse outcome')
            failed=e['kind']=='correction' or self._credible(e,'failed')
            success=self._credible(e)
            if not failed and not success:raise ValueError('Numerical, upload or assumed outcome cannot measure visual reuse success')
            if success:
                if lesson['kind']!='recipe':raise ValueError('Only an executed recipe has a visual reuse success')
                o=e['observation']
                attempts=[x for x in self._events(c,e['task_id'],e['episode_id'])
                          if x['kind']=='attempt' and x['seq']<ev['seq']
                          and x['attempt']['attempt_id']==o['attempt_id']
                          and x['attempt']['after_candidate']==o['candidate']
                          and x['attempt']['action']==lesson['action']]
                if not attempts:raise ValueError('Reuse success needs a matching recorded execution receipt')
            result='failure' if failed else 'success'
            c.execute('INSERT OR IGNORE INTO outcomes VALUES(?,?,?)',(lesson_id,event_id,result))
            if failed:
                lesson['state']='SUSPENDED';lesson['reason']='NEGATIVE_REUSE_OUTCOME';lesson['counterexample_event_id']=event_id
                c.execute("UPDATE lessons SET state='SUSPENDED',payload=?,updated_seq=? WHERE lesson_id=?",(dumps(lesson),ev['seq'],lesson_id))
            # Success is counted, but never automatically resurrects a suspended rule.
            return {'lesson_id':lesson_id,'outcome':result,'state':lesson['state']}

    def report(self,task_id: str) -> dict:
        with self._db() as c:
            if c is None:return {'task_id':task_id,'events':0,'corrections':0,'lessons':[]}
            payloads=[json.loads(r[0]) for r in c.execute('SELECT payload FROM events WHERE task_id=?',(task_id,)).fetchall()]
            lessons=[{'lesson_id':r['lesson_id'],'kind':r['kind'],'state':r['state']}
                     for r in c.execute('SELECT lesson_id,kind,state FROM lessons WHERE task_id=?',(task_id,)).fetchall()]
            return {'task_id':task_id,'events':len(payloads),'corrections':sum(e['kind']=='correction' for e in payloads),
                    'new_requirements':sum(e['kind']=='requirement_change' for e in payloads),'lessons':lessons,
                    'note':'Counts are memory bookkeeping, not delivered avatar progress or measured token savings.'}

    def forget(self,task_id: str) -> dict:
        with self._db() as c:
            if c is None:return {'status':'FORGOTTEN','task_id':task_id}
        with self._db(True) as c:
            c.execute('DELETE FROM outcomes WHERE lesson_id IN (SELECT lesson_id FROM lessons WHERE task_id=?) OR event_id IN (SELECT event_id FROM events WHERE task_id=?)',(task_id,task_id))
            c.execute('DELETE FROM lessons WHERE task_id=?',(task_id,))
            c.execute('DELETE FROM events WHERE task_id=?',(task_id,))
            c.execute('DELETE FROM episodes WHERE task_id=?',(task_id,))
        return {'status':'FORGOTTEN','task_id':task_id,'note':'Local records removed; user-created exports and OS backups are not controlled by this tool.'}
