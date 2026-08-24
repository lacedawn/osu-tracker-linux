using System.Threading.Tasks;

namespace Circle_Tracker
{
    public interface IMainWindow
    {
        void SetCredentialsFound(bool found);
        void SetSheetsApiReady(bool val);
        void UpdateTime();
        void StopUpdateTimer();
        void ShowMessage(string message, string title = "Info");
        Task<bool> ShowYesNoDialog(string message, string title = "Confirm");
    }
}
