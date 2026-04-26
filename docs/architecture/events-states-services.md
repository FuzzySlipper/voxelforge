# Events, States, Services, and Adapters

This document records the VoxelForge Events-States-Services (ESS) rule set after tasks 680-684. It is the architecture seam that future GUI, console, LLM, and MCP work must preserve.

## Roles

### Events

Application events are typed facts that something happened. They implement `IApplicationEvent` and use `*Event` names such as `VoxelModelChangedEvent`, `ProjectLoadedEvent`, or `UndoHistoryChangedEvent`.

Rules:

- Events report facts after a state transition; they are not requests.
- Cross-component notification goes through `IEventPublisher` / `IEventDispatcher`.
- Service results include the events they emitted so adapters and tests can observe behavior without subscribing to UI callbacks.
- Free-form strings are allowed as user-facing messages, but they are not internal event contracts.

### States

`*State` types own mutable runtime truth. The state ownership map is in [`state-boundaries.md`](state-boundaries.md).

Rules:

- Durable editor data lives in explicit state objects such as `EditorDocumentState`, `EditorSessionState`, `EditorConfigState`, `UndoHistoryState`, `ReferenceModelState`, and `ReferenceImageState`.
- A service must not hide durable mutable editor state in fields. State is passed into service operations explicitly.
- Derived structures keep their lifecycle explicit. For example, `LabelIndex` is rebuilt from serialized region data rather than treated as independent saved truth.
- Compatibility facades such as `EditorState` and `UndoStack` may remain only when they delegate to explicit state/service operations.

### Services

Services own application behavior: validation, sequencing, undo orchestration, persistence orchestration, and event creation.

Rules:

- Services expose typed methods over explicit state plus typed request/result objects.
- Mutations that affect the model, labels, palette, document, or editor configuration go through a service and, where user-visible, through undoable commands.
- Services may depend on other services or stateless infrastructure such as loaders and serializers, but any instance fields must be readonly dependencies, not hidden state.
- Services do not depend on adapters, UI frameworks, FNA/Myra types, or string command routing.
- Results use `ApplicationServiceResult` / `ApplicationServiceResult<T>` with a success flag, user message, optional data, and emitted events.

### Adapters

Adapters translate a transport or UI surface into typed service calls. Console commands, stdio JSON-line handling, GUI panels/tools, LLM tool application, and future MCP tools are all adapters.

Rules:

- Adapters parse, validate transport-specific input, call typed services, publish or forward typed events, and format responses.
- Adapters do not own durable business state and do not duplicate service behavior.
- Internal app behavior must not be driven by concatenated command strings or `Action` mutation callbacks. The console may still accept user strings at its front door, but other adapters should use typed arguments or services directly.
- LLM tool handlers return typed mutation intents (`VoxelMutationIntent`) for application services to apply. They must not return deferred mutation delegates.
- GUI callbacks are allowed for local widget mechanics, but model/document changes flow through typed services and events.

## Local event conventions

- Renderer dirtying listens to model, palette, project-load, and undo-history events. `UndoHistoryChangedEvent` is intentional because undo/redo replay undoable commands without replaying every domain-specific event. Duplicate dirty marks during ordinary command execution are acceptable because renderer dirtying is idempotent.
- `ApplicationEventDispatcher` is an in-process synchronous dispatcher. Register handlers during application composition before background publishers start; `Publish` runs handlers on the caller's thread and does not marshal to the UI thread. Add an explicit synchronization strategy before introducing late/dynamic registration.
- App-layer tests currently live in `tests/VoxelForge.Core.Tests` because that is the existing test project with explicit App references. Move `EventDispatcherTests`, `UndoStackTests`, and related App-service tests together when a dedicated `VoxelForge.App.Tests` project is introduced.

## MCP integration seam

MCP should be implemented as another thin adapter over the same application core:

```text
MCP tool request -> typed adapter DTO/request -> application service -> explicit state + undo/history -> typed events/result -> MCP response
```

MCP tools should not call GUI code, depend on FNA/Myra types, mutate `VoxelModel` or `LabelIndex` directly, or rebuild console command strings to trigger behavior. If a needed operation only exists as a console command or UI callback, first extract or extend a typed service operation and then call that operation from MCP.

## Test coverage expectations

Architecture tests should keep the most important boundaries from drifting:

- Core remains independent from App, Engine, Myra/FNA, and provider SDKs.
- App remains independent from Engine and UI frameworks.
- Services do not capture mutable state or adapter dependencies.
- Application events are typed `*Event` facts.
- LLM mutation results stay typed intents rather than deferred delegates.
- The MCP adapter seam remains documented in this file.
