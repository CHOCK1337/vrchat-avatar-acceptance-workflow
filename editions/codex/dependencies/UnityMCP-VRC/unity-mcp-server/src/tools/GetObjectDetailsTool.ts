import { ErrorCode, McpError } from "@modelcontextprotocol/sdk/types.js";
import { pageText } from "./commandResultCache.js";
import { Tool, ToolContext, ToolDefinition } from "./types.js";

interface Vec3 {
  x: number;
  y: number;
  z: number;
}

export interface ObjectDetails {
  name: string;
  path?: string;
  active?: boolean;
  activeInHierarchy?: boolean;
  tag?: string;
  layer?: string;
  transform?: {
    position: Vec3;
    rotation: Vec3;
    localScale: Vec3;
    lossyScale: Vec3;
  };
  childCount?: number;
  children?: string[];
  components?: {
    type: string;
    data: Record<string, any>;
  }[];
  error?: string;
}

export class GetObjectDetailsTool implements Tool {
  getDefinition(): ToolDefinition {
    return {
      name: "get_object_details",
      description:
        "Retrieve detailed information about a GameObject in the active scene: its transform (including world-space lossyScale), tag, layer, active state, immediate children, and per-component fields/properties. Also reports size info that the inspector can't easily give — Renderer world-space bounds, MeshFilter/SkinnedMeshRenderer vertex counts and local bounds, and shared mesh/material names. Note: while the Unity Editor window is unfocused its update loop is throttled, so a call may not complete until you refocus the Editor (or set Preferences > General > Interaction Mode to 'No Throttling').",
      category: "Editor State",
      tags: [
        "unity",
        "editor",
        "gameobject",
        "inspector",
        "components",
        "transform",
        "bounds",
      ],
      inputSchema: {
        type: "object",
        properties: {
          objectName: {
            type: "string",
            description:
              "The GameObject to inspect. Accepts a plain name ('Main Camera') or a hierarchy path from a scene root ('SSX_GARI/Props/default').",
          },
          includeInactive: {
            type: "boolean",
            description:
              "Also search disabled/inactive GameObjects (default false). Plain-name lookup of active objects is fastest.",
            default: false,
          },
        },
        required: ["objectName"],
        additionalProperties: false,
      },
      returns: {
        type: "object",
        description:
          "Returns a JSON object with the GameObject's transform, tag, layer, active state, children, and per-component field/property data (including bounds and mesh/material info).",
        format: "JSON ObjectDetails object",
      },
      examples: [
        {
          description: "Inspect the Main Camera",
          input: { objectName: "Main Camera" },
          output:
            '{ "name": "Main Camera", "path": "Main Camera", "active": true, "components": [ ... ] }',
        },
        {
          description: "Inspect an inactive object by hierarchy path",
          input: { objectName: "SSX_GARI/Props/default", includeInactive: true },
          output:
            '{ "name": "default", "path": "SSX_GARI/Props/default", "components": [ { "type": "MeshFilter", "data": { "vertexCount": 12345, "localBounds": { ... } } } ] }',
        },
      ],
    };
  }

  async execute(args: any, context: ToolContext) {
    const objectName = args?.objectName as string;

    if (!objectName || typeof objectName !== "string") {
      throw new McpError(
        ErrorCode.InvalidParams,
        "Missing required parameter: 'objectName' (a GameObject name or hierarchy path to inspect).",
      );
    }

    const includeInactive = args?.includeInactive === true;
    const timeoutMs = 60_000;

    try {
      // Send to Unity and await its correlated response (matched by request id).
      const details = (await context.unityConnection.sendRequest(
        "getGameObjectDetails",
        { objectName, includeInactive },
        timeoutMs,
      )) as ObjectDetails;

      // Unity returns an { error } payload if the object wasn't found or gathering failed -
      // surface it as a failure rather than returning a half-empty object.
      if (details?.error) {
        throw new Error(details.error);
      }

      // Bounded at the source (capped collection elements + recursion depth), but a wide object
      // can still exceed the byte cap - page it the same way execute_editor_command results are.
      return {
        content: [
          {
            type: "text",
            text: pageText(JSON.stringify(details, null, 2)),
          },
        ],
      };
    } catch (error) {
      // Surface timeouts as-is (already actionable); wrap anything else.
      if (error instanceof Error && error.message.includes("timed out")) {
        throw new McpError(ErrorCode.InternalError, error.message);
      }

      throw new McpError(
        ErrorCode.InternalError,
        `Failed to get object details: ${
          error instanceof Error ? error.message : "Unknown error"
        }`,
      );
    }
  }
}
