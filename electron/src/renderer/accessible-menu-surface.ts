/**
 * accessible-menu-surface.ts — Renderer-owned accessible menu/command surface
 *
 * Creates a semantic <nav role="menubar"> with ARIA roles and keyboard navigation
 * that replaces the native Electron menu for primary workflows.
 *
 * Keyboard navigation:
 *   Tab / Shift+Tab — move focus into/out of the menubar
 *   Left/Right arrows — navigate between top-level menu items
 *   Enter / Space / Down arrow — open the focused menu's submenu
 *   Up/Down arrows — navigate items inside an open submenu
 *   Enter / Space — activate the focused menu item
 *   Escape — close the open submenu
 *   Home / End — jump to first/last item
 *
 * The model (APP_MENU_MODEL) is shared with native menu setup to prevent
 * divergent menu definitions.
 */
import {
  APP_MENU_MODEL,
  type MenuCommandModel,
  type MenuCommandGroup,
  type MenuCommandItem,
  findMenuItemById,
} from "../shared/menu-command-model";

// ── Types ──

export interface AccessibleMenuSurfaceOptions {
  /** DOM element to attach the menubar into. */
  container: HTMLElement;
  /** Callback when a menu command is activated. Receives the IPC channel string. */
  onCommand: (channel: string, itemId: string) => void;
  /** Optional: override the menu model (default: APP_MENU_MODEL). */
  model?: MenuCommandModel;
  /** Optional: CSS class prefix (default: "vf-am"). */
  cssPrefix?: string;
}

// ── Focus management ──

const MENUBAR_SELECTOR = '[role="menubar"]';
const MENU_ITEM_SELECTOR = '[role="menuitem"]';
const MENU_SELECTOR = '[role="menu"]';

// ── Class ──

export class AccessibleMenuSurface {
  private readonly container: HTMLElement;
  private readonly model: MenuCommandModel;
  private readonly onCommand: (channel: string, itemId: string) => void;
  private readonly cssPrefix: string;

  private menubarEl: HTMLElement | null = null;
  private topLevelButtons: HTMLElement[] = [];
  private submenus: Map<string, HTMLElement> = new Map();
  private openMenuId: string | null = null;
  private injectedStyleId: string | null = null;
  private _destroyed = false;

  constructor(options: AccessibleMenuSurfaceOptions) {
    this.container = options.container;
    this.model = options.model ?? APP_MENU_MODEL;
    this.onCommand = options.onCommand;
    this.cssPrefix = options.cssPrefix ?? "vf-am";

    this.build();
  }

  get destroyed(): boolean {
    return this._destroyed;
  }

  /** Full teardown: remove DOM elements and event listeners. */
  destroy(): void {
    if (this._destroyed) return;
    this._destroyed = true;
    this.closeAllSubmenus();
    if (this.menubarEl?.parentNode) {
      this.menubarEl.parentNode.removeChild(this.menubarEl);
    }
    this.menubarEl = null;
    this.topLevelButtons = [];
    this.submenus.clear();
    this.openMenuId = null;
    if (this.injectedStyleId) {
      const styleEl = document.getElementById(this.injectedStyleId);
      styleEl?.remove();
    }
  }

  // ── Public API ──

  /** Get the menubar DOM element. */
  getElement(): HTMLElement | null {
    return this.menubarEl;
  }

  /** Focus the menubar. */
  focus(): void {
    if (this.topLevelButtons.length > 0) {
      this.topLevelButtons[0].focus();
    }
  }

  // ── Build ──

  private build(): void {
    this.injectStyles();

    // Create the menubar
    this.menubarEl = document.createElement("nav");
    this.menubarEl.setAttribute("role", "menubar");
    this.menubarEl.setAttribute("aria-label", "Application menu");
    this.menubarEl.className = `${this.cssPrefix}-menubar`;

    // Trap keyboard events at menubar level
    this.menubarEl.addEventListener("keydown", (e) => this.onMenubarKeyDown(e));
    this.menubarEl.addEventListener("focusout", (e) => this.onFocusOut(e));

    for (const group of this.model) {
      const btn = this.createTopLevelButton(group);
      this.menubarEl.appendChild(btn);
      this.topLevelButtons.push(btn);
    }

    this.container.appendChild(this.menubarEl);
  }

