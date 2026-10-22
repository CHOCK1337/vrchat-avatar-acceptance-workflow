import { ToolDefinition } from "./types.js";

// Every tool call must carry a `comment`: a short, human-readable note on WHY the call is being
// made. It isn't used by any tool's logic - it exists for the developer watching the Unity Editor,
// where the UnityMCP debug window shows a live log of recent calls (time / comment / tool). Making
// it required keeps that log meaningful: every action Claude takes is annotated with its intent.
//
// This is injected centrally (see index.ts) rather than declared in each tool, so all current and
// future tools get the same required field with one definition. The connection layer forwards the
// comment to Unity in the request envelope (see UnityConnection.forComment).
const COMMENT_PROPERTY = {
  type: "string",
  description:
    "A brief, one-sentence note on why you're making this call / what you're trying to accomplish. " +
    "Shown live in the Unity Editor's UnityMCP debug window so the developer can follow what you're doing. " +
    "Required on every call.",
  minLength: 1,
} as const;

// Return a copy of `def` whose inputSchema has `comment` added as a required string property.
// Pure: it never mutates the tool's own definition object.
export function requireComment(def: ToolDefinition): ToolDefinition {
  const schema = def.inputSchema as {
    properties?: Record<string, unknown>;
    required?: string[];
    [key: string]: unknown;
  };

  return {
    ...def,
    inputSchema: {
      ...schema,
      properties: { ...(schema.properties ?? {}), comment: COMMENT_PROPERTY },
      required: Array.from(new Set([...(schema.required ?? []), "comment"])),
    },
  };
}
