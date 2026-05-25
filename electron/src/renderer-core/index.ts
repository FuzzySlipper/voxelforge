/**
 * renderer-core barrel export.
 * Exports all public types, classes, and helpers from the shared TS renderer-core.
 */

// Protocol types and normalizers
export * from "./protocol/types";
export {
  normalizeSnapshot,
  normalizeByteArrayField,
  normalizeBounds,
  computeCombinedBounds,
  snapshotToStateSummary,
  transitionalStateToSummary,
  transitionalMeshToSnapshot,
} from "./protocol/normalizeSnapshot";

// Scene
export {
  VoxelForgeScene,
  shouldFrameCamera,
  normalizeColorsRgba,
  hasVertexAlpha,
  maxVertexAlpha,
  computeViewFromAnglePosition,
} from "./scene/VoxelForgeScene";
export type { RendererMetrics, VoxelForgeSceneOptions } from "./scene/VoxelForgeScene";

// Reference models, materials, capture readiness
export * from "./scene/referenceModels";
export * from "./scene/materials";
export { CaptureReadyManager, captureReadyManager } from "./scene/captureReady";

// Transport
export type {
  RenderProtocolClient,
  ConnectionState,
  BridgeRequestResponse,
} from "./transport/RenderProtocolClient";
export { HttpSseRenderClient } from "./transport/HttpSseRenderClient";
export type { HttpSseRenderClientOptions } from "./transport/HttpSseRenderClient";
export { DenBridgeRenderClient } from "./transport/DenBridgeRenderClient";
export type { DenBridgeRenderClientOptions, VoxelForgeBridge } from "./transport/DenBridgeRenderClient";