  private createTopLevelButton(group: MenuCommandGroup): HTMLElement {
    const btn = document.createElement("button");
    btn.setAttribute("role", "menuitem");
    btn.setAttribute("aria-haspopup", "true");
    btn.setAttribute("aria-expanded", "false");
    btn.setAttribute("aria-label", group.label);
    btn.tabIndex = -1; // managed by roving tabindex
    btn.className = `${this.cssPrefix}-top-btn`;
    btn.textContent = group.label;
    btn.dataset.menuId = group.id;

    // Click to toggle submenu
    btn.addEventListener("click", () => this.toggleSubmenu(group.id, btn));
    // Hover to open when another menu is already open
    btn.addEventListener("mouseenter", () => {
      if (this.openMenuId !== null && this.openMenuId !== group.id) {
        this.closeAllSubmenus();
        this.openSubmenu(group.id, btn);
      }
    });

    return btn;
  }

  private createSubmenu(group: MenuCommandGroup): HTMLElement {
    const menuEl = document.createElement("div");
    menuEl.setAttribute("role", "menu");
    menuEl.setAttribute("aria-label", group.label);
    menuEl.className = `${this.cssPrefix}-submenu`;
    menuEl.dataset.menuId = group.id;
    menuEl.style.display = "none";

    // Keyboard navigation within submenu
    menuEl.addEventListener("keydown", (e) => this.onSubmenuKeyDown(e, group.id));

    for (let i = 0; i < group.items.length; i++) {
      const item = group.items[i];
      if (item.type === "separator") {
        const sep = document.createElement("div");
        sep.setAttribute("role", "separator");
        sep.className = `${this.cssPrefix}-separator`;
        menuEl.appendChild(sep);
      } else {
        const mi = document.createElement("button");
        mi.setAttribute("role", "menuitem");
        mi.tabIndex = -1;
        mi.className = `${this.cssPrefix}-item`;
        mi.dataset.itemId = item.id;

        // Label with accelerator hint
        const labelSpan = document.createElement("span");
        labelSpan.className = `${this.cssPrefix}-item-label`;
        labelSpan.textContent = item.label;
        mi.appendChild(labelSpan);

        if (item.accelerator) {
          const accelSpan = document.createElement("kbd");
          accelSpan.className = `${this.cssPrefix}-item-accel`;
          accelSpan.textContent = item.accelerator;
          mi.appendChild(accelSpan);
        }

        const enabled = item.enabled !== false;
        mi.disabled = !enabled;

        mi.addEventListener("click", () => {
          if (item.channel) {
            this.onCommand(item.channel, item.id);
          }
          this.closeAllSubmenus();
        });

        // Hover sets active descendant
        mi.addEventListener("mouseenter", () => {
          this.setActiveDescendant(group.id, mi);
        });

        menuEl.appendChild(mi);
      }
    }

    // Close on click outside
    menuEl.addEventListener("click", (e) => {
      // Only close if the click is on the background of the submenu, not an item
      if (e.target === menuEl) {
        this.closeAllSubmenus();
      }
    });

    return menuEl;
  }

  // ── Submenu management ──

  private openSubmenu(groupId: string, btn: HTMLElement): void {
    // Create submenu DOM on demand
    let menuEl = this.submenus.get(groupId);
    if (!menuEl) {
      const group = this.model.find((g) => g.id === groupId);
      if (!group) return;
      menuEl = this.createSubmenu(group);
      this.submenus.set(groupId, menuEl);
      // Append to menubar container for proper positioning
      this.menubarEl?.parentNode?.appendChild(menuEl);
    }

    // Position below the button
    const btnRect = btn.getBoundingClientRect();
    menuEl.style.display = "block";
    menuEl.style.position = "fixed";
    menuEl.style.top = `${btnRect.bottom}px`;
    menuEl.style.left = `${btnRect.left}px`;
    menuEl.style.minWidth = `${Math.max(btnRect.width, 220)}px`;

    // Update ARIA states
    btn.setAttribute("aria-expanded", "true");
    this.openMenuId = groupId;

    // Focus first item
    const firstItem = menuEl.querySelector<HTMLElement>(MENU_ITEM_SELECTOR);
    if (firstItem) {
      firstItem.focus();
    }
  }

