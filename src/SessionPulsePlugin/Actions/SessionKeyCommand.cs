namespace Loupedeck.SessionPulsePlugin;

/// <summary>
/// One session key. There is one subclass per key instead of one command with parameters, because
/// parameterized actions don't work reliably in Options+ (docs/LESSONS.md).
/// </summary>
public abstract class SessionKeyCommand : PluginDynamicCommand
{
    protected SessionKeyCommand(int number)
        : base(displayName: $"Session {number}", description: $"Shows the status of a Claude session and opens it (key {number})", groupName: "Sessions")
    {
        this.Number = number;
    }

    public int Number { get; }

    private SessionPulsePlugin Owner => (SessionPulsePlugin)this.Plugin;

    internal void Redraw() => this.ActionImageChanged();

    protected override bool OnLoad()
    {
        this.Owner.Register(this);
        return true;
    }

    protected override void RunCommand(string actionParameter) => this.Owner.OnSessionKeyPressed(this.Number);

    protected override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize) => $"Session {this.Number}";

    protected override BitmapImage? GetCommandImage(string actionParameter, PluginImageSize imageSize)
    {
        var owner = this.Owner;
        owner.Diagnostics.CountImageRequest();
        var model = owner.GetKey(this.Number);
        try
        {
            return owner.Renderer.Render(model, imageSize, owner.Frame);
        }
        catch (Exception ex)
        {
            owner.Diagnostics.LogDrawError(this.Number, ex);
            return owner.Renderer.RenderError(imageSize);
        }
    }
}

public sealed class Session1Command : SessionKeyCommand { public Session1Command() : base(1) { } }

public sealed class Session2Command : SessionKeyCommand { public Session2Command() : base(2) { } }

public sealed class Session3Command : SessionKeyCommand { public Session3Command() : base(3) { } }

public sealed class Session4Command : SessionKeyCommand { public Session4Command() : base(4) { } }

public sealed class Session5Command : SessionKeyCommand { public Session5Command() : base(5) { } }

public sealed class Session6Command : SessionKeyCommand { public Session6Command() : base(6) { } }

public sealed class Session7Command : SessionKeyCommand { public Session7Command() : base(7) { } }

public sealed class Session8Command : SessionKeyCommand { public Session8Command() : base(8) { } }
