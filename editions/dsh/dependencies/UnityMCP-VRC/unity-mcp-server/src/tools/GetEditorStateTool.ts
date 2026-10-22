import { ErrorCode, McpError } from "@modelcontextprotocol/sdk/types.js";
import { pageText } from "./commandResultCache.js";
import { Tool, ToolContext, ToolDefinition } from "./types.js";

export interface UnityEditorState {
  activeGameObjects: string[];
  selectedObjects: string[];
  playModeState: string;
  sceneHierarchy: any;
  projectStructure: {
    scenes?: string[];
    assets?: string[];
    [key: string]: string[] | undefined;
  };
  error?: string;
}

export class GetEditorStateTool implements Tool {
  getDefinition(): ToolDefinition {
    return {
      name: "get_editor_state",
      description:
        "Retrieve a snapshot of the Unity Editor's current state: active GameObjects, current selection, play-mode status, scene hierarchy, and the project's scenes/assets. The result is bounded for large scenes and projects (capped object and hierarchy counts) so it can't flood the context window. Note: while the Editor window is unfocused its update loop is throttled, so a call may not complete until you refocus the Editor (or set Preferences > General > Interaction Mode to 'No Throttling').",
      category: "Editor State",
      tags: ["unity", "editor", "state", "hierarchy", "project"],
      inputSchema: {
        type: "object",
        properties: {
          format: {
            type: "string",
            enum: ["Raw"],
            description:
              "Specify the output format:\n- Raw: Complete editor state including all available data",
            default: "Raw",
          },
        },
        additionalProperties: false,
      },
      returns: {
        type: "object",
        description:
          "Returns a JSON object containing the requested editor state information",
        format:
          "The response format varies based on the format parameter:\n- Raw: Full UnityEditorState object",
      },
      examples: [
        {
          description: "Get complete editor state",
          input: { format: "Raw" },
          output:
            '{ "activeGameObjects": ["Main Camera", "Directional Light"], ... }',
        },
      ],
    };
  }

  async execute(args: any, context: ToolContext) {
    const validFormats = ["Raw"];
    const format = (args?.format as string) || "Raw";

    if (args?.format && !validFormats.includes(format)) {
      throw new McpError(
        ErrorCode.InvalidParams,
        `Invalid format: "${format}". Valid formats are: ${validFormats.join(
          ", ",
        )}`,
      );
    }

    try {
      // Send command to Unity and await its correlated response (matched by request id).
      const timeoutMs = 60_000;
      const editorState: UnityEditorState =
        await context.unityConnection.sendRequest(
          "getEditorState",
          {},
          timeoutMs,
        );

      // Unity returns an { error } payload if it couldn't gather state (e.g. the Editor
      // stayed unfocused past the main-thread timeout) - surface it as a failure.
      if (editorState?.error) {
        throw new Error(editorState.error);
      }

      // "Raw" is the only format today; the switch leaves room for trimmed variants later.
      let responseData: any;
      switch (format) {
        case "Raw":
          responseData = editorState;
          break;
      }

      // Bounded at the source (capped object/asset/hierarchy counts), but a large project can
      // still exceed the byte cap - page it like execute_editor_command results.
      return {
        content: [
          {
            type: "text",
            text: pageText(JSON.stringify(responseData, null, 2)),
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
        `Failed to process editor state: ${
          error instanceof Error ? error.message : "Unknown error"
        }`,
      );
    }
  }
}
