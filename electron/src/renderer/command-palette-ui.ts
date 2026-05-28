/**
 * Command palette overlay — searchable modal dialog for executing any
 * Myra CLI command from the VoxelForge CommandRegistry catalog.
 *
 * Usage:
 *   const palette = new CommandPalette(myraExecuteCommand);
 *   palette.show();          // generic search mode
 *   palette.show("ao-bake"); // pre-selected command
 *   palette.destroy();       // remove DOM
 */

import { COMMAND_CATALOG, type CommandCatalogEntry, lookupCommand } from "./command-palette";

type MyraExecutor = (label: string, command: string, args: string[]) => Promise<void>;

interface CommandPaletteOptions {
  /** Callback to execute a Myra CLI command. */
  execute: MyraExecutor;
  /** DOM element to attach the overlay to (default: document.body). */
  container?: HTMLElement;
  /** Status display callback. */
  setStatus?: (msg: string) => void;
}

const PALETTE_CSS = `
.vf-command-palette-overlay {
  position: fixed;
  inset: 0;
  z-index: 9999;
  display: flex;
  align-items: flex-start;
  justify-content: center;
  padding-top: 15vh;
  background: rgba(0, 0, 0, 0.55);
  backdrop-filter: blur(2px);
}
.vf-command-palette {
  width: 640px;
  max-height: 70vh;
  display: flex;
  flex-direction: column;
  background: #1b2028;
  border: 1px solid #394150;
  border-radius: 12px;
  box-shadow: 0 16px 48px rgba(0, 0, 0, 0.5);
  overflow: hidden;
}
.vf-command-palette-input {
  width: 100%;
  padding: 14px 16px;
  font-size: 15px;
  background: #11151b;
  border: none;
  border-bottom: 1px solid #303846;
  color: #d9e2ef;
  outline: none;
}
.vf-command-palette-input::placeholder {
  color: #6f7a89;
}
.vf-command-palette-input:focus {
  background: #0f1319;
}
.vf-command-palette-list {
  flex: 1;
  overflow-y: auto;
  padding: 8px;
}
.vf-command-palette-group-label {
  padding: 6px 10px 4px;
  font-size: 11px;
  font-weight: 600;
  color: #6f7a89;
  text-transform: uppercase;
  letter-spacing: 0.08em;
}
.vf-command-palette-item {
  display: grid;
  grid-template-columns: 1fr auto;
  align-items: center;
  width: 100%;
  padding: 9px 12px;
  border-radius: 6px;
  cursor: pointer;
  border: none;
  background: transparent;
  color: #d9e2ef;
  text-align: left;
  font: inherit;
  font-size: 13px;
}
.vf-command-palette-item:hover,
.vf-command-palette-item.selected {
  background: #2a3442;
}
.vf-command-palette-item-name {
  font-weight: 500;
}
.vf-command-palette-item-aliases {
  color: #6f7a89;
  font-size: 11px;
}
.vf-command-palette-item-desc {
  color: #93a4b8;
  font-size: 11px;
  margin-top: 2px;
}
.vf-command-palette-item-badge {
  padding: 2px 7px;
  border-radius: 999px;
  font-size: 10px;
  font-weight: 600;
  color: #11151b;
}
.vf-command-palette-item-badge.backed {
  background: #4ec66e;
}
.vf-command-palette-item-badge.unbacked {
  background: #6f7a89;
}
.vf-command-palette-footer {
  padding: 8px 14px;
  border-top: 1px solid #303846;
  font-size: 11px;
  color: #6f7a89;
}
`;

const ARG_PROMPT_CSS = `
.vf-command-arg-prompt {
  padding: 16px;
}
.vf-command-arg-prompt h3 {
  font-size: 14px;
  margin-bottom: 8px;
  color: #d9e2ef;
}
.vf-command-arg-prompt .help {
  font-size: 12px;
  color: #93a4b8;
  margin-bottom: 12px;
}
.vf-command-arg-prompt input {
  width: 100%;
  padding: 10px;
  font-size: 13px;
  background: #11151b;
  border: 1px solid #394150;
  border-radius: 6px;
  color: #d9e2ef;
  margin-bottom: 10px;
}
.vf-command-arg-prompt input:focus {
  border-color: #5a8fca;
}
.vf-command-arg-actions {
  display: flex;
  gap: 8px;
  justify-content: flex-end;
}
.vf-command-arg-actions button {
  padding: 7px 16px;
  border: 1px solid #394150;
  border-radius: 6px;
  cursor: pointer;
  font: inherit;
  font-size: 12px;
}
.vf-command-arg-btn-execute {
  background: #33557a;
  color: #fff;
  border-color: #5a8fca;
}
.vf-command-arg-btn-cancel {
  background: #242b36;
  color: #d9e2ef;
}
`;

export class CommandPalette {
  private overlay: HTMLDivElement | null = null;
  private input: HTMLInputElement | null = null;
  private listEl: HTMLDivElement | null = null;
  private footer: HTMLDivElement | null = null;
  private selectedIndex = 0;
  private filteredCommands: CommandCatalogEntry[] = [];
  private currentCommand: CommandCatalogEntry | null = null;
  private _visible = false;

