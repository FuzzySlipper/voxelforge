/**
 * Coalescing utility for async operations.
 *
 * Ensures at most one invocation of the wrapped async function is in-flight at
 * any time. If a call arrives while one is already in-flight, the call is
 * **deferred** — it runs once more after the current call completes, collapsing
 * multiple overlapping requests into a single deferred execution.
 *
 * Useful for operations like mesh refresh where:
 *   - Concurrent requests are harmful or wasteful.
 *   - The latest state should always be picked up eventually.
 *   - Stale intermediate results are acceptable as long as the UI catches up.
 *
 * ## Error isolation
 *
 * The in-flight call's error propagates only to its own caller. A deferred
 * call's error is caught and logged via `console.warn` — coalesced callers
 * have already received `undefined` and are not affected.
 */

export interface Coalescer<TResult> {
  /** Schedule an invocation. Returns the result of the actual execution, or
   *  `undefined` if the call was coalesced (deferred to run after the
   *  current in-flight call completes). */
  (): Promise<TResult | undefined>;
}

/**
 * Create a coalescing wrapper around an async function.
 *
 * @param fn — The async function to coalesce. Must return a promise.
 * @returns A coalesced version of `fn`.
 *
 * @example
 * ```ts
 * const refreshMesh = createCoalescer(async () => {
 *   const data = await bridge.request("bridge:mesh-snapshot", { ... });
 *   scene.buildMeshFromSnapshot(data);
 * });
 *
 * // During rapid edits, overlapping calls are collapsed:
 * await refreshMesh(); // starts a request
 * await refreshMesh(); // deferred — runs once after the first completes
 * await refreshMesh(); // also deferred — same deferred run
 * ```
 */
export function createCoalescer<TResult>(
  fn: () => Promise<TResult>,
): Coalescer<TResult> {
  let inProgress = false;
  let pending = false;

  async function run(): Promise<TResult | undefined> {
    if (inProgress) {
      pending = true;
      return undefined;
    }

    inProgress = true;
    let result: TResult | undefined;
    let error: unknown = undefined;

    try {
      result = await fn();
    } catch (e) {
      error = e;
    }

    // Clear in-progress *before* checking pending so a deferred call can
    // properly re-enter.
    inProgress = false;

    // If a call was deferred while we were running, fire a deferred execution
    // asynchronously.  We fire-and-forget because:
    //   - The coalesced caller already got `undefined`.
    //   - The in-flight caller's result/error is independent.
    //   - Any deferred error is logged but does not surface to either caller.
    if (pending) {
      pending = false;
      // Do not await — the deferred run is a logical continuation that
      // should not block or alter the current caller's promise.
      run().catch((deferredErr) => {
        console.warn("[createCoalescer] deferred call failed:", deferredErr);
      });
    }

    // Propagate the *first* (this) execution's error to its caller.
    if (error !== undefined) {
      throw error;
    }

    return result;
  }

  return run;
}
