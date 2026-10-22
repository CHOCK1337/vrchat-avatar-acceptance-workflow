import path, { dirname } from "path";
import { fileURLToPath } from "url";
import { loadTextResources } from "./TextResource.js";
import { Resource } from "./types.js";

export * from "./types.js";

// ES modules have no __dirname; derive it so we can locate the bundled text/ resources at runtime.
const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);

// build/resources/text — populated by the copy-resources build step.
const textResourceDir = path.join(__dirname, "text");

export async function getAllResources(): Promise<Resource[]> {
  // Code-defined resources would be listed here; today every resource is a text file under text/.
  const staticResources: Resource[] = [];

  const textResources = await loadTextResources(textResourceDir);

  return [...staticResources, ...textResources];
}
