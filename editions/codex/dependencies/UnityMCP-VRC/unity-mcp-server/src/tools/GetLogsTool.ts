import { LogEntry, Tool, ToolContext, ToolDefinition } from "./types.js";

export class GetLogsTool implements Tool {
  getDefinition(): ToolDefinition {
    return {
      name: "get_logs",
      description:
        "Retrieve recent Unity console logs (Log, Warning, Error, Exception) from the Unity Editor's log buffer, filtering by type, message or stack-trace substring, timestamp range, and which fields to return. The plugin keeps roughly the last 1000 entries.",
      category: "Debugging",
      tags: ["unity", "editor", "logs", "debugging", "console"],
      inputSchema: {
        type: "object",
        properties: {
          types: {
            type: "array",
            items: {
              type: "string",
              enum: ["Log", "Warning", "Error", "Exception"],
              description: "Log entry types to include",
            },
            description:
              "Filter logs by type. If not specified, all types are included.",
            examples: [
              ["Error", "Exception"],
              ["Log", "Warning"],
            ],
          },
          count: {
            type: "number",
            description: "Maximum number of log entries to return",
            minimum: 1,
            maximum: 1000,
            default: 100,
          },
          fields: {
            type: "array",
            items: {
              type: "string",
              enum: ["message", "stackTrace", "logType", "timestamp"],
            },
            description:
              "Specify which fields to include in the output. If not specified, all fields are included.",
            examples: [
              ["message", "logType"],
              ["message", "stackTrace", "timestamp"],
            ],
          },
          messageContains: {
            type: "string",
            description:
              "Filter logs to only include entries where the message contains this string (case-sensitive)",
            minLength: 1,
          },
          stackTraceContains: {
            type: "string",
            description:
              "Filter logs to only include entries where the stack trace contains this string (case-sensitive)",
            minLength: 1,
          },
          timestampAfter: {
            type: "string",
            description: "Filter logs after this ISO timestamp (inclusive)",
            format: "date-time",
            example: "2024-01-14T00:00:00Z",
          },
          timestampBefore: {
            type: "string",
            description: "Filter logs before this ISO timestamp (inclusive)",
            format: "date-time",
            example: "2024-01-14T23:59:59Z",
          },
        },
        additionalProperties: false,
      },
      returns: {
        type: "array",
        description:
          "Returns an array of log entries matching the specified filters",
        format: "Array of objects containing requested log entry fields",
      },
      examples: [
        {
          description: "Get recent error logs",
          input: {
            types: ["Error", "Exception"],
            count: 10,
            fields: ["message", "timestamp"],
          },
          output:
            '[{"message": "NullReferenceException", "timestamp": "2024-01-14T12:00:00Z"}, ...]',
        },
        {
          description: "Search logs for specific message",
          input: {
            messageContains: "Player",
            fields: ["message", "logType"],
          },
          output:
            '[{"message": "Player position updated", "logType": "Log"}, ...]',
        },
      ],
    };
  }

  async execute(args: any, context: ToolContext) {
    const options = {
      types: args?.types as string[] | undefined,
      count: (args?.count as number) || 100,
      fields: args?.fields as string[] | undefined,
      messageContains: args?.messageContains as string | undefined,
      stackTraceContains: args?.stackTraceContains as string | undefined,
      timestampAfter: args?.timestampAfter as string | undefined,
      timestampBefore: args?.timestampBefore as string | undefined,
    };

    const res = await context.unityConnection.sendRequest("getLogs", {});
    const buffer = (res?.logs ?? []) as LogEntry[];
    const logs = this.filterLogs(buffer, options);
    return {
      content: [
        {
          type: "text",
          text: JSON.stringify(logs, null, 2),
        },
      ],
    };
  }

  private filterLogs(
    logBuffer: LogEntry[],
    options: {
      types?: string[];
      count?: number;
      fields?: string[];
      messageContains?: string;
      stackTraceContains?: string;
      timestampAfter?: string;
      timestampBefore?: string;
    },
  ): any[] {
    const {
      types,
      count = 100,
      fields,
      messageContains,
      stackTraceContains,
      timestampAfter,
      timestampBefore,
    } = options;

    // Keep entries matching every supplied filter (type, message/stack substring, timestamp range).
    let filteredLogs = logBuffer.filter((log) => {
      if (types && !types.includes(log.logType)) return false;

      if (messageContains && !log.message.includes(messageContains))
        return false;

      if (stackTraceContains && !log.stackTrace.includes(stackTraceContains))
        return false;

      if (timestampAfter && new Date(log.timestamp) < new Date(timestampAfter))
        return false;
      if (
        timestampBefore &&
        new Date(log.timestamp) > new Date(timestampBefore)
      )
        return false;

      return true;
    });

    // Keep the most-recent `count`, then project to the requested fields if any.
    filteredLogs = filteredLogs.slice(-count);

    if (fields?.length) {
      return filteredLogs.map((log) => {
        const selectedFields: Partial<LogEntry> = {};
        fields.forEach((field) => {
          if (
            field in log &&
            (field === "message" ||
              field === "stackTrace" ||
              field === "logType" ||
              field === "timestamp")
          ) {
            selectedFields[field as keyof LogEntry] =
              log[field as keyof LogEntry];
          }
        });
        return selectedFields;
      });
    }

    return filteredLogs;
  }
}