  private closeSubmenu(groupId: string): void {
    const menuEl = this.submenus.get(groupId);
    if (menuEl) {
      menuEl.style.display = "none";
    }
    const btn = this.topLevelButtons.find((b) => b.dataset.menuId === groupId);
    if (btn) {
      btn.setAttribute("aria-expanded", "false");
    }
    if (this.openMenuId === groupId) {
      this.openMenuId = null;
    }
  }

  private closeAllSubmenus(): void {
    for (const [id] of this.submenus) {
      this.closeSubmenu(id);
    }
  }

  private toggleSubmenu(groupId: string, btn: HTMLElement): void {
    if (this.openMenuId === groupId) {
      this.closeSubmenu(groupId);
      btn.focus();
    } else {
      this.closeAllSubmenus();
      this.openSubmenu(groupId, btn);
    }
  }

  private setActiveDescendant(groupId: string, itemEl: HTMLElement): void {
    const menuEl = this.submenus.get(groupId);
    if (!menuEl) return;
    // Remove active class from all items
    menuEl.querySelectorAll(MENU_ITEM_SELECTOR).forEach((el) => {
      el.classList.remove(`${this.cssPrefix}-item-active`);
    });
    itemEl.classList.add(`${this.cssPrefix}-item-active`);
  }

  // ── Keyboard handling ──

  private onMenubarKeyDown(e: KeyboardEvent): void {
    if (this._destroyed) return;
    const currentItem = e.target as HTMLElement;
    const currentIndex = this.topLevelButtons.indexOf(currentItem);
    if (currentIndex === -1) return;

    switch (e.key) {
      case "ArrowRight":
        e.preventDefault();
        this.closeAllSubmenus();
        this.focusTopLevel((currentIndex + 1) % this.topLevelButtons.length);
        break;
      case "ArrowLeft":
        e.preventDefault();
        this.closeAllSubmenus();
        this.focusTopLevel(
          (currentIndex - 1 + this.topLevelButtons.length) % this.topLevelButtons.length,
        );
        break;
      case "ArrowDown":
      case "Enter":
      case " ":
        e.preventDefault();
        {
          const btn = this.topLevelButtons[currentIndex];
          const groupId = btn.dataset.menuId;
          if (groupId) this.openSubmenu(groupId, btn);
        }
        break;
      case "Escape":
        e.preventDefault();
        this.closeAllSubmenus();
        break;
      case "Home":
        e.preventDefault();
        this.closeAllSubmenus();
        this.focusTopLevel(0);
        break;
      case "End":
        e.preventDefault();
        this.closeAllSubmenus();
        this.focusTopLevel(this.topLevelButtons.length - 1);
        break;
    }
  }

  private onSubmenuKeyDown(e: KeyboardEvent, groupId: string): void {
    if (this._destroyed) return;
    const menuEl = this.submenus.get(groupId);
    if (!menuEl) return;

    const items = Array.from(menuEl.querySelectorAll<HTMLElement>(MENU_ITEM_SELECTOR));
    const currentItem = e.target as HTMLElement;
    const currentIndex = items.indexOf(currentItem);

    switch (e.key) {
      case "ArrowDown":
        e.preventDefault();
        if (currentIndex < items.length - 1) {
          items[currentIndex + 1].focus();
        } else {
          items[0].focus();
        }
        break;
      case "ArrowUp":
        e.preventDefault();
        if (currentIndex > 0) {
          items[currentIndex - 1].focus();
        } else {
          items[items.length - 1].focus();
        }
        break;
      case "Enter":
      case " ":
        e.preventDefault();
        currentItem.click();
        break;
      case "Escape":
        e.preventDefault();
        this.closeSubmenu(groupId);
        // Return focus to the top-level button
        const btn = this.topLevelButtons.find((b) => b.dataset.menuId === groupId);
        btn?.focus();
        break;
      case "Tab":
        e.preventDefault();
        this.closeAllSubmenus();
        break;
      case "ArrowLeft":
        e.preventDefault();
        this.closeSubmenu(groupId);
        this.focusAdjacentTopLevel(this.topLevelButtons.findIndex((b) => b.dataset.menuId === groupId), -1);
        break;
      case "ArrowRight":
        e.preventDefault();
        this.closeSubmenu(groupId);
        this.focusAdjacentTopLevel(this.topLevelButtons.findIndex((b) => b.dataset.menuId === groupId), 1);
        break;
      case "Home":
        e.preventDefault();
        items[0]?.focus();
        break;
      case "End":
        e.preventDefault();
        items[items.length - 1]?.focus();
        break;
    }
  }

