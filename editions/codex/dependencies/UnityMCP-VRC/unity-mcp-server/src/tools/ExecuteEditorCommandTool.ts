import { ErrorCode, McpError } from "@modelcontextprotocol/sdk/types.js";
import {
  Tool,
  ToolContext,
  ToolDefinition,
} from "./types.js";
import { pageText } from "./commandResultCache.js";

export interface CommandResult {
  result: any;
  logs: string[];
  errors: string[];
  warnings: string[];
  executionSuccess: boolean;
  errorDetails?: {
    message: string;
    stackTrace: string;
    type: string;
  };
}

export class ExecuteEditorCommandTool implements Tool {
  getDefinition(): ToolDefinition {
    return {
      name: "execute_editor_command",
      description:
        "Compile and run C# inside the running Unity Editor; returns the command's result plus any console logs it produced. Has full access to the UnityEditor and UnityEngine APIs, so it can inspect or modify GameObjects, components, and project assets. Oversized results are paged - fetch the rest with get_command_page.",
      category: "Editor Control",
      tags: ["unity", "editor", "command", "c#", "scripting"],
      inputSchema: {
        type: "object",
        properties: {
          code: {
            type: "string",
            description: `C# code file to execute in the Unity Editor context.
The code has access to all UnityEditor and UnityEngine APIs.
Include any necessary using directives at the top of the code.
The code must have a EditorCommand class with a static Execute method that returns an object.`,
            minLength: 1,
          },
          timeoutMs: {
            type: "number",
            description:
              "How long to wait (ms) for Unity to return a result before giving up. Defaults to 60000 (60s). Raise it for known-heavy operations (large asset imports, bulk scene edits) that would otherwise time out with no partial result. Clamped to 1000-300000.",
            minimum: 1000,
            maximum: 300000,
            default: 60000,
          },
        },
        required: ["code"],
        additionalProperties: false,
      },
      returns: {
        type: "object",
        description:
          "Returns the execution result and any logs generated during execution",
        format: 'JSON object containing "result" and "logs" fields',
      },
      errorHandling: {
        description: "Common error scenarios and their handling:",
        scenarios: [
          {
            error: "Compilation error",
            handling: "Returns compilation error details in logs",
          },
          {
            error: "Runtime exception",
            handling: "Returns exception details and stack trace",
          },
          {
            error: "Timeout",
            handling:
              "Request times out after timeoutMs (default 60s; e.g. the Editor stayed unfocused or busy). Pass a larger timeoutMs for known-heavy operations.",
          },
        ],
      },
      examples: [
        {
          description: "Center selected object",
          input: {
            code: `using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

public class EditorCommand
{
  public static object Execute()
  {
      Selection.activeGameObject.transform.position = Vector3.zero;
      EditorApplication.isPlaying = !EditorApplication.isPlaying;
      return ""Success"";
  }
}`,
          },
          output:
            '{ "result": "Success", "logs": [...], "executionTime": "12ms", "status": "success" }',
        },
      ],
    };
  }

  async execute(args: any, context: ToolContext) {
    if (!args?.code) {
      throw new McpError(
        ErrorCode.InvalidParams,
        "The code parameter is required",
      );
    }

    if (typeof args.code !== "string") {
      throw new McpError(
        ErrorCode.InvalidParams,
        "The code parameter must be a string",
      );
    }

    if (args.code.trim().length === 0) {
      throw new McpError(
        ErrorCode.InvalidParams,
        "The code parameter cannot be empty",
      );
    }

    if (args.timeoutMs !== undefined && typeof args.timeoutMs !== "number") {
      throw new McpError(
        ErrorCode.InvalidParams,
        "The timeoutMs parameter must be a number",
      );
    }

    // Default to 60s, clamped to the schema bounds so a stray value can't disable the timeout
    // or set an absurd one.
    const timeoutMs = Math.min(
      300_000,
      Math.max(1_000, args.timeoutMs ?? 60_000),
    );

    try {
      const commandStartTime = Date.now();

      // Send the command to Unity and await its result. The payload already carries this command's
      // own scoped logs/errors/warnings (captured plugin-side around the snippet), so there's no
      // separate log buffer to slice here.
      const result = await context.unityConnection.sendRequest(
        "executeEditorCommand",
        { code: args.code },
        timeoutMs,
      );

      const executionTime = Date.now() - commandStartTime;

      const text = JSON.stringify(
        {
          result,
          executionTime: `${executionTime}ms`,
          status: "success",
        },
        null,
        2,
      );

      // Generic backstop against context-window blowout: a command's result shape is unknown, so
      // it has no source-level cap. pageText returns it as-is if it fits, else caches the full
      // snapshot and returns the first page (agent fetches the rest via get_command_page).
      return { content: [{ type: "text", text: pageText(text) }] };
    } catch (error) {
      // Map a few well-known failure substrings to clearer, more actionable messages;
      // anything else falls through to the generic wrapper below.
      if (error instanceof Error) {
        if (error.message.includes("timed out")) {
          throw new McpError(ErrorCode.InternalError, error.message);
        }

        if (error.message.includes("NullReferenceException")) {
          throw new McpError(
            ErrorCode.InvalidParams,
            "The code attempted to access a null object. Please check that all GameObject references exist.",
          );
        }

        if (error.message.includes("CompileError")) {
          throw new McpError(
            ErrorCode.InvalidParams,
            "C# compilation error. Please check the syntax of your code.",
          );
        }
      }

      // Generic error fallback
      throw new McpError(
        ErrorCode.InternalError,
        `Failed to execute command: ${
          error instanceof Error ? error.message : "Unknown error"
        }`,
      );
    }
  }
}
