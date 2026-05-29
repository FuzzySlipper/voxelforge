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