  private onFocusOut(e: FocusEvent): void {
    // Schedule close if focus moves outside the entire menu surface
    if (this._destroyed) return;
    const relatedTarget = e.relatedTarget as HTMLElement | null;
    const menubarEl = this.menubarEl;
    if (!menubarEl) return;

    // Check if new focus is anywhere in our menu surface tree
    let isInside = false;
    if (relatedTarget) {
      isInside = menubarEl.contains(relatedTarget);
      if (!isInside) {
        // Also check submenus
        for (const menuEl of this.submenus.values()) {
          if (menuEl.contains(relatedTarget)) {
            isInside = true;
            break;
          }
        }
      }
    }

    if (!isInside) {
      // Defer to let click handlers fire first
      setTimeout(() => this.closeAllSubmenus(), 100);
    }
  }

  // ── Focus helpers ──

  private focusTopLevel(index: number): void {
    const btn = this.topLevelButtons[index];
    if (btn) {
      btn.focus();
    }
  }

  private focusAdjacentTopLevel(currentIndex: number, direction: -1 | 1): void {
    const next =
      (currentIndex + direction + this.topLevelButtons.length) % this.topLevelButtons.length;
    const btn = this.topLevelButtons[next];
    if (btn) {
      const groupId = btn.dataset.menuId;
      if (groupId) this.openSubmenu(groupId, btn);
    }
  }

  // ── Styles ──

  private injectStyles(): void {
    const id = `__${this.cssPrefix}_style`;
    this.injectedStyleId = id;
    if (document.getElementById(id)) return;

    const style = document.createElement("style");
    style.id = id;
    style.textContent = this.getCSS();
    document.head.appendChild(style);
  }

  private getCSS(): string {
    const p = this.cssPrefix;
    return `
/* ── Accessible menu surface ── */
.${p}-menubar {
  display: flex;
  align-items: center;
  gap: 0;
  background: transparent;
  min-height: 28px;
}

.${p}-top-btn {
  display: inline-flex;
  align-items: center;
  padding: 4px 10px;
  border: 1px solid transparent;
  border-radius: 4px;
  background: transparent;
  color: #d9e2ef;
  font: inherit;
  font-size: 12px;
  cursor: pointer;
  white-space: nowrap;
  outline: none;
  transition: background 0.1s, border-color 0.1s;
}

.${p}-top-btn:hover,
.${p}-top-btn:focus-visible {
  background: #2a3442;
  border-color: #48617f;
}

.${p}-top-btn[aria-expanded="true"] {
  background: #243044;
  border-color: #5a8fca;
  color: #fff;
}

.${p}-submenu {
  z-index: 10000;
  background: #1b2028;
  border: 1px solid #394150;
  border-radius: 8px;
  padding: 6px;
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.5);
  max-height: 75vh;
  overflow-y: auto;
}

.${p}-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  padding: 7px 12px;
  border: 1px solid transparent;
  border-radius: 5px;
  background: transparent;
  color: #d9e2ef;
  font: inherit;
  font-size: 12px;
  cursor: pointer;
  text-align: left;
  outline: none;
  transition: background 0.1s;
  gap: 24px;
}

.${p}-item:hover,
.${p}-item:focus,
.${p}-item-active {
  background: #2a3442;
  border-color: #48617f;
}

.${p}-item:focus-visible {
  outline: 2px solid #5a8fca;
  outline-offset: -1px;
}

.${p}-item:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

.${p}-item-label {
  flex: 1;
}

.${p}-item-accel {
  color: #6f7a89;
  font-size: 11px;
  font-family: inherit;
  margin-left: 16px;
}

.${p}-separator {
  height: 1px;
  background: #303846;
  margin: 4px 8px;
}

/* Focus-within highlighting for the menubar */
.${p}-menubar:focus-within {
  outline: none;
}
`;
  }
}
