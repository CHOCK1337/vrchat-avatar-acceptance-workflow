import { UnityConnection } from "./UnityConnection.js";
import { InstanceRecord } from "./registry.js";

// One UnityConnection per target instance, reused across calls.
//
// A connection is just a base URL plus retry logic, but caching it lets shutdown stop every target's
// retries at once, and keeps the (cosmetic) instance label stable. Keyed by instanceId, not port, so
// a record whose port changed (e.g. its pinned port couldn't be reclaimed after a reload) rebuilds
// onto the new address instead of talking to a stale one.
export class ConnectionPool {
  private readonly byInstance = new Map<
    string,
    { port: number; conn: UnityConnection }
  >();

  forInstance(rec: InstanceRecord): UnityConnection {
    const existing = this.byInstance.get(rec.instanceId);
    if (existing && existing.port === rec.port) return existing.conn;
    if (existing) existing.conn.close();

    const conn = new UnityConnection({
      baseUrl: `http://localhost:${rec.port}/`,
      label: rec.name,
    });
    this.byInstance.set(rec.instanceId, { port: rec.port, conn });
    return conn;
  }

  closeAll(): void {
    for (const { conn } of this.byInstance.values()) conn.close();
    this.byInstance.clear();
  }
}
