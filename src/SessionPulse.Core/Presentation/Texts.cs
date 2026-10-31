using System.Globalization;
using SessionPulse.Core.Status;

namespace SessionPulse.Core.Presentation;

/// <summary>Key texts in English and German, chosen by the system UI language.</summary>
public sealed class Texts
{
    public static readonly Texts English = new(german: false);
    public static readonly Texts German = new(german: true);

    private readonly bool german;

    private Texts(bool german) => this.german = german;

    public static Texts ForCulture(CultureInfo culture) =>
        culture.TwoLetterISOLanguageName == "de" ? German : English;

    public static Texts Current => ForCulture(CultureInfo.CurrentUICulture);

    public string Code => "Claude Code";

    public string Cowork => "Cowork";

    public string Stuck => this.german ? "Hängt?" : "Stuck?";

    public string AcknowledgeAll => this.german ? "Alle quittieren" : "Clear all";

    public string NextWaiting => this.german ? "Nächste" : "Next";

    public string NothingWaiting => this.german ? "Keine wartet" : "None waiting";

    public string Approvals => this.german ? "Freigaben" : "Approvals";

    public string AllowOnce => this.german ? "Erlauben" : "Allow";

    public string AllowSession => this.german ? "Für Session" : "For session";

    public string AllowAlways => this.german ? "Immer" : "Always";

    public string Deny => this.german ? "Ablehnen" : "Deny";

    public string DecideInClaude => this.german ? "In Claude" : "In Claude";

    public string Risky => this.german ? "Riskant!" : "Risky!";

    public string Usage => this.german ? "Nutzung" : "Usage";

    public string FiveHours => "5h";

    public string Week => this.german ? "Wo" : "Wk";

    public string ResetsIn(string time) => (this.german ? "Reset in " : "Resets in ") + time;

    public string NoData => this.german ? "Keine Daten" : "No data";

    public string Scheduled => this.german ? "Geplant" : "Scheduled";

    public string Dictation => this.german ? "Diktat" : "Dictate";

    public string DictationActive => this.german ? "Diktat läuft" : "Dictating";

    public string NotConfigured => this.german ? "Nicht eingerichtet" : "Not set up";

    public string Group(SessionGroup group) => group switch
    {
        SessionGroup.Working => this.german ? "Läuft" : "Working",
        SessionGroup.Waiting => this.german ? "Wartet" : "Waiting",
        _ => this.german ? "Fertig" : "Done",
    };

    /// <summary>Short detail text; one word where possible, as it has to fit on one key line.</summary>
    public string Detail(StatusDetail detail, string? tool) => detail switch
    {
        StatusDetail.Tool => tool ?? (this.german ? "Werkzeug" : "Tool"),
        StatusDetail.Thinking => this.german ? "Denkt" : "Thinking",
        StatusDetail.Question => this.german ? "Frage" : "Question",
        StatusDetail.Plan => this.german ? "Plan" : "Plan",
        StatusDetail.Permission => this.german ? "Freigabe?" : "Approve?",
        StatusDetail.Input => this.german ? "Eingabe?" : "Input?",
        StatusDetail.Subagent => "Subagent",
        StatusDetail.Interrupted => this.german ? "Gestoppt" : "Stopped",
        StatusDetail.Done or StatusDetail.Ended => this.german ? "Fertig" : "Done",
        StatusDetail.Error => this.german ? "Fehler" : "Error",
        StatusDetail.Idle => this.german ? "Bereit" : "Ready",
        StatusDetail.Compacting => this.german ? "Verdichtet" : "Compacting",
        StatusDetail.Limit => "Limit",
        _ => this.german ? "Aktiv" : "Active",
    };
}
