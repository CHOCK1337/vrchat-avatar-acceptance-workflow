import { Tool, ToolContext, ToolDefinition } from "./types.js";

export class ClearLogsTool implements Tool {
  getDefinition(): ToolDefinition {
    return {
      name: "clear_logs",
      description:
        "Clear the Unity Editor's buffered console logs. Use it to discard stale entries - e.g. a one-off compile error from a failed execute_editor_command snippet - so a later get_logs isn't ambiguous about whether an error is current. New logs accumulate from this point on. Note that execute_editor_command already returns a per-call 'errors' array scoped to just that command, which is the most reliable signal for whether a specific snippet failed.",
      category: "Debugging",
      tags: ["unity", "editor", "logs", "debugging", "console"],
      inputSchema: {
        type: "object",
        properties: {},
        additionalProperties: false,
      },
      returns: {
        type: "object",
        description: "Confirmation including how many buffered entries were cleared",
        format: 'JSON object with "cleared" (number) and "message" fields',
      },
      examples: [
        {
          description: "Clear stale logs before re-running a command",
          input: {},
          output: '{ "cleared": 42, "message": "Cleared 42 buffered log entries." }',
        },
      ],
    };
  }

  async execute(_args: any, context: ToolContext) {
    const res = await context.unityConnection.sendRequest("clearLogs", {});
    const cleared = (res?.cleared ?? 0) as number;
    return {
      content: [
        {
          type: "text",
          text: JSON.stringify(
            {
              cleared,
              message: `Cleared ${cleared} buffered log entr${
                cleared === 1 ? "y" : "ies"
              }.`,
            },
            null,
            2,
          ),
        },
      ],
    };
  }
}
