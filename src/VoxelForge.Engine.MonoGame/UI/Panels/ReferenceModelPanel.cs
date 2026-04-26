using Microsoft.Xna.Framework;
using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.UI;
using Myra.Graphics2D.UI.File;
using VoxelForge.App.Reference;
using VoxelForge.Core.Reference;
using VoxelForge.Engine.MonoGame.UI;

namespace VoxelForge.Engine.MonoGame.UI.Panels;

/// <summary>
/// Sidebar panel for managing reference models: load, select, transform, render mode, animation.
/// All mutations dispatch through the command system for GUI/console/LLM parity.
/// </summary>
public sealed class ReferenceModelPanel
{
    private readonly ReferenceModelState _referenceModelState;
    private readonly MenuCommandDispatcher _dispatcher;

    private readonly VerticalStackPanel _modelList;
    private readonly VerticalStackPanel _propertiesSection;

    // Transform fields
    private readonly TextBox _posX, _posY, _posZ;
    private readonly TextBox _rotX, _rotY, _rotZ;
    private readonly TextBox _scaleField;

    // Display controls
    private readonly ComboView _renderModeCombo;
    private readonly CheckButton _visibilityCheck;

    // Animation controls
    private readonly VerticalStackPanel _animSection;
    private readonly ComboView _clipCombo;
    private readonly HorizontalSlider _timelineSlider;
    private readonly Label _timeLabel;
    private readonly TextBox _speedField;

    private Desktop? _desktop;
    private int _selectedIndex = -1;
    private bool _updatingFields;

    public Widget Root { get; }

