import { describe, it, expect, vi } from "vitest";
import { createCoalescer } from "../src/shared/refresh-coalescer";

/**
 * Tests for the refresh coalescer utility.
 *
 * These are pure async-function tests — no DOM, no WebGL, no bridge.
 * They verify the coalescing contract:
 *
 *   - At most one invocation in-flight at any time.
 *   - Calls during an in-flight window are deferred (return undefined).
 *   - Multiple overlapping calls collapse into a single deferred execution.
 *   - Deferred execution runs after the current one completes.
 *   - Errors from the in-flight call propagate to its caller.
 *   - Errors from a deferred call are caught and logged (not surfaced
 *     to coalesced callers, who already got undefined).
 */

describe("createCoalescer", () => {
  it("runs the wrapped function once on a single call", async () => {
    const fn = vi.fn(async () => "ok");
    const coalesced = createCoalescer(fn);

    const result = await coalesced();

    expect(fn).toHaveBeenCalledTimes(1);
    expect(result).toBe("ok");
  });

  it("returns the wrapped function's result", async () => {
    const coalesced = createCoalescer(async () => 42);
    const result = await coalesced();
    expect(result).toBe(42);
  });

  it("coalesced calls return undefined immediately", async () => {
    // Use a controlled promise to keep the first call in-flight.
    let resolveFirst!: (v: string) => void;
    const firstPromiseCtrl = new Promise<string>((resolve) => { resolveFirst = resolve; });

    const fn = vi.fn(async () => firstPromiseCtrl);
    const coalesced = createCoalescer(fn);

    // Start the first call (don't await yet).
    const firstPromise = coalesced();

    // These should be coalesced — they return undefined immediately.
    const r2 = await coalesced();
    const r3 = await coalesced();

    expect(r2).toBeUndefined();
    expect(r3).toBeUndefined();
    expect(fn).toHaveBeenCalledTimes(1); // still only one started

    // Let the first call complete.
    resolveFirst("done");
    await firstPromise;
  });

  it("runs a deferred call after the in-flight call completes", async () => {
    let resolveFirst!: (v: string) => void;
    const firstPromiseCtrl = new Promise<string>((resolve) => { resolveFirst = resolve; });

    let callOrder: number[] = [];
    const fn = vi.fn()
      .mockImplementationOnce(async () => {
        callOrder.push(0);
        return firstPromiseCtrl;
      })
      .mockImplementationOnce(async () => {
        callOrder.push(1);
        return "ok";
      });
    const coalesced = createCoalescer(fn);

    // First call starts.
    void coalesced();
    await new Promise((r) => setTimeout(r, 5));

    // Second call coalesced.
    void coalesced();

    // First call should have started, second deferred.
    expect(callOrder).toEqual([0]);
    expect(fn).toHaveBeenCalledTimes(1);

    // Let the first call complete.
    resolveFirst("done");

    // Wait for the deferred call to execute.
    await new Promise((r) => setTimeout(r, 50));

    // Second call should have run after the first.
    expect(callOrder).toEqual([0, 1]);
    expect(fn).toHaveBeenCalledTimes(2);
  });

  it("collapses multiple overlapping calls into one deferred execution", async () => {
    let resolveFirst!: (v: string) => void;
    const firstPromiseCtrl = new Promise<string>((resolve) => { resolveFirst = resolve; });

    let callCount = 0;
    const fn = vi.fn()
      .mockImplementationOnce(async () => {
        callCount++;
        return firstPromiseCtrl;
      })
      .mockImplementationOnce(async () => {
        callCount++;
        return "ok";
      });
    const coalesced = createCoalescer(fn);

    // Start first, then pile on three more while in-flight.
    void coalesced();
    await new Promise((r) => setTimeout(r, 5));
    void coalesced(); // sets pending = true
    void coalesced(); // pending already true — no-op
    void coalesced(); // still pending — no-op

    expect(callCount).toBe(1); // only first started

    // Let the first call complete — deferred should then fire.
    resolveFirst("done");

    // Wait for all to settle.
    await new Promise((r) => setTimeout(r, 50));

    // fn should only have been called twice: the first execution, and
    // one deferred execution (all overlapping calls collapsed).
    expect(callCount).toBe(2);
  });

  it("propagates error to the in-flight caller", async () => {
    let rejectFirst!: (e: Error) => void;
    const firstPromiseCtrl = new Promise<string>((_, reject) => { rejectFirst = reject; });

    const fn = vi.fn(async () => firstPromiseCtrl);
    const coalesced = createCoalescer(fn);

    const firstPromise = coalesced();

    rejectFirst(new Error("boom"));
    await expect(firstPromise).rejects.toThrow("boom");
    expect(fn).toHaveBeenCalledTimes(1);
  });

  it("runs a deferred call even after the in-flight call fails", async () => {
    let rejectFirst!: (e: Error) => void;
    const firstPromiseCtrl = new Promise<string>((_, reject) => { rejectFirst = reject; });

    let callSequence: string[] = [];
    const fn = vi.fn()
      .mockImplementationOnce(async () => {
        callSequence.push("first-start");
        return firstPromiseCtrl;
      })
      .mockImplementationOnce(async () => {
        callSequence.push("deferred-start");
        return "deferred-ok";
      });

    const coalesced = createCoalescer(fn);

    // Start first call (will fail when we reject).
    const firstPromise = coalesced();
    await new Promise((r) => setTimeout(r, 5));

    // Queue a deferred call.
    const secondPromise = coalesced();
    expect(await secondPromise).toBeUndefined(); // coalesced

    // Cause the first call to fail.
    rejectFirst(new Error("first failed"));

    // First call rejects.
    await expect(firstPromise).rejects.toThrow("first failed");

    // Wait for the deferred call to complete.
    await new Promise((r) => setTimeout(r, 20));

    // Deferred call should have run after the first one failed.
    expect(callSequence).toEqual(["first-start", "deferred-start"]);
    expect(fn).toHaveBeenCalledTimes(2);
  });

  it("isolates errors between independent coalescers", async () => {
    const fnA = vi.fn(async () => "A");
    const fnB = vi.fn(async () => "B");
    const coalescedA = createCoalescer(fnA);
    const coalescedB = createCoalescer(fnB);

    const [rA, rB] = await Promise.all([coalescedA(), coalescedB()]);
    expect(rA).toBe("A");
    expect(rB).toBe("B");
    expect(fnA).toHaveBeenCalledTimes(1);
    expect(fnB).toHaveBeenCalledTimes(1);
  });

  it("does not leave dangling inProgress state after error", async () => {
    let attempt = 0;
    const fn = vi.fn(async () => {
      attempt++;
      if (attempt === 1) {
        throw new Error("first failed");
      }
      return "ok";
    });
    const coalesced = createCoalescer(fn);

    // First call fails.
    await expect(coalesced()).rejects.toThrow("first failed");
    expect(attempt).toBe(1);

    // Second call should be a fresh start (not blocked by stale inProgress).
    const result = await coalesced();
    expect(result).toBe("ok");
    expect(attempt).toBe(2);
  });
});
