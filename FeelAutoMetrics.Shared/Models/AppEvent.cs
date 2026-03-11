using System;
using System.Collections.Generic;

namespace FeelAutoMetrics.Shared.Models;

public class AppEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    // Identification de la source
    public string AppName { get; set; } = string.Empty; // ex: "SipLineWeb"
    public string Environment { get; set; } = "Production"; // Development, Staging, Production

    // Détails de l'événement
    public string Level { get; set; } = "Information"; // Info, Warning, Error, Critical
    public string Message { get; set; } = string.Empty;
    public string? Category { get; set; } // ex: "Payment", "Auth", "Inventory"
    
    // Corrélation (Optionnel mais recommandé)
    public string? CorrelationId { get; set; } // Pour lier au RequestId de Traefik si possible
    public string? UserId { get; set; }

    // Métadonnées flexibles (Stockées en JSON dans PostgreSQL)
    public Dictionary<string, string>? Metadata { get; set; }

    // Détails techniques (si Error)
    public string? ExceptionMessage { get; set; }
    public string? StackTrace { get; set; }
}