    public ReferenceModelPanel(ReferenceModelState referenceModelState, MenuCommandDispatcher dispatcher)
    {
        _referenceModelState = referenceModelState;
        _dispatcher = dispatcher;

        var root = new VerticalStackPanel { Spacing = 4 };

        // Header
        root.Widgets.Add(new Label { Text = "Reference Models" });

        // Load button
        var loadBtn = new Button { Content = new Label { Text = "Load Model..." }, Width = 160 };
        loadBtn.Click += (_, _) => ShowLoadModelDialog();
        root.Widgets.Add(loadBtn);

        // Model list
        _modelList = new VerticalStackPanel { Spacing = 2 };
        var listScroll = new ScrollViewer
        {
            Content = _modelList,
            ShowHorizontalScrollBar = false,
            MaxHeight = 120,
        };
        root.Widgets.Add(listScroll);

        // Properties section (hidden when no selection)
        _propertiesSection = new VerticalStackPanel { Spacing = 4, Visible = false };

        // -- Position
        _propertiesSection.Widgets.Add(new Label { Text = "Position" });
        var posRow = new HorizontalStackPanel { Spacing = 2 };
        _posX = SmallField("X"); _posY = SmallField("Y"); _posZ = SmallField("Z");
        posRow.Widgets.Add(FieldWithLabel("X", _posX));
        posRow.Widgets.Add(FieldWithLabel("Y", _posY));
        posRow.Widgets.Add(FieldWithLabel("Z", _posZ));
        _propertiesSection.Widgets.Add(posRow);

        // -- Rotation
        _propertiesSection.Widgets.Add(new Label { Text = "Rotation" });
        var rotRow = new HorizontalStackPanel { Spacing = 2 };
        _rotX = SmallField("0"); _rotY = SmallField("0"); _rotZ = SmallField("0");
        rotRow.Widgets.Add(FieldWithLabel("X", _rotX));
        rotRow.Widgets.Add(FieldWithLabel("Y", _rotY));
        rotRow.Widgets.Add(FieldWithLabel("Z", _rotZ));
        _propertiesSection.Widgets.Add(rotRow);

        // Quick rotate buttons
        var rotBtnRow = new HorizontalStackPanel { Spacing = 2 };
        rotBtnRow.Widgets.Add(MakeButton("X+90", () => DispatchAndRefresh($"refrotate {_selectedIndex} x")));
        rotBtnRow.Widgets.Add(MakeButton("Y+90", () => DispatchAndRefresh($"refrotate {_selectedIndex} y")));
        rotBtnRow.Widgets.Add(MakeButton("Z+90", () => DispatchAndRefresh($"refrotate {_selectedIndex} z")));
        _propertiesSection.Widgets.Add(rotBtnRow);

        // -- Scale
        var scaleRow = new HorizontalStackPanel { Spacing = 4 };
        scaleRow.Widgets.Add(new Label { Text = "Scale" });
        _scaleField = new TextBox { Text = "1", Width = 50 };
        _scaleField.TextChanged += (_, _) => OnScaleChanged();
        scaleRow.Widgets.Add(_scaleField);
        scaleRow.Widgets.Add(MakeButton("0.5x", () => SetScale(0.5f)));
        scaleRow.Widgets.Add(MakeButton("1x", () => SetScale(1f)));
        scaleRow.Widgets.Add(MakeButton("2x", () => SetScale(2f)));
        _propertiesSection.Widgets.Add(scaleRow);

        // -- Auto-Orient
        var orientBtn = new Button { Content = new Label { Text = "Auto-Orient" }, Width = 160 };
        orientBtn.Click += (_, _) =>
        {
            if (_selectedIndex >= 0)
                DispatchAndRefresh($"reforient {_selectedIndex}");
        };
        _propertiesSection.Widgets.Add(orientBtn);

        // -- Render mode
        var modeRow = new HorizontalStackPanel { Spacing = 4 };
        modeRow.Widgets.Add(new Label { Text = "Mode" });
        _renderModeCombo = new ComboView { Width = 110 };
        _renderModeCombo.Widgets.Add(new Label { Text = "Solid", Tag = "solid" });
        _renderModeCombo.Widgets.Add(new Label { Text = "Wireframe", Tag = "wireframe" });
        _renderModeCombo.Widgets.Add(new Label { Text = "Transparent", Tag = "transparent" });
        _renderModeCombo.SelectedIndex = 0;
        _renderModeCombo.SelectedIndexChanged += (_, _) => OnRenderModeChanged();
        modeRow.Widgets.Add(_renderModeCombo);
        _propertiesSection.Widgets.Add(modeRow);

        // -- Visibility
        _visibilityCheck = new CheckButton { IsChecked = true };
        var visRow = new HorizontalStackPanel { Spacing = 4 };
        visRow.Widgets.Add(_visibilityCheck);
        visRow.Widgets.Add(new Label { Text = "Visible" });
        _visibilityCheck.Click += (_, _) => OnVisibilityChanged();
        _propertiesSection.Widgets.Add(visRow);

        // -- Remove / Clear buttons
        var removeBtnRow = new HorizontalStackPanel { Spacing = 4 };
        var removeBtn = new Button { Content = new Label { Text = "Remove" } };
        removeBtn.Click += (_, _) =>
        {
            if (_selectedIndex >= 0)
            {
                _dispatcher.Dispatch($"refremove {_selectedIndex}");
                _selectedIndex = -1;
                Refresh();
            }
        };
        removeBtnRow.Widgets.Add(removeBtn);

        var clearAllBtn = new Button { Content = new Label { Text = "Clear All" } };
        clearAllBtn.Click += (_, _) =>
        {
            _dispatcher.Dispatch("refclear");
            _selectedIndex = -1;
            Refresh();
        };
        removeBtnRow.Widgets.Add(clearAllBtn);
        _propertiesSection.Widgets.Add(removeBtnRow);

        // -- Animation section (only visible when model has animations)
        _animSection = new VerticalStackPanel { Spacing = 4, Visible = false };
        _animSection.Widgets.Add(new Label { Text = "Animation" });

        // Clip selector
        var clipRow = new HorizontalStackPanel { Spacing = 4 };
        clipRow.Widgets.Add(new Label { Text = "Clip" });
        _clipCombo = new ComboView { Width = 120 };
        _clipCombo.SelectedIndexChanged += (_, _) => OnClipSelected();
        clipRow.Widgets.Add(_clipCombo);
        _animSection.Widgets.Add(clipRow);

        // Transport controls
        var transportRow = new HorizontalStackPanel { Spacing = 2 };
        transportRow.Widgets.Add(MakeButton("Play", () =>
        {
            if (_selectedIndex < 0) return;
            var clipIdx = _clipCombo.SelectedIndex ?? 0;
            _dispatcher.Dispatch($"refanim {_selectedIndex} play {clipIdx}");
        }));
        transportRow.Widgets.Add(MakeButton("Pause", () =>
        {
            if (_selectedIndex >= 0)
                _dispatcher.Dispatch($"refanim {_selectedIndex} pause");
        }));
        transportRow.Widgets.Add(MakeButton("Stop", () =>
        {
            if (_selectedIndex >= 0)
                _dispatcher.Dispatch($"refanim {_selectedIndex} stop");
        }));
        _animSection.Widgets.Add(transportRow);

        // Timeline scrubber
        _timelineSlider = new HorizontalSlider
        {
            Minimum = 0,
            Maximum = 1,
            Value = 0,
            Width = 160,
        };
        _timelineSlider.ValueChangedByUser += (_, _) => OnTimelineScrubbed();
        _animSection.Widgets.Add(_timelineSlider);

        // Time display
        _timeLabel = new Label { Text = "0.00s / 0.00s" };
        _animSection.Widgets.Add(_timeLabel);

        // Speed control
        var speedRow = new HorizontalStackPanel { Spacing = 4 };
        speedRow.Widgets.Add(new Label { Text = "Speed" });
        _speedField = new TextBox { Text = "1.0", Width = 45 };
        _speedField.TextChanged += (_, _) => OnSpeedChanged();
        speedRow.Widgets.Add(_speedField);
        speedRow.Widgets.Add(MakeButton("0.5x", () => SetSpeed(0.5f)));
        speedRow.Widgets.Add(MakeButton("1x", () => SetSpeed(1f)));
        speedRow.Widgets.Add(MakeButton("2x", () => SetSpeed(2f)));
        _animSection.Widgets.Add(speedRow);

        // Frame step
        var stepRow = new HorizontalStackPanel { Spacing = 2 };
        stepRow.Widgets.Add(MakeButton("<< Frame", () => StepFrame(-1)));
        stepRow.Widgets.Add(MakeButton("Frame >>", () => StepFrame(1)));
        _animSection.Widgets.Add(stepRow);

        _propertiesSection.Widgets.Add(_animSection);

        root.Widgets.Add(_propertiesSection);

        Root = root;

        // Wire up position/rotation field commits
        WireTransformField(_posX, () => CommitTransform());
        WireTransformField(_posY, () => CommitTransform());
        WireTransformField(_posZ, () => CommitTransform());
        WireTransformField(_rotX, () => CommitTransform());
        WireTransformField(_rotY, () => CommitTransform());
        WireTransformField(_rotZ, () => CommitTransform());

        // Listen for referenceModelState changes
        _referenceModelState.Changed += Refresh;
    }

