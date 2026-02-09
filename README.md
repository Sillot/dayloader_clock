# Dayloader Clock

Application Windows inspirée du concept de **Dayloader Clock** par Matty Benndetto.

Une représentation visuelle du temps de travail écoulé dans la journée, sous forme d'une grille de pixels colorés qui se remplissent progressivement.

## Fonctionnalités

- **Grille 10×10** : 100 pixels représentant chacun 1% de la journée de travail (8h par défaut)
- **Gradient coloré** : Bleu → Cyan → Vert pour le temps écoulé
- **Pause déjeuner** : Configurable (horaire + durée), automatiquement exclue du temps de travail
- **Heures supplémentaires** : Notification + compteur quand les 8h sont dépassées
- **System tray** : Icône avec mini-grille, double-clic pour afficher/masquer
- **Always-on-top** : Fenêtre flottante toujours visible
- **Auto-reset** : Se réinitialise automatiquement au changement de jour
- **Démarrage auto** : Se lance au démarrage de Windows (configurable)
- **Persistance** : Paramètres et historique sauvegardés dans `%APPDATA%/DayloaderClock/`

## Prérequis

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (ou supérieur)
- Windows 10/11

## Compilation & lancement

```bash
# Build
dotnet build

# Run
dotnet run

# Publish (exécutable autonome)
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

L'exécutable publié sera dans `bin/Release/net8.0-windows/win-x64/publish/`.

## Configuration

Cliquez sur **⚙ Paramètres** dans l'application pour configurer :

| Paramètre | Défaut | Description |
|---|---|---|
| Durée journée | 8h | Heures de travail par jour |
| Début pause | 12:00 | Heure de début du déjeuner |
| Durée pause | 60 min | Durée de la pause déjeuner |
| Démarrage auto | Oui | Lancer avec Windows |

## Données

Les fichiers sont stockés dans `%APPDATA%/DayloaderClock/` :

- `settings.json` — Paramètres de l'application
- `sessions.json` — Session en cours + historique des journées
