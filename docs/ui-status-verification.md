# UI Status Event Manual Verification

Task 866 adds a Myra status bar that consumes App-layer `EditorStatusEvent` messages through an explicit Engine UI handler.

Manual verification steps:

1. Start the editor:

   ```bash
   dotnet run --project src/VoxelForge.Engine.MonoGame
   ```

2. Select the fill tool in the left **Tools** panel.
3. Click an empty/air voxel location in the viewport so the fill operation is rejected.
4. Verify the bottom status bar changes from `Ready` to a warning message similar to:

   ```text
   Warning: fill — Cannot flood fill air...
   ```

5. Wait approximately six seconds.
6. Verify the status bar returns to `Ready`.

The status bar is an Engine/Myra adapter only. `EditorStatusEvent` remains defined in `VoxelForge.App`, and Core/App do not reference Engine/Myra UI types.
