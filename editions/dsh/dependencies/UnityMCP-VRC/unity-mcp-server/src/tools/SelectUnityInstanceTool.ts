import { ErrorCode, McpError } from "@modelcontextprotocol/sdk/types.js";
import { Tool, ToolContext, ToolDefinition } from "./types.js";

// Sets the default Unity instance for this session. Subsequent tool calls that omit `instance` target
// it, until changed. Stores the resolved instanceId (stable across the target's domain reloads).
export class SelectUnityInstanceTool implements Tool {
  getDefinition(): ToolDefinition {
    return {
      name: "select_unity_instance",
      description:
        "Choose which running Unity instance subsequent tool calls target by default (until changed). " +
        "Pass a name or instanceId from list_unity_instances. Individual calls can still override with " +
        "their own `instance` argument.",
      category: "Instance Management",
      tags: ["unity", "instance", "select"],
      // Selecting is meta - it doesn't itself route to an instance (it takes its target explicitly).
      requiresInstance: false,
      inputSchema: {
        type: "object",
        properties: {
          instance: {
            type: "string",
            description:
              "Name or instanceId of the instance to select (from list_unity_instances).",
            minLength: 1,
          },
        },
        required: ["instance"],
        additionalProperties: false,
      },
      returns: {
        type: "object",
        description: "Confirmation of the selected instance.",
        format: "JSON object: { selected: { instanceId, name, projectPath, port } }",
      },
      examples: [
        {
          description: "Target the Garibaldi project",
          input: { instance: "Garibaldi" },
          output:
            '{ "selected": { "name": "Garibaldi", "instanceId": "a1b2c3d4", "port": 51017, ... } }',
        },
      ],
    };
  }

  async execute(args: any, context: ToolContext) {
    const key = typeof args?.instance === "string" ? args.instance.trim() : "";
    if (!key) {
      throw new McpError(
        ErrorCode.InvalidParams,
        "The 'instance' parameter is required: a name or instanceId from list_unity_instances.",
      );
    }

    // Match against LIVE instances only - selecting something that isn't running should fail now,
    // not on the next call.
    const live = (await context.registry.list()).filter((i) => i.live);
    const matches = live.filter(
      (i) => i.instanceId === key || i.name.toLowerCase() === key.toLowerCase(),
    );

    if (matches.length === 0) {
      const known = live.length
        ? live.map((i) => `${i.name} [${i.instanceId}]`).join(", ")
        : "(none running)";
      throw new McpError(
        ErrorCode.InvalidParams,
        `No running Unity instance matches '${key}'. Live instances: ${known}.`,
      );
    }
    if (matches.length > 1) {
      throw new McpError(
        ErrorCode.InvalidParams,
        `'${key}' is ambiguous (${matches
          .map((i) => `${i.name} [${i.instanceId}]`)
          .join(", ")}). Select by instanceId.`,
      );
    }

    const chosen = matches[0];
    context.session.set(chosen.instanceId);

    return {
      content: [
        {
          type: "text",
          text: JSON.stringify(
            {
              selected: {
                instanceId: chosen.instanceId,
                name: chosen.name,
                projectPath: chosen.projectPath,
                port: chosen.port,
              },
            },
            null,
            2,
          ),
        },
      ],
    };
  }
}
