#!/usr/bin/env node
import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import {
  CallToolRequestSchema,
  ErrorCode,
  ListResourcesRequestSchema,
  ListToolsRequestSchema,
  McpError,
  ReadResourceRequestSchema,
} from "@modelcontextprotocol/sdk/types.js";
import {
  RequestSender,
  unusableSender,
} from "./communication/UnityConnection.js";
import { InstanceRegistry } from "./communication/registry.js";
import { ConnectionPool } from "./communication/ConnectionPool.js";
import { InstanceSession } from "./session.js";
import { getAllResources, ResourceContext } from "./resources/index.js";
import { getAllTools, ToolContext } from "./tools/index.js";
import { requireComment } from "./tools/comment.js";
import { addInstanceParam } from "./tools/instanceParam.js";

class UnityMCPServer {
  private server: Server;
  // Discovery + routing across however many Unity Editors are running. The registry reads the shared
  // instance directory; the pool holds one HTTP connection per target; the session remembers which
  // instance this MCP process has selected as its default target.
  private registry: InstanceRegistry;
  private session: InstanceSession;
  private pool: ConnectionPool;
  private initialized = false;
  private shuttingDown = false;

  constructor() {
    this.server = new Server(
      {
        name: "unity-mcp-server",
        version: "0.1.0",
      },
      {
        capabilities: {
          tools: {},
          resources: {},
        },
      },
    );

    // No fixed endpoint anymore: each Unity Editor hosts its own HTTP server on a dynamic port and
    // publishes itself in the shared instance registry. A tool call's target instance is resolved to
    // a connection at call time (see setupTools).
    this.registry = new InstanceRegistry();
    this.session = new InstanceSession();
    this.pool = new ConnectionPool();

    // Log MCP-layer errors, and exit cleanly when interrupted or asked to terminate.
    this.server.onerror = (error) => console.error("[MCP Error]", error);
    process.on("SIGINT", () => this.shutdown(0));
    process.on("SIGTERM", () => this.shutdown(0));
  }

  /** Initialize the server asynchronously */
  async initialize() {
    if (this.initialized) return;
    
    await this.setupResources();
    this.setupTools();
    
    this.initialized = true;
  }

  /** Optional resources the user can include in Claude Desktop to give additional context to the LLM */
  private async setupResources() {
    const resources = await getAllResources();

    // Set up the resource request handler
    this.server.setRequestHandler(
      ListResourcesRequestSchema,
      async (request) => {
        return {
          resources: resources.map((resource) => resource.getDefinition()),
        };
      },
    );

    // Read resource contents
    this.server.setRequestHandler(
      ReadResourceRequestSchema,
      async (request) => {
        const uri = request.params.uri;
        const resource = resources.find((r) => r.getDefinition().uri === uri);

        if (!resource) {
          throw new McpError(
            ErrorCode.MethodNotFound,
            `Resource not found: ${uri}. Available resources: ${resources
              .map((r) => r.getDefinition().uri)
              .join(", ")}`,
          );
        }

        // Resources are static text and never call Unity, so they get a sender that errors if used
        // (there's no single instance to bind them to).
        const resourceContext: ResourceContext = {
          unityConnection: unusableSender(
            "Resources don't talk to a Unity instance.",
          ),
        };

        const content = await resource.getContents(resourceContext);

        return {
          contents: [
            {
              uri,
              mimeType: resource.getDefinition().mimeType,
              text: content,
            },
          ],
        };
      },
    );
  }

