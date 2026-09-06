using Circle_Tracker.Storage;
using System;
using System.IO;

namespace Circle_Tracker.Services;

internal static class TrackerInitializer
{
    public static (IPlaySink PlaySink, CompositePlaySink? CompositeSink, LocalSqlitePlaySink? LocalSink, SessionManager SessionManager, IDatabaseManager DbManager, GoogleSheetsManager SheetsManager) 
        InitializeProductionServices(IMainWindow form, ISettingsService settings)
    {
        var sheetsManager = new GoogleSheetsManager(form, settings.GetFunctionSeparator);
        
        var dbManager = new SqliteDatabaseManager(settings.LocalDatabasePath);
        var localSqliteSink = new LocalSqlitePlaySink(dbManager);
        var sessionManager = new SessionManager(dbManager);

        var compositeSink = new CompositePlaySink();
        compositeSink.AddSink(localSqliteSink, () => settings.EnableLocalLogging);
        compositeSink.AddSink(sheetsManager, () => settings.EnableGoogleSheetsLogging);

        return (compositeSink, compositeSink, localSqliteSink, sessionManager, dbManager, sheetsManager);
    }

    public static (IPlaySink PlaySink, SessionManager SessionManager, IDatabaseManager DbManager) 
        InitializeTestServices(ISheetsSink sheetsSink)
    {
        IPlaySink playSink;
        if (sheetsSink is IPlaySink ps)
        {
            playSink = ps;
        }
        else
        {
            playSink = new SheetsSinkAdapter(sheetsSink);
        }

        var dbManager = new SqliteDatabaseManager(":memory:");
        var sessionManager = new SessionManager(dbManager);

        return (playSink, sessionManager, dbManager);
    }

    public static void ShowWelcomeMessageIfFirstRun(IMainWindow form, string settingsFilePath)
    {
        if (!File.Exists(settingsFilePath) && !File.Exists(Path.Combine(AppContext.BaseDirectory, "user_settings.txt")))
        {
            string welcomeMsg = "Welcome to circle tracker!\n\n" +
                "This app connects to 'tosu' running alongside osu!.\n\n" +
                "Works with both osu!stable (Wine) and osu!lazer.";
            form.ShowMessage(welcomeMsg, "Welcome to Circle Tracker!");
        }
    }
}
