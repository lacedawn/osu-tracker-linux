using System.Threading.Tasks;

namespace Circle_Tracker.Services;

public interface IDialogService
{
    Task ShowMessageAsync(string message, string title = "Info");
    Task<bool> ShowYesNoDialogAsync(string message, string title = "Confirm");
    Task<string?> RequestSaveFilePathAsync(string defaultFileName = "export.csv");
}
