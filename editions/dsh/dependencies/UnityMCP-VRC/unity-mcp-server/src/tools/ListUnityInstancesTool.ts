import { Tool, ToolContext, ToolDefinition } from "./types.js";

// Enumerates the Unity Editors currently reachable (registered AND answering on their port). This is
// how a client discovers what's open before choosing one with select_unity_instance.
export class ListUnityInstancesTool implements Tool {
  getDefinition(): ToolDefinition {
    return {
      name: "list_unity_instances",
      description:
        "List the Unity Editor instances currently running with the UnityMCP plugin - each with its " +
        "name, project path, and instanceId. Use this to discover what's open, then call " +
        "select_unity_instance (or pass `instance` on a call) to target one. Worth calling first " +
        "whenever more than one Editor might be open.",
      category: "Instance Management",
      tags: ["unity", "instance", "discovery"],
      // Discovery itself isn't tied to a single instance.
      requiresInstance: false,
      inputSchema: {
        type: "object",
        properties: {},
        additionalProperties: false,
      },
      returns: {
        type: "object",
        description:
          "The live instances and which one (if any) is selected for this session.",
        format: "JSON object: { selected, count, instances: [{ instanceId, name, projectPath, port, unityVersion, isSelected }] }",
      },
      examples: [
        {
          description: "See what's running before picking one",
          input: {},
          output:
            '{ "selected": null, "count": 2, "instances": [ { "name": "Garibaldi", "instanceId": "a1b2c3d4", "port": 51017, ... }, ... ] }',
        },
      ],
    };
  }

  async execute(_args: any, context: ToolContext) {
    const live = (await context.registry.list()).filter((i) => i.live);
    const selected = context.session.get();

    const isSelected = (instanceId: string, name: string) =>
      selected != null &&
      (instanceId === selected || name.toLowerCase() === selected.toLowerCase());

    if (live.length === 0) {
      return {
        content: [
          {
            type: "text",
            text:
              "No Unity instances are currently running with the UnityMCP plugin. Open a project " +
              "(with the plugin installed) and confirm UnityMCP > Debug Window shows 'Listening'.",
          },
        ],
      };
    }

    const payload = {
      selected: selected ?? null,
      count: live.length,
      instances: live.map((i) => ({
        instanceId: i.instanceId,
        name: i.name,
        projectPath: i.projectPath,
        port: i.port,
        unityVersion: i.unityVersion,
        isSelected: isSelected(i.instanceId, i.name),
      })),
    };

    return {
      content: [{ type: "text", text: JSON.stringify(payload, null, 2) }],
    };
  }
}
