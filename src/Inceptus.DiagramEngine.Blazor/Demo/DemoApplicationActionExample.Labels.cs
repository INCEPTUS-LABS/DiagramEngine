namespace Inceptus.DiagramEngine.Blazor.Demo;

public sealed partial class DemoApplicationActionExample
{
    public DemoApplicationLabels Labels { get; private set; }

    public void SetLabels(DemoApplicationLabels labels)
    {
        if (!VerificationEnabled || !Enum.IsDefined(labels) || Labels == labels) return;
        Labels = labels;
        AdvanceRevision();
    }

    // Application-owned samples, independent of the modeler's UI culture and catalogue policy.
    private ActionLabels GetLabels(string? catalogLabel) => Labels switch
    {
        DemoApplicationLabels.Expanded => new(
            "Inspect the complete application details for the currently selected diagram representation",
            "Open the linked application record associated with this exact diagram representation",
            "Inspection is unavailable because the application's current policy disables this action for the selected representation.",
            "No external application is connected to this demonstration, so the linked record cannot be opened for this representation.",
            "Show the complete semantic owner identity for this representation",
            "Show the current scope and view identity for this representation",
            "Show the accepted source document identity for this representation",
            "Show the native visual representation identity for this owner"),
        DemoApplicationLabels.German => new("Details anzeigen", "Verknüpften Datensatz öffnen",
            "Die Prüfung ist durch die aktuelle Anwendungsrichtlinie deaktiviert.", "In diesem Beispiel ist keine externe Anwendung verbunden.",
            "Eigentümerkennung anzeigen", "Bereichskennung anzeigen", "Quellkennung anzeigen", "Darstellungskennung anzeigen"),
        DemoApplicationLabels.Spanish => new("Inspeccionar detalles", "Abrir registro vinculado",
            "La inspección está deshabilitada por la política actual de la aplicación.", "No hay ninguna aplicación externa conectada en este ejemplo.",
            "Mostrar identidad del propietario", "Mostrar identidad del ámbito", "Mostrar identidad del origen", "Mostrar identidad visual"),
        DemoApplicationLabels.French => new("Examiner les détails", "Ouvrir l’enregistrement lié",
            "L’inspection est désactivée par la politique actuelle de l’application.", "Aucune application externe n’est connectée dans cet exemple.",
            "Afficher l’identité du propriétaire", "Afficher l’identité de la portée", "Afficher l’identité de la source", "Afficher l’identité visuelle"),
        DemoApplicationLabels.Polish => new("Sprawdź szczegóły", "Otwórz powiązany rekord",
            "Sprawdzanie jest wyłączone przez bieżącą politykę aplikacji.", "W tym przykładzie nie podłączono zewnętrznej aplikacji.",
            "Pokaż tożsamość właściciela", "Pokaż tożsamość zakresu", "Pokaż tożsamość źródła", "Pokaż tożsamość wizualną"),
        _ => new(catalogLabel ?? "Inspect details", "Open linked record",
            "Inspection is disabled by the application's current policy.", "No external application is connected in this example.",
            "Show owner identity", "Show scope identity", "Show source identity", "Show visual identity"),
    };

    private sealed record ActionLabels(string Inspect, string LinkedRecord, string Unavailable, string NotConnected,
        string Owner, string Scope, string Source, string Visual);
}

public enum DemoApplicationLabels { Default, Expanded, German, Spanish, French, Polish }
