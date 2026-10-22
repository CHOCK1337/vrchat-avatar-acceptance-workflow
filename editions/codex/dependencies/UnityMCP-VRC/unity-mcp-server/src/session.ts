// The Unity instance this MCP process has selected as its default target.
//
// Used when a tool call omits `instance`. It's per-process state (each Claude session runs its own
// MCP server, so each can target a different Editor), and it stores the stable instanceId rather
// than a name so the choice survives the target's domain reloads and stays unambiguous.
//
// Seeded from UNITYMCP_INSTANCE so a host config can pin a session to one project up front - then the
// agent never has to call select_unity_instance. (The seed may be a name or id; resolution handles
// both at call time.)
export class InstanceSession {
  private selected: string | undefined;

  constructor(seed: string | undefined = process.env.UNITYMCP_INSTANCE) {
    this.selected = seed?.trim() || undefined;
  }

  get(): string | undefined {
    return this.selected;
  }

  set(instanceId: string): void {
    this.selected = instanceId;
  }
}