  constructor(private readonly options: CommandPaletteOptions) {}

  get visible(): boolean {
    return this._visible;
  }

  /** Show the palette, optionally pre-selecting a command. */
  show(preselectedCommand?: string): void {
    this.destroy();
    this._visible = true;
    this.currentCommand = null;

    this.overlay = document.createElement("div");
    this.overlay.className = "vf-command-palette-overlay";
    this.overlay.addEventListener("click", (e) => {
      if (e.target === this.overlay) this.destroy();
    });

    const palette = document.createElement("div");
    palette.className = "vf-command-palette";

    // Search input
    this.input = document.createElement("input");
    this.input.className = "vf-command-palette-input";
    this.input.placeholder = "Type a command name or alias…";
    this.input.addEventListener("input", () => this.filter());
    this.input.addEventListener("keydown", (e) => this.onKeyDown(e));

    // Command list
    this.listEl = document.createElement("div");
    this.listEl.className = "vf-command-palette-list";

    // Footer
    this.footer = document.createElement("div");
    this.footer.className = "vf-command-palette-footer";
    this.footer.textContent = `${COMMAND_CATALOG.length} commands · ↑↓ navigate · Enter execute · Esc close`;

    palette.appendChild(this.input);
    palette.appendChild(this.listEl);
    palette.appendChild(this.footer);
    this.overlay.appendChild(palette);

    // Inject styles
    this.injectStyle(PALETTE_CSS);
    this.injectStyle(ARG_PROMPT_CSS);

    const container = this.options.container ?? document.body;
    container.appendChild(this.overlay);

    if (preselectedCommand) {
      this.input.value = preselectedCommand;
    }
    this.filter();
    this.input.focus();

    // Handle Escape
    const escHandler = (e: KeyboardEvent) => {
      if (e.key === "Escape") this.destroy();
    };
    this.overlay.addEventListener("keydown", escHandler);
  }

  /** Hide and destroy the palette DOM. */
  destroy(): void {
    this._visible = false;
    if (this.overlay && this.overlay.parentNode) {
      this.overlay.parentNode.removeChild(this.overlay);
    }
    this.overlay = null;
    this.input = null;
    this.listEl = null;
    this.footer = null;
  }

  private filter(): void {
    const q = (this.input?.value ?? "").toLowerCase().trim();
    this.filteredCommands = q
      ? COMMAND_CATALOG.filter(
          (c) =>
            c.name.includes(q) ||
            c.aliases.some((a) => a.includes(q)) ||
            c.helpText.toLowerCase().includes(q),
        )
      : COMMAND_CATALOG;
    this.selectedIndex = 0;
    this.renderList();
  }

  private renderList(): void {
    if (!this.listEl) return;
    this.listEl.innerHTML = "";

    if (this.filteredCommands.length === 0) {
      const msg = document.createElement("div");
      msg.style.cssText = "padding: 24px; text-align: center; color: #6f7a89; font-size: 13px;";
      msg.textContent = "No matching commands.";
      this.listEl.appendChild(msg);
      return;
    }

    // Group by category preserving order
    const groups = new Map<string, CommandCatalogEntry[]>();
    for (const cmd of this.filteredCommands) {
      const group = groups.get(cmd.category) ?? [];
      group.push(cmd);
      groups.set(cmd.category, group);
    }

    let flatIndex = 0;
    for (const [category, cmds] of groups) {
      const label = document.createElement("div");
      label.className = "vf-command-palette-group-label";
      label.textContent = category;
      this.listEl.appendChild(label);

      for (const cmd of cmds) {
        const btn = document.createElement("button");
        btn.className = "vf-command-palette-item";
        if (flatIndex === this.selectedIndex) btn.classList.add("selected");
        btn.dataset.index = String(flatIndex);

        const nameSpan = document.createElement("div");
        nameSpan.className = "vf-command-palette-item-name";
        nameSpan.textContent = cmd.name;

        const aliasesSpan = document.createElement("div");
        aliasesSpan.className = "vf-command-palette-item-aliases";
        aliasesSpan.textContent = cmd.aliases.length > 0 ? cmd.aliases.join(", ") : "";

        const descSpan = document.createElement("div");
        descSpan.className = "vf-command-palette-item-desc";
        descSpan.textContent = cmd.helpText;

        const leftCol = document.createElement("div");
        leftCol.style.cssText = "display: flex; flex-direction: column;";
        leftCol.appendChild(nameSpan);
        leftCol.appendChild(aliasesSpan);
        leftCol.appendChild(descSpan);

        const badge = document.createElement("span");
        badge.className = `vf-command-palette-item-badge ${cmd.backed ? "backed" : "unbacked"}`;
        badge.textContent = cmd.backed ? "ready" : "unavailable";

        btn.appendChild(leftCol);
        btn.appendChild(badge);

        btn.addEventListener("click", () => this.selectCommand(cmd));
        btn.addEventListener("dblclick", () => this.executeSelected(cmd));

        this.listEl.appendChild(btn);
        flatIndex++;
      }
    }
  }