  private setupTools() {
    const tools = getAllTools();

    // Advertise the available tools and their schemas. requireComment injects a required `comment`
    // field; addInstanceParam adds the optional `instance` routing field to every tool that talks to
    // a specific Editor. Both are applied centrally, so all current and future tools get them
    // uniformly.
    this.server.setRequestHandler(ListToolsRequestSchema, async () => ({
      tools: tools.map((tool) =>
        addInstanceParam(requireComment(tool.getDefinition())),
      ),
    }));

    // Dispatch a tool call to the matching tool.
    this.server.setRequestHandler(CallToolRequestSchema, async (request) => {
      const { name, arguments: args } = request.params;

      const tool = tools.find((t) => t.getDefinition().name === name);

      // Unknown tool name: fail with the list of valid ones.
      if (!tool) {
        const availableTools = tools.map((t) => t.getDefinition().name);
        throw new McpError(
          ErrorCode.MethodNotFound,
          `Unknown tool: ${name}. Available tools are: ${availableTools.join(
            ", ",
          )}`,
        );
      }

      // Every call must carry a comment (the schema marks it required; this is the server-side
      // backstop for clients that don't validate). It's the operator-facing "why" shown live in the
      // Unity debug window's recent-calls panel.
      const comment =
        typeof args?.comment === "string" ? args.comment.trim() : "";
      if (!comment) {
        throw new McpError(
          ErrorCode.InvalidParams,
          `The 'comment' parameter is required: a brief, one-sentence note on why you're calling ${name}. ` +
            "It's shown live in the Unity Editor's UnityMCP debug window.",
        );
      }

      // Resolve which Unity instance this call targets and bind a connection to it. Tools that don't
      // talk to Unity (requiresInstance === false: list/select/get_command_page) skip resolution and
      // get a sender that errors if misused.
      //
      // Selection is required - several Editors may be running, so we never guess. The target is the
      // call's own `instance` arg, else the instance selected for this session; with neither, we fail
      // with a pointer to list/select. Routing reads the registry fresh each call, so a domain reload
      // (same pinned port) is transparent, while a closed Editor surfaces a clear error.
      //
      // Once resolved, the per-target connection (which retries a refused connection briefly to ride
      // out a reload) is bound to this call's comment so every request it sends is stamped with it.
      const def = tool.getDefinition();
      let unityConnection: RequestSender;

      if (def.requiresInstance === false) {
        unityConnection = unusableSender(
          `${name} does not target a specific Unity instance.`,
        );
      } else {
        const key =
          (typeof args?.instance === "string" && args.instance.trim()) ||
          this.session.get();
        if (!key) {
          throw new McpError(
            ErrorCode.InvalidParams,
            "No Unity instance selected. Call list_unity_instances to see what's running, then " +
              "select_unity_instance (or pass `instance` on this call). Selection is required " +
              "because more than one Editor may be open.",
          );
        }

        let record;
        try {
          record = await this.registry.resolve(String(key));
        } catch (err) {
          throw new McpError(
            ErrorCode.InvalidParams,
            err instanceof Error ? err.message : String(err),
          );
        }

        unityConnection = this.pool.forInstance(record).forComment(comment);
      }

      const toolContext: ToolContext = {
        unityConnection,
        registry: this.registry,
        session: this.session,
      };

      return await tool.execute(args, toolContext);
    });
  }

  private async cleanup() {
    this.pool.closeAll();
    await this.server.close();
  }

  /**
   * Tear down and exit. The client owns our lifetime, so when it disconnects we must exit rather
   * than linger: UnityConnection's 3s reconnect timer keeps the event loop alive indefinitely,
   * which previously left zombie servers endlessly reconnecting to the Editor and storming it on
   * every domain reload. Idempotent, and force-exits if a graceful close stalls.
   */
  private shutdown(code = 0): void {
    if (this.shuttingDown) return;
    this.shuttingDown = true;
    const force = setTimeout(() => process.exit(code), 2000);
    force.unref();
    this.cleanup()
      .catch(() => {})
      .then(() => process.exit(code));
  }

  async run() {
    await this.initialize();

    const transport = new StdioServerTransport();
    await this.server.connect(transport);
    console.error("Unity MCP server running on stdio");

    // The client owns our lifetime: when it goes away, its end of our stdio closes. Exit then, so
    // we don't linger reconnecting to Unity. server.onclose fires on transport close; the stdin
    // listeners are a backstop in case the transport doesn't surface the EOF itself.
    this.server.onclose = () => this.shutdown(0);
    process.stdin.on("end", () => this.shutdown(0));
    process.stdin.on("close", () => this.shutdown(0));

    // Brief settle before run() returns. The Unity client connects in the background
    // and reconnects on its own, so there's nothing to block on here.
    await new Promise<void>((resolve) => {
      setTimeout(resolve, 100);
    });
  }
}

const server = new UnityMCPServer();
server.run().catch(console.error);
