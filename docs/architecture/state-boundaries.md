# State Boundaries

Task 680 establishes the first Events-States-Services boundary: durable mutable application data lives in explicit state objects. Later ESS tasks can replace callback notifications, extract services, and thin adapters, but those tasks should build on the ownership map here.

## State Ownership

| State type | Owns | Persistence boundary |
| --- | --- | --- |
| `EditorDocumentState` | Current `VoxelModel`, `LabelIndex`, and animation clips | Project save/load serializes the model, palette, labels, and clips through `ProjectSerializer` |
| `EditorSessionState` | Active tool, active palette index, selected region, active frame, and selected voxels | Runtime editor session only |
| `EditorConfigState` | User-editable editor configuration from `config.json` | Serialized directly to `config.json` |
| `UndoHistoryState` | Undo and redo command history plus maximum history depth | Runtime editor session only |
| `ReferenceModelState` | Loaded reference models and their runtime metadata | Runtime editor session; individual metadata can still be saved by reference commands |
| `ReferenceImageState` | Loaded reference image entries and raw image bytes | Runtime editor session only |

## Transitional Facades

`EditorState` remains as a compatibility aggregate for existing UI, console, and tool code. It delegates document data to `EditorDocumentState` and editor choices to `EditorSessionState`; new durable fields should be added to an explicit state type instead.

`UndoStack` remains as the command application facade for current undo/redo behavior. Its mutable history now lives in `UndoHistoryState`; task 683 should move orchestration from this imperative facade into service operations.

`CommandContext` remains a temporary invocation envelope for console and stdio execution. It points at the document state objects created by the composition root rather than owning separate durable data.

## Naming Boundaries

- `Registry` and `Store` names are reserved for stateless lookup helpers or external storage adapters, not in-memory state owners.
- Serializable metadata DTOs should not use `*State` names unless they own durable mutable runtime state; reference model animation metadata uses `AnimationSnapshot` for that reason.
- Typed application events are intentionally left for task 681 and should report changes to these state objects rather than redefining ownership.
