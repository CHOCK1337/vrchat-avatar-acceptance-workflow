import fs from "fs/promises";
import path from "path";
import { Resource, ResourceContext, ResourceDefinition } from "./types.js";

export class TextResource implements Resource {
  private filePath: string;
  private fileName: string;
  private description: string;

  constructor(filePath: string, description: string) {
    this.filePath = filePath;
    this.fileName = path.basename(filePath);
    this.description = description;
  }

  getDefinition(): ResourceDefinition {
    return {
      uri: `file:///${this.fileName}`,
      name: this.fileName,
      mimeType: "text/plain",
      description: this.description,
    };
  }

  async getContents(context: ResourceContext): Promise<string> {
    try {
      return await fs.readFile(this.filePath, "utf8");
    } catch (error) {
      console.error(`Error reading text file ${this.filePath}:`, error);
      return `Error loading text file: ${this.fileName}`;
    }
  }
}

// A resource's description is what the model reads to decide which doc is relevant — with both
// VRChat and Basis docs loaded, a good one-liner is how it picks the right project's notes. We take,
// in order: an explicit leading `<!-- description: … -->` comment, else the first Markdown heading,
// else the filename. Add the comment to any doc whose heading alone is too vague to disambiguate.
function extractDescription(content: string, fileName: string): string {
  const comment = content.match(/<!--\s*description:\s*([\s\S]+?)\s*-->/i);
  if (comment) return comment[1].trim().replace(/\s+/g, " ");

  const heading = content.match(/^\s*#{1,6}\s+(.+?)\s*$/m);
  if (heading) return heading[1].trim();

  return `Text file: ${fileName}`;
}

export async function loadTextResources(
  directoryPath: string,
): Promise<TextResource[]> {
  try {
    const textFiles = await fs.readdir(directoryPath);

    return await Promise.all(
      textFiles.map(async (file) => {
        const filePath = path.join(directoryPath, file);
        let description: string;
        try {
          const content = await fs.readFile(filePath, "utf8");
          description = extractDescription(content, file);
        } catch {
          description = `Text file: ${file}`;
        }
        return new TextResource(filePath, description);
      }),
    );
  } catch (error) {
    console.error("Error loading text resources:", error);
    return [];
  }
}
