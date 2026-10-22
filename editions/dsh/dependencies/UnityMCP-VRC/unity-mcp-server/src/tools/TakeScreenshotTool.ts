import { ErrorCode, McpError } from "@modelcontextprotocol/sdk/types.js";
import { Tool, ToolContext, ToolDefinition } from "./types.js";

interface ScreenshotResult {
  base64: string;
  format: string;
  error?: string;
}

export class TakeScreenshotTool implements Tool {
  getDefinition(): ToolDefinition {
    return {
      name: "take_screenshot",
      description:
        "Capture a screenshot of the Unity Editor and return it as an image, so Claude can visually verify the result of edits. By default it renders the Scene view (what the developer is looking at); pass camera='game' to render the game camera instead. Note: while the Unity Editor window is unfocused its update loop is throttled, so a call may not complete until you refocus the Editor (or set Preferences > General > Interaction Mode to 'No Throttling').",
      category: "Visual",
      tags: ["unity", "editor", "screenshot", "image", "camera", "render", "scene view"],
      inputSchema: {
        type: "object",
        properties: {
          camera: {
            type: "string",
            enum: ["scene", "game"],
            description:
              "Which view to capture: 'scene' (the Scene view, default) or 'game' (Camera.main, falling back to any camera in the scene).",
            default: "scene",
          },
          width: {
            type: "number",
            description: "Output width in pixels (default 1280).",
          },
          height: {
            type: "number",
            description: "Output height in pixels (default 720).",
          },
          format: {
            type: "string",
            enum: ["jpg", "png"],
            description:
              "Image format: 'jpg' (default, smaller) or 'png' (lossless, crisper for text/UI).",
            default: "jpg",
          },
        },
        additionalProperties: false,
      },
      returns: {
        type: "object",
        description:
          "Returns an MCP image content block containing the captured screenshot.",
        format: "Image content with mimeType image/jpeg or image/png",
      },
      examples: [
        {
          description: "Capture the Scene view at default resolution",
          input: {},
          output: "<image: base64-encoded JPEG of the Scene view>",
        },
        {
          description: "Capture the game camera as a 1920x1080 PNG",
          input: { camera: "game", width: 1920, height: 1080, format: "png" },
          output: "<image: base64-encoded PNG of the game camera>",
        },
      ],
    };
  }

  async execute(args: any, context: ToolContext) {
    const timeoutMs = 60_000;
    try {
      // Send to Unity and await its correlated response (matched by request id). Unity applies
      // defaults for omitted fields.
      const result = (await context.unityConnection.sendRequest(
        "takeScreenshot",
        {
          camera: args?.camera,
          width: args?.width,
          height: args?.height,
          format: args?.format,
        },
        timeoutMs,
      )) as ScreenshotResult;

      // Unity returns an { error } payload if capture failed (e.g. no camera available, or the
      // Editor stayed unfocused past the main-thread timeout) - surface it as a failure.
      if (result?.error) {
        throw new Error(result.error);
      }

      const mimeType = result.format === "png" ? "image/png" : "image/jpeg";

      return {
        content: [
          {
            type: "image",
            data: result.base64,
            mimeType,
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
        `Failed to take screenshot: ${
          error instanceof Error ? error.message : "Unknown error"
        }`,
      );
    }
  }
}