  private onKeyDown(e: KeyboardEvent): void {
    if (e.key === "ArrowDown") {
      e.preventDefault();
      this.selectedIndex = Math.min(this.selectedIndex + 1, this.filteredCommands.length - 1);
      this.scrollToSelected();
      this.renderList();
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      this.selectedIndex = Math.max(this.selectedIndex - 1, 0);
      this.scrollToSelected();
      this.renderList();
    } else if (e.key === "Enter") {
      e.preventDefault();
      const cmd = this.filteredCommands[this.selectedIndex];
      if (cmd) this.selectCommand(cmd);
    }
  }

  private scrollToSelected(): void {
    const selected = this.listEl?.querySelector(".vf-command-palette-item.selected");
    if (selected) {
      selected.scrollIntoView({ block: "nearest" });
    }
  }

  private selectCommand(entry: CommandCatalogEntry): void {
    this.currentCommand = entry;

    if (!entry.backed) {
      this.showMessage(
        `Command "${entry.name}" is not yet backed by the C# sidecar handler.`,
        true,
      );
      return;
    }

    // Show argument prompt
    this.showArgPrompt(entry);
  }

  private showArgPrompt(entry: CommandCatalogEntry): void {
    if (!this.listEl || !this.overlay) return;

    this.listEl.innerHTML = "";
    if (this.footer) this.footer.style.display = "none";

    const container = document.createElement("div");
    container.className = "vf-command-arg-prompt";

    const title = document.createElement("h3");
    title.textContent = entry.name;

    const help = document.createElement("div");
    help.className = "help";
    help.textContent = entry.helpText;

    const argInput = document.createElement("input");
    argInput.placeholder = "Arguments (space-separated, or leave empty)";
    argInput.focus();

    const actions = document.createElement("div");
    actions.className = "vf-command-arg-actions";

    const executeBtn = document.createElement("button");
    executeBtn.className = "vf-command-arg-btn-execute";
    executeBtn.textContent = "Execute";
    executeBtn.addEventListener("click", () => {
      const argStr = argInput.value.trim();
      const args = argStr ? argStr.split(/\s+/).filter(Boolean) : [];
      void this.runCommand(entry, args);
    });

    const cancelBtn = document.createElement("button");
    cancelBtn.className = "vf-command-arg-btn-cancel";
    cancelBtn.textContent = "Back";
    cancelBtn.addEventListener("click", () => {
      this.currentCommand = null;
      if (this.footer) this.footer.style.display = "";
      this.filter();
    });

    // Also execute on Enter in the arg input
    argInput.addEventListener("keydown", (e) => {
      if (e.key === "Enter") {
        const argStr = argInput.value.trim();
        const args = argStr ? argStr.split(/\s+/).filter(Boolean) : [];
        void this.runCommand(entry, args);
      }
      if (e.key === "Escape") {
        this.destroy();
      }
    });

    actions.appendChild(cancelBtn);
    actions.appendChild(executeBtn);
    container.appendChild(title);
    container.appendChild(help);
    container.appendChild(argInput);
    container.appendChild(actions);
    this.listEl.appendChild(container);
  }

  private async runCommand(entry: CommandCatalogEntry, args: string[]): Promise<void> {
    const label = entry.aliases.length > 0
      ? `${entry.name} (${entry.aliases[0]})`
      : entry.name;
    this.options.setStatus?.(`Executing: ${entry.name} ${args.join(" ")}`);
    this.destroy();
    try {
      await this.options.execute(label, entry.name, args);
    } catch (err) {
      this.options.setStatus?.(`Command failed: ${String(err)}`);
    }
  }

  private executeSelected(entry: CommandCatalogEntry): void {
    this.selectCommand(entry);
  }

  private showMessage(msg: string, isWarning = false): void {
    if (!this.listEl) return;
    this.listEl.innerHTML = "";
    if (this.footer) this.footer.style.display = "none";

    const el = document.createElement("div");
    el.style.cssText = "padding: 24px; text-align: center; font-size: 13px;";
    el.style.color = isWarning ? "#ffca6e" : "#93a4b8";
    el.textContent = msg;

    const backBtn = document.createElement("button");
    backBtn.style.cssText = "margin-top: 16px; padding: 7px 16px; border: 1px solid #394150; border-radius: 6px; cursor: pointer; background: #242b36; color: #d9e2ef; font: inherit; font-size: 12px;";
    backBtn.textContent = "Back";
    backBtn.addEventListener("click", () => {
      this.currentCommand = null;
      if (this.footer) this.footer.style.display = "";
      this.filter();
    });

    el.appendChild(document.createElement("br"));
    el.appendChild(backBtn);
    this.listEl.appendChild(el);
  }

  private injectStyle(css: string): void {
    const id = "__vf_palette_style";
    if (document.getElementById(id)) return;
    const style = document.createElement("style");
    style.id = id;
    style.textContent = css;
    document.head.appendChild(style);
  }
}