    public void SetDesktop(Desktop desktop) => _desktop = desktop;

    public void Refresh()
    {
        _modelList.Widgets.Clear();

        for (int i = 0; i < _referenceModelState.Models.Count; i++)
        {
            var model = _referenceModelState.Models[i];
            var name = Path.GetFileName(model.FilePath);
            var idx = i;

            var btn = new Button
            {
                Content = new Label { Text = $"[{i}] {name}" },
                Width = 160,
            };

            if (i == _selectedIndex)
                btn.Background = new SolidBrush(new Color(80, 120, 200));

            btn.Click += (_, _) => SelectModel(idx);
            _modelList.Widgets.Add(btn);
        }

        if (_selectedIndex >= 0 && _selectedIndex < _referenceModelState.Models.Count)
        {
            _propertiesSection.Visible = true;
            LoadPropertiesFromModel(_referenceModelState.Models[_selectedIndex]);
        }
        else
        {
            _propertiesSection.Visible = false;
            _selectedIndex = -1;
        }
    }

    private void SelectModel(int index)
    {
        _selectedIndex = index;
        Refresh();
    }

    private void LoadPropertiesFromModel(ReferenceModelData model)
    {
        _updatingFields = true;

        _posX.Text = model.PositionX.ToString("F1");
        _posY.Text = model.PositionY.ToString("F1");
        _posZ.Text = model.PositionZ.ToString("F1");
        _rotX.Text = model.RotationX.ToString("F1");
        _rotY.Text = model.RotationY.ToString("F1");
        _rotZ.Text = model.RotationZ.ToString("F1");
        _scaleField.Text = model.Scale.ToString("F2");

        _renderModeCombo.SelectedIndex = model.RenderMode switch
        {
            ReferenceRenderMode.Wireframe => 1,
            ReferenceRenderMode.Transparent => 2,
            _ => 0,
        };

        _visibilityCheck.IsChecked = model.IsVisible;

        // Animation section
        if (model.HasAnimations)
        {
            _animSection.Visible = true;

            // Populate clip dropdown
            _clipCombo.Widgets.Clear();
            foreach (var clip in model.AnimationClips!)
                _clipCombo.Widgets.Add(new Label { Text = clip.Name });

            _clipCombo.SelectedIndex = model.ActiveClipIndex ?? 0;

            // Update slider range from active clip
            var activeClip = model.AnimationClips[model.ActiveClipIndex ?? 0];
            _timelineSlider.Maximum = Math.Max(activeClip.Duration, 0.01f);
            _timelineSlider.Value = model.AnimationTime;
            _timeLabel.Text = $"{model.AnimationTime:F2}s / {activeClip.Duration:F2}s";

            _speedField.Text = model.AnimationSpeed.ToString("F1");
        }
        else
        {
            _animSection.Visible = false;
        }

        _updatingFields = false;
    }

