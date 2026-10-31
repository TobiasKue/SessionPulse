namespace Loupedeck.SessionPulsePlugin;

using SessionPulse.Core;
using SessionPulse.Core.Approvals;
using SessionPulse.Core.Presentation;

/// <summary>One key inside a folder: a session (key model), a permission request, or an action such as "clear all".</summary>
internal sealed record FolderItem(string Parameter, KeyModel? Model, string? ActionLabel = null, TileIcon ActionIcon = TileIcon.Done, PendingApproval? Approval = null);

/// <summary>What the folder's own tile (the key that opens it) shows.</summary>
internal sealed record TileState(TileIcon Icon, int? Count, string Label, bool Active)
{
    public bool IsAnimated => this.Active && this.Icon is TileIcon.Working or TileIcon.Waiting or TileIcon.Microphone or TileIcon.Approvals or TileIcon.Calendar;
}

/// <summary>What changed during a tick, to be reported to the service outside the plugin lock.</summary>
internal sealed record FolderUpdate(bool NamesChanged, IReadOnlyList<string> ChangedItems, bool TileChanged);

/// <summary>
/// Base for SessionPulse folders: a tile with symbol and count, and a page with up to eight keys. The
/// "back" key is added by the service (ButtonArea navigation). After a key press the folder closes with a
/// delay and arms the ghost-press guard (docs/LESSONS.md).
/// </summary>
public abstract class SessionPulseFolder : PluginDynamicFolder
{
    private volatile IReadOnlyList<FolderItem> items = [];
    private volatile TileState tile;
    private volatile bool active;

    private protected SessionPulseFolder(string displayName, TileState initialTile)
    {
        this.DisplayName = displayName;
        this.GroupName = "SessionPulse";
        this.tile = initialTile;
    }

    internal SessionPulsePlugin Owner => (SessionPulsePlugin)this.Plugin;

    public override PluginDynamicFolderNavigation GetNavigationArea(DeviceType _) => PluginDynamicFolderNavigation.ButtonArea;

    public override bool Load()
    {
        this.Owner.RegisterFolder(this);
        return true;
    }

    public override bool Activate()
    {
        this.active = true;
        this.Owner.LogVerbose($"folder {this.GetType().Name} opened");
        this.Owner.RequestTick();
        return true;
    }

    public override bool Deactivate()
    {
        this.active = false;
        this.Owner.LogVerbose($"folder {this.GetType().Name} closed");
        return true;
    }

    public override IEnumerable<string> GetButtonPressActionNames(DeviceType deviceType) =>
        this.items.Select(i => this.CreateCommandName(i.Parameter)).ToList();

    public override string GetButtonDisplayName(PluginImageSize imageSize) => this.DisplayName;

    public override BitmapImage? GetButtonImage(PluginImageSize imageSize)
    {
        var t = this.tile;
        return this.Draw(-1, () => this.Owner.Renderer.RenderTile(t.Icon, t.Count, t.Label, t.Active, imageSize, this.Owner.Frame), imageSize);
    }

    public override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize)
    {
        var item = this.Find(actionParameter);
        return item?.Model?.Title ?? item?.ActionLabel ?? item?.Approval?.Tool ?? string.Empty;
    }

    public override BitmapImage? GetCommandImage(string actionParameter, PluginImageSize imageSize)
    {
        var item = this.Find(actionParameter);
        var position = item?.Model?.Number ?? 0;
        return this.Draw(position, () => item switch
        {
            { Model: { } model } => this.Owner.Renderer.Render(model, imageSize, this.Owner.Frame),
            { Approval: { } request } => this.Owner.Renderer.RenderApproval(request, this.Owner.Texts, imageSize),
            { ActionLabel: { } label } => this.Owner.Renderer.RenderTile(item.ActionIcon, null, label, true, imageSize, this.Owner.Frame),
            _ => this.Owner.Renderer.Render(KeyModel.Empty(0), imageSize, 0),
        }, imageSize);
    }

    public override void RunCommand(string actionParameter)
    {
        if (this.Owner.PressGuard.ShouldIgnore())
        {
            this.Owner.LogVerbose("ghost press ignored in folder");
            return;
        }

        var item = this.Find(actionParameter);
        if (item is null)
        {
            return;
        }

        this.Owner.Diagnostics.CountPress();
        this.OnItemPressed(item);
        this.CloseSoon();
    }

    /// <summary>Computes items and tile for this tick (called under the plugin lock).</summary>
    internal abstract (IReadOnlyList<FolderItem> Items, TileState Tile) Compute(Dashboard dashboard, Texts texts);

    /// <summary>Runs the action of a pressed key (not under the plugin lock).</summary>
    internal abstract void OnItemPressed(FolderItem item);

    internal FolderUpdate Prepare(Dashboard dashboard, Texts texts)
    {
        var (newItems, newTile) = this.Compute(dashboard, texts);
        var oldItems = this.items;
        var namesChanged = !oldItems.Select(i => i.Parameter).SequenceEqual(newItems.Select(i => i.Parameter));
        var changed = namesChanged
            ? []
            : newItems.Where((item, i) => item != oldItems[i] || item.Model?.IsAnimated == true).Select(i => i.Parameter).ToList();
        var tileChanged = newTile != this.tile || newTile.IsAnimated;
        this.items = newItems;
        this.tile = newTile;
        return new FolderUpdate(namesChanged, changed, tileChanged);
    }

    internal void Notify(FolderUpdate update)
    {
        if (this.active)
        {
            if (update.NamesChanged)
            {
                this.ButtonActionNamesChanged();
            }
            else
            {
                foreach (var parameter in update.ChangedItems)
                {
                    this.CommandImageChanged(parameter);
                }
            }
        }

        if (update.TileChanged)
        {
            // Redraws the key that opens the folder (docs/LESSONS.md).
            this.Plugin.OnActionImageChanged("#DynamicFolder", "DynamicFolder#" + this.GetType().FullName, false);
        }
    }

    private FolderItem? Find(string actionParameter) => this.items.FirstOrDefault(i => i.Parameter == actionParameter);

    private void CloseSoon()
    {
        _ = Task.Delay(PressGuard.CloseDelay).ContinueWith(_ =>
        {
            // Bringing Claude to the front may already have closed the folder (active application changed).
            if (this.active)
            {
                this.Close();
                this.Owner.PressGuard.FolderClosed();
                this.Owner.LogVerbose($"folder {this.GetType().Name} closed after press");
            }
        }, TaskScheduler.Default);
    }

    private BitmapImage? Draw(int position, Func<BitmapImage> draw, PluginImageSize imageSize)
    {
        this.Owner.Diagnostics.CountImageRequest();
        try
        {
            return draw();
        }
        catch (Exception ex)
        {
            this.Owner.Diagnostics.LogDrawError($"{this.GetType().Name}:{position}", ex);
            return this.Owner.Renderer.RenderError(imageSize);
        }
    }
}
