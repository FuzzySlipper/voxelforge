/**
 * Capture readiness signal management.
 * Tracks texture loading progress and sets a data attribute on the document body
 * when the scene is ready for deterministic screenshot capture.
 */

/**
 * Manages capture readiness state.
 * Tracks pending texture loads and signals readiness via DOM data attribute.
 */
export class CaptureReadyManager {
  private _pendingLoads = 0;
  private _ready = false;
  private _readySet = false;
  private _handlers: Array<() => void> = [];

  /**
   * Register a pending texture load.
   * Increments the pending counter and resets ready state.
   */
  onTextureLoadStart(): void {
    this._pendingLoads++;
    this._ready = false;
  }

  /**
   * Called when a texture load completes (success or failure).
   * Decrements the pending counter and checks readiness.
   */
  onTextureLoadEnd(): void {
    this._pendingLoads--;
    if (this._pendingLoads < 0) this._pendingLoads = 0;
    this.checkAndSignal();
  }

  /**
   * Get the number of pending texture loads.
   */
  get pendingLoads(): number {
    return this._pendingLoads;
  }

  /**
   * Check if the scene is ready for capture.
   */
  get isReady(): boolean {
    return this._ready;
  }

  /**
   * Register a callback that fires when capture readiness is achieved.
   * If already ready, calls immediately.
   */
  onReady(handler: () => void): void {
    if (this._ready) {
      handler();
    } else {
      this._handlers.push(handler);
    }
  }

  /**
   * Reset the readiness state (e.g., when clearing and rebuilding the scene).
   */
  reset(): void {
    this._pendingLoads = 0;
    this._ready = false;
    this._readySet = false;
    this._handlers = [];
  }

  /**
   * Manually force capture-ready state (useful when no textures are pending).
   */
  forceReady(): void {
    this._pendingLoads = 0;
    this.checkAndSignal();
  }

  /**
   * Set the DOM capture-ready data attribute.
   * When `capture=1` is in the URL, sets `document.body.dataset.captureReady = "true"`.
   */
  setDomCaptureReady(): void {
    if (typeof document === "undefined") return;
    if (this._readySet) return;
    this._readySet = true;
    document.body.dataset.captureReady = "true";
    console.log("[CaptureReady] captureReady set");
  }

  private checkAndSignal(): void {
    if (this._pendingLoads > 0) return;
    if (this._ready) return;

    this._ready = true;
    this.setDomCaptureReady();

    // Fire all registered handlers
    const handlers = this._handlers;
    this._handlers = [];
    for (const handler of handlers) {
      try {
        handler();
      } catch (err) {
        console.error("[CaptureReady] handler error:", err);
      }
    }
  }
}

/**
 * Default shared capture-ready manager instance.
 */
export const captureReadyManager = new CaptureReadyManager();
