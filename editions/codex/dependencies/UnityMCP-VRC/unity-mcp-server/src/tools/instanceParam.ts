import { ToolDefinition } from "./types.js";

// Every tool that talks to a specific Editor accepts an optional `instance` argument selecting which
// one. It's injected centrally (like `comment`) so all current and future Unity-talking tools get it
// uniformly, and the dispatcher reads it to route the call. Tools with requiresInstance === false
// (list/select/get_command_page) don't route, so they're returned unchanged.
const INSTANCE_PROPERTY = {
  type: "string",
  description:
    "Which Unity instance to target: a name or instanceId from list_unity_instances. " +
    "Omit to use the instance selected for this session via select_unity_instance " +
    "(or the UNITYMCP_INSTANCE default). With several Editors open and none selected, the call fails.",
  minLength: 1,
} as const;

// Return a copy of `def` whose inputSchema has the optional `instance` property added. Pure: it
// never mutates the tool's own definition object.
export function addInstanceParam(def: ToolDefinition): ToolDefinition {
  if (def.requiresInstance === false) return def;

  const schema = def.inputSchema as {
    properties?: Record<string, unknown>;
    [key: string]: unknown;
  };

  return {
    ...def,
    inputSchema: {
      ...schema,
      properties: { ...(schema.properties ?? {}), instance: INSTANCE_PROPERTY },
    },
  };
}