    private void CommitTransform()
    {
        if (_updatingFields || _selectedIndex < 0) return;

        if (float.TryParse(_posX.Text, out float px) &&
            float.TryParse(_posY.Text, out float py) &&
            float.TryParse(_posZ.Text, out float pz) &&
            float.TryParse(_rotX.Text, out float rx) &&
            float.TryParse(_rotY.Text, out float ry) &&
            float.TryParse(_rotZ.Text, out float rz))
        {
            var scale = _referenceModelState.Get(_selectedIndex)?.Scale ?? 1f;
            _dispatcher.Dispatch($"reftransform {_selectedIndex} {px} {py} {pz} {rx} {ry} {rz} {scale}");
        }
    }

    private void OnScaleChanged()
    {
        if (_updatingFields || _selectedIndex < 0) return;
        if (float.TryParse(_scaleField.Text, out float s))
            _dispatcher.Dispatch($"refscale {_selectedIndex} {s}");
    }

    private void SetScale(float value)
    {
        if (_selectedIndex < 0) return;
        _updatingFields = true;
        _scaleField.Text = value.ToString("F2");
        _updatingFields = false;
        _dispatcher.Dispatch($"refscale {_selectedIndex} {value}");
    }

    private void OnRenderModeChanged()
    {
        if (_updatingFields || _selectedIndex < 0) return;
        var mode = (_renderModeCombo.SelectedItem as Label)?.Tag as string ?? "solid";
        _dispatcher.Dispatch($"refmode {_selectedIndex} {mode}");
    }

    private void OnVisibilityChanged()
    {
        if (_updatingFields || _selectedIndex < 0) return;
        var cmd = _visibilityCheck.IsChecked ? "refshow" : "refhide";
        _dispatcher.Dispatch($"{cmd} {_selectedIndex}");
    }

    private void OnClipSelected()
    {
        if (_updatingFields || _selectedIndex < 0) return;
        var clipIdx = _clipCombo.SelectedIndex ?? 0;
        // If currently animating, switch to the new clip
        var model = _referenceModelState.Get(_selectedIndex);
        if (model?.IsAnimating == true)
            _dispatcher.Dispatch($"refanim {_selectedIndex} play {clipIdx}");
    }

