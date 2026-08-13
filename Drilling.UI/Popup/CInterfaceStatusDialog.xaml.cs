using System.Windows;
using System.Windows.Media;
using Drilling.Common.Interface;
using Drilling.UI.Menu.Menus;

namespace Drilling.UI.Popup;

public partial class CInterfaceStatusDialog : Window
{
    public CInterfaceStatusDialog(
        string title,
        IReadOnlyList<ST_INTERFACE_COMM_STATUS> statuses)
    {
        InitializeComponent();

        var rows = statuses
            .OrderBy(status => status.Number)
            .ThenBy(status => status.NickName, StringComparer.OrdinalIgnoreCase)
            .Select(status => new CInterfaceStatusRow(status))
            .ToArray();

        Title = $"{title} Status";
        TitleText.Text = title;
        SummaryText.Text = $"{rows.Length} interface item(s)";
        StatusGrid.ItemsSource = rows;
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    public sealed class CInterfaceStatusRow
    {
        public CInterfaceStatusRow(ST_INTERFACE_COMM_STATUS status)
        {
            No = status.Number + 1;
            NickName = string.IsNullOrWhiteSpace(status.NickName) ? "-" : status.NickName;
            InterfaceType = status.InterfaceType.ToString();
            State = ToStateText(status.ConnectionState);
            Endpoint = string.IsNullOrWhiteSpace(status.Endpoint) ? "-" : status.Endpoint;
            LastChangedText = status.LastChangedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
            StateBrush = CStatusBrush.ForHeaderState(State);
        }

        public int No { get; }

        public string NickName { get; }

        public string InterfaceType { get; }

        public string State { get; }

        public string Endpoint { get; }

        public string LastChangedText { get; }

        public Brush StateBrush { get; }

        private static string ToStateText(EN_COMM_STATE state)
        {
            return state switch
            {
                EN_COMM_STATE.Online => "ONLINE",
                EN_COMM_STATE.Offline => "OFFLINE",
                _ => "SIMULATION"
            };
        }
    }
}
