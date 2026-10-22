import { ErrorCode, McpError } from "@modelcontextprotocol/sdk/types.js";
import { Tool, ToolContext, ToolDefinition } from "./types.js";
import {
  MAX_RESPONSE_CHARS,
  formatPage,
  getPage,
} from "./commandResultCache.js";

// Companion to execute_editor_command: fetches later pages of a result that was too large to
// return in one call and was cached server-side. See docs/002-design-decisions.md.
export class GetCommandPageTool implements Tool {
  getDefinition(): ToolDefinition {
    return {
      name: "get_command_page",
      description:
        "Fetch a later page of a large execute_editor_command result that was truncated and " +
        "cached. Use the token and offset from the '[paged]' footer of a previous response. " +
        "Pages are raw text slices of the result's JSON: concatenate every page in offset " +
        "order, then parse the combined string as JSON.",
      category: "Editor Control",
      tags: ["unity", "editor", "command", "paging"],
      // Reads only the in-memory page cache, never Unity - so it needs no instance selected.
      requiresInstance: false,
      inputSchema: {
        type: "object",
        properties: {
          token: {
            type: "string",
            description:
              'Cache token from a paged execute_editor_command footer (e.g. "cmd_1748500000000_3").',
            minLength: 1,
          },
          offset: {
            type: "number",
            description:
              "Character offset to start from. Use the nextOffset reported in the previous footer.",
            minimum: 0,
            default: 0,
          },
          length: {
            type: "number",
            description: `Maximum number of characters to return (default ${MAX_RESPONSE_CHARS}).`,
            minimum: 1,
            default: MAX_RESPONSE_CHARS,
          },
        },
        required: ["token"],
        additionalProperties: false,
      },
      returns: {
        type: "object",
        description:
          "A page of the cached command result plus a footer indicating whether more remains.",
        format: "Text content: a raw chunk followed by a '[paged]' footer.",
      },
      examples: [
        {
          description: "Fetch the next page after the first 25k chars",
          input: { token: "cmd_1748500000000_3", offset: 25000 },
          output:
            "<chunk text>\\n\\n---\\n[paged] returned 25000 of 182334 chars (offset 25000). ...",
        },
      ],
    };
  }

  async execute(args: any, _context: ToolContext) {
    if (!args?.token || typeof args.token !== "string") {
      throw new McpError(
        ErrorCode.InvalidParams,
        "The token parameter is required and must be a string.",
      );
    }

    const offset = typeof args.offset === "number" ? args.offset : 0;
    const length =
      typeof args.length === "number" ? args.length : MAX_RESPONSE_CHARS;

    if (offset < 0) {
      throw new McpError(ErrorCode.InvalidParams, "offset must be >= 0.");
    }
    if (length < 1) {
      throw new McpError(ErrorCode.InvalidParams, "length must be >= 1.");
    }

    const page = getPage(args.token, offset, length);
    if (!page) {
      throw new McpError(
        ErrorCode.InvalidParams,
        "Result no longer cached (expired or evicted) — re-run the command.",
      );
    }

    return {
      content: [
        {
          type: "text",
          text: formatPage(args.token, page),
        },
      ],
    };
  }
}