    private void OnTimelineScrubbed()
    {
        if (_updatingFields || _selectedIndex < 0) return;
        _dispatcher.Dispatch($"refanim {_selectedIndex} frame {_timelineSlider.Value:F3}");
        var model = _referenceModelState.Get(_selectedIndex);
        if (model is not null)
        {
            var clip = model.AnimationClips?[model.ActiveClipIndex ?? 0];
            _timeLabel.Text = $"{_timelineSlider.Value:F2}s / {clip?.Duration ?? 0:F2}s";
        }
    }

    private void OnSpeedChanged()
    {
        if (_updatingFields || _selectedIndex < 0) return;
        if (float.TryParse(_speedField.Text, out float speed))
            _dispatcher.Dispatch($"refanim {_selectedIndex} speed {speed}");
    }

    private void SetSpeed(float value)
    {
        if (_selectedIndex < 0) return;
        _updatingFields = true;
        _speedField.Text = value.ToString("F1");
        _updatingFields = false;
        _dispatcher.Dispatch($"refanim {_selectedIndex} speed {value}");
    }

    private void StepFrame(int direction)
    {
        if (_selectedIndex < 0) return;
        var model = _referenceModelState.Get(_selectedIndex);
        if (model is null || !model.HasAnimations) return;

        var clip = model.AnimationClips![model.ActiveClipIndex ?? 0];
        float fps = clip.TicksPerSecond > 0 ? clip.TicksPerSecond : 25f;
        float frameTime = 1f / fps;
        float newTime = Math.Clamp(model.AnimationTime + direction * frameTime, 0, clip.Duration);
        _dispatcher.Dispatch($"refanim {_selectedIndex} frame {newTime:F3}");
    }

    /// <summary>
    /// Call from the game's update loop to keep the timeline slider and time label in sync
    /// while animation is playing.
    /// </summary>
    public void UpdateAnimationDisplay()
    {
        if (_selectedIndex < 0 || _updatingFields) return;
        var model = _referenceModelState.Get(_selectedIndex);
        if (model is null || !model.HasAnimations || !model.IsAnimating) return;

        _updatingFields = true;
        var clip = model.AnimationClips![model.ActiveClipIndex ?? 0];
        _timelineSlider.Maximum = Math.Max(clip.Duration, 0.01f);
        _timelineSlider.Value = model.AnimationTime;
        _timeLabel.Text = $"{model.AnimationTime:F2}s / {clip.Duration:F2}s";
        _updatingFields = false;
    }

    private void DispatchAndRefresh(string command)
    {
        _dispatcher.Dispatch(command);
        Refresh();
    }

    private void ShowLoadModelDialog()
    {
        if (_desktop is null) return;

        var dlg = new VoxelForgeFileDialog(FileDialogMode.OpenFile)
        {
            Filter = "*.fbx|*.obj|*.gltf|*.glb|*.dae|*.3ds|*.blend",
        };

        dlg.Closed += (_, _) =>
        {
            if (dlg.Result && !string.IsNullOrEmpty(dlg.FilePath))
            {
                _dispatcher.Dispatch("refload", dlg.FilePath);
                // Select the newly loaded model
                _selectedIndex = _referenceModelState.Models.Count - 1;
                Refresh();
            }
        };

        dlg.ShowModal(_desktop);
    }

    private static void WireTransformField(TextBox field, Action onCommit)
    {
        // Commit on focus lost (user tabs away or clicks elsewhere)
        field.TouchDown += (_, _) => field.SelectAll();
    }

    private static TextBox SmallField(string placeholder) =>
        new() { Width = 45, Text = "0" };

    private static Widget FieldWithLabel(string label, TextBox field)
    {
        var row = new HorizontalStackPanel { Spacing = 1 };
        row.Widgets.Add(new Label { Text = label, Width = 12 });
        row.Widgets.Add(field);
        return row;
    }

    private static Button MakeButton(string text, Action onClick)
    {
        var btn = new Button { Content = new Label { Text = text } };
        btn.Click += (_, _) => onClick();
        return btn;
    }
}
