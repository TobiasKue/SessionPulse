namespace Loupedeck.SessionPulsePlugin;

/// <summary>
/// Jumps to the session that most needs you: the longest waiting one, then a possibly stuck one, then the
/// most recently finished one. The key shows how many sessions are waiting.
/// </summary>
public sealed class NextWaitingCommand : PluginDynamicCommand
{
    public NextWaitingCommand()
        : base(displayName: "Next waiting session", description: "Opens the session that waits longest for you", groupName: "SessionPulse")
    {
    }

    private SessionPulsePlugin Owner => (SessionPulsePlugin)this.Plugin;

    internal void Redraw() => this.ActionImageChanged();

    protected override bool OnLoad()
    {
        this.Owner.RegisterNextWaiting(this);
        return true;
    }

    protected override void RunCommand(string actionParameter) => this.Owner.OpenNextWaiting();

    protected override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize) => "Next waiting session";

    protected override BitmapImage? GetCommandImage(string actionParameter, PluginImageSize imageSize)
    {
        var owner = this.Owner;
        owner.Diagnostics.CountImageRequest();
        var waiting = owner.WaitingCount;
        try
        {
            return owner.Renderer.RenderTile(TileIcon.Next, waiting, waiting > 0 ? owner.Texts.NextWaiting : owner.Texts.NothingWaiting, waiting > 0, imageSize, owner.Frame);
        }
        catch (Exception ex)
        {
            owner.Diagnostics.LogDrawError("next waiting", ex);
            return owner.Renderer.RenderError(imageSize);
        }
    }
}
