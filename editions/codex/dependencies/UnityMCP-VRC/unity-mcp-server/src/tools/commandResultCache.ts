// Shared, in-memory cache + paging for oversized tool results.
//
// A single tool result can blow out the model's context window: an arbitrary
// execute_editor_command blob ("list every GameObject and its components"), or a wide
// get_object_details / get_editor_state dump. Tools that know their shape still cap at the source
// (element counts, recursion depth) to keep the common case small, but those caps bound breadth,
// not total bytes - so this is the generic byte-level backstop: when a serialized result exceeds
// MAX_RESPONSE_CHARS we cache the full snapshot under a token and hand back only the first page.
// The agent pulls later pages via the get_command_page tool, slicing the *cached* snapshot so
// paging is consistent even for non-idempotent commands. See docs/002-design-decisions.md.

/** Max chars returned in a single page before we cache + paginate (~6-8k tokens). */
export const MAX_RESPONSE_CHARS = 25_000;

const TTL_MS = 5 * 60_000; // evict entries older than 5 minutes
const MAX_ENTRIES = 20; // and cap total entries (evict oldest first)

interface CachedResult {
  fullText: string;
  createdAt: number;
}

// Map iteration order is insertion order, so the first key is always the oldest entry.
const cache = new Map<string, CachedResult>();
let counter = 0;

export interface Page {
  chunk: string;
  total: number;
  offset: number;
  /** Where the next page starts; equals `total` when this is the final page. */
  nextOffset: number;
}

/** Drop expired entries, then trim the oldest until we're within MAX_ENTRIES. */
function evict(now: number): void {
  for (const [token, entry] of cache) {
    if (now - entry.createdAt > TTL_MS) cache.delete(token);
  }
  while (cache.size > MAX_ENTRIES) {
    const oldest = cache.keys().next().value as string | undefined;
    if (oldest === undefined) break;
    cache.delete(oldest);
  }
}

/** Cache the full text and return its token. Eviction runs lazily here (no timer). */
export function put(fullText: string): string {
  const now = Date.now(); // Date.now() is fine in the server; only banned in workflow scripts.
  const token = `cmd_${now}_${counter++}`;
  cache.set(token, { fullText, createdAt: now });
  evict(now);
  return token;
}

/**
 * Return a page of a cached result, or null if the token is missing/expired/evicted.
 * `offset`/`length` are clamped to the snapshot; an offset at/past the end yields an empty
 * final chunk (nextOffset === total).
 */
export function getPage(
  token: string,
  offset: number,
  length: number,
): Page | null {
  evict(Date.now());
  const entry = cache.get(token);
  if (!entry) return null;

  const total = entry.fullText.length;
  const start = Math.max(0, Math.min(offset, total));
  const end = Math.min(start + Math.max(0, length), total);
  return {
    chunk: entry.fullText.slice(start, end),
    total,
    offset: start,
    nextOffset: end,
  };
}

/**
 * Render a page as the tool's text payload: the raw chunk followed by a footer telling the
 * agent how to continue. Chunks are raw slices of serialized JSON, so they must be
 * concatenated in offset order before parsing.
 */
export function formatPage(token: string, page: Page): string {
  const { chunk, total, offset, nextOffset } = page;
  const returned = nextOffset - offset;

  const footer = [
    "---",
    `[paged] returned ${returned} of ${total} chars (offset ${offset}).`,
  ];
  if (nextOffset < total) {
    footer.push(
      `More available: call get_command_page { token: "${token}", offset: ${nextOffset} }.`,
    );
  } else {
    footer.push("[end of result] no more pages.");
  }
  footer.push(
    "Chunks are raw text — concatenate all pages in offset order, then parse the combined JSON.",
  );

  return `${chunk}\n\n${footer.join("\n")}`;
}

/**
 * The cap-or-page decision in one place: return `text` unchanged if it fits within
 * MAX_RESPONSE_CHARS, otherwise cache the full snapshot and return its first page (with a footer
 * telling the agent how to fetch the rest via get_command_page). Tools drop the result straight
 * into a text content block.
 */
export function pageText(text: string): string {
  if (text.length <= MAX_RESPONSE_CHARS) return text;
  const token = put(text);
  const page = getPage(token, 0, MAX_RESPONSE_CHARS)!; // just inserted — never null
  return formatPage(token, page);
}
