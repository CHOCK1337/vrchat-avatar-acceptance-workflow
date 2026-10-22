import { ClearLogsTool } from "./ClearLogsTool.js";
import { ExecuteEditorCommandTool } from "./ExecuteEditorCommandTool.js";
import { GetCommandPageTool } from "./GetCommandPageTool.js";
import { GetEditorStateTool } from "./GetEditorStateTool.js";
import { GetLogsTool } from "./GetLogsTool.js";
import { GetObjectDetailsTool } from "./GetObjectDetailsTool.js";
import { ListUnityInstancesTool } from "./ListUnityInstancesTool.js";
import { SelectUnityInstanceTool } from "./SelectUnityInstanceTool.js";
import { TakeScreenshotTool } from "./TakeScreenshotTool.js";
import { Tool } from "./types.js";

export * from "./types.js";

export function getAllTools(): Tool[] {
  return [
    // Instance discovery/selection first - clients call these to pick which Editor to drive.
    new ListUnityInstancesTool(),
    new SelectUnityInstanceTool(),
    new GetEditorStateTool(),
    new ExecuteEditorCommandTool(),
    new GetLogsTool(),
    new ClearLogsTool(),
    new TakeScreenshotTool(),
    new GetObjectDetailsTool(),
    new GetCommandPageTool(),
  ];
}
