import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    // Run tests in a Node-like environment (no DOM/WebGL required).
    // Tests against pure helpers extracted from renderer code.
    include: ["tests/**/*.test.ts"],
    // Show full diff in assertion failures.
    bail: 0,
  },
});
