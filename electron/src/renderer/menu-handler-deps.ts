/**
 * Menu handler dependency interface.
 *
 * Factored menu handlers receive a `MenuHandlerDeps` object so the same code
 * path runs in the real renderer (real DOM prompts + bridge requests) and in
 * tests (mocked/stub deps).
 */

export interface MenuHandlerDeps {
  /** Prompt the user for a string value. Returns null on cancel. */
  promptUser(message: string, defaultValue?: string): string | null;

  /** Prompt the user asynchronously with a renderer-owned dialog. Returns null on cancel. */
  promptUserAsync?(message: string, defaultValue?: string): Promise<string | null>;

  /** Confirm with the user. Returns true if accepted. */
  confirmUser(message: string): boolean;

  /** Prompt for an integer. Returns null on cancel or invalid input. */
  promptInt(label: string, defaultValue?: string): number | null;

  /** Execute a Myra CLI command through the bridge:myra-command-execute channel. */
  myraExecuteCommand(label: string, command: string, args: string[]): Promise<void>;

  /** Execute a bridge action request (runAction equivalent). */
  runAction(label: string, channel: string, payload: unknown): Promise<void>;

  /** Set the status display text. */
  setStatus(message: string): void;

  /** Log a message. */
  log(...args: unknown[]): void;

  /** Optional: get the default path from the UI (used by handleFileOpen). */
  getDefaultPath?(): string;
}

/**
 * Create a MenuHandlerDeps implementation backed by the real DOM/bridge globals
 * available in the renderer process.
 */
export function createRendererDeps(
  myraExecuteCommand: MenuHandlerDeps["myraExecuteCommand"],
  runAction: MenuHandlerDeps["runAction"],
  setStatus: MenuHandlerDeps["setStatus"],
  getDefaultPath?: () => string,
): MenuHandlerDeps {
  return {
    promptUser: (message: string, defaultValue?: string) =>
      window.prompt(message, defaultValue ?? ""),
    promptUserAsync: showRendererPrompt,
    confirmUser: (message: string) => window.confirm(message),
    promptInt: (label: string, defaultValue = "0") => {
      const s = window.prompt(label, defaultValue);
      if (s === null) return null;
      const n = parseInt(s, 10);
      return isNaN(n) ? null : n;
    },
    myraExecuteCommand,
    runAction,
    setStatus,
    log: (...args: unknown[]) => console.log(...args),
    getDefaultPath,
  };
}

function showRendererPrompt(message: string, defaultValue = ""): Promise<string | null> {
  return new Promise((resolve) => {
    const overlay = document.createElement("div");
    overlay.setAttribute("role", "dialog");
    overlay.setAttribute("aria-modal", "true");
    overlay.style.cssText = [
      "position: fixed",
      "inset: 0",
      "z-index: 2147483647",
      "display: grid",
      "place-items: center",
      "background: rgba(0, 0, 0, 0.55)",
      "font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace",
    ].join(";");

    const panel = document.createElement("form");
    panel.style.cssText = [
      "width: min(680px, calc(100vw - 48px))",
      "border: 1px solid #5d789d",
      "border-radius: 10px",
      "background: #151b24",
      "box-shadow: 0 18px 50px rgba(0, 0, 0, 0.55)",
      "color: #dbe9fa",
      "padding: 18px",
    ].join(";");

    const label = document.createElement("label");
    label.textContent = message;
    label.style.cssText = "display: block; white-space: pre-wrap; line-height: 1.4; margin-bottom: 12px;";

    const input = document.createElement("input");
    input.value = defaultValue;
    input.style.cssText = [
      "width: 100%",
      "box-sizing: border-box",
      "border: 1px solid #5d789d",
      "border-radius: 6px",
      "background: #0d1117",
      "color: #f2f7ff",
      "padding: 9px 10px",
      "font: inherit",
    ].join(";");

    const buttons = document.createElement("div");
    buttons.style.cssText = "display: flex; justify-content: flex-end; gap: 8px; margin-top: 14px;";

    const cancel = document.createElement("button");
    cancel.type = "button";
    cancel.textContent = "Cancel";

    const ok = document.createElement("button");
    ok.type = "submit";
    ok.textContent = "OK";

    for (const button of [cancel, ok]) {
      button.style.cssText = [
        "border: 1px solid #48617f",
        "border-radius: 6px",
        "background: #243044",
        "color: #dbe9fa",
        "padding: 7px 12px",
        "font: inherit",
        "cursor: pointer",
      ].join(";");
    }

    const cleanup = (value: string | null) => {
      document.removeEventListener("keydown", onKeyDown, true);
      overlay.remove();
      resolve(value);
    };

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        event.preventDefault();
        cleanup(null);
      }
    };

    cancel.addEventListener("click", () => cleanup(null));
    panel.addEventListener("submit", (event) => {
      event.preventDefault();
      cleanup(input.value);
    });
    document.addEventListener("keydown", onKeyDown, true);

    buttons.append(cancel, ok);
    panel.append(label, input, buttons);
    overlay.append(panel);
    document.body.append(overlay);
    input.focus();
    input.select();
  });
}
