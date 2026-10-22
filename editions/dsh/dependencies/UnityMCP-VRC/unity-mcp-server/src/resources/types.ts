import { RequestSender } from "../communication/UnityConnection.js";

export interface ResourceDefinition {
  uri: string;
  name: string;
  mimeType: string;
  description?: string;
}

export interface ResourceContext {
  // Resources are static text today and don't call Unity; this is the minimal sender interface (in
  // practice an unusableSender) rather than a concrete connection bound to one instance.
  unityConnection: RequestSender;
}

// A readable MCP resource (exposed as file:///<name>) the client can pull in for extra context.
export interface Resource {
  getDefinition(): ResourceDefinition;
  getContents(context: ResourceContext): Promise<string>;
}
