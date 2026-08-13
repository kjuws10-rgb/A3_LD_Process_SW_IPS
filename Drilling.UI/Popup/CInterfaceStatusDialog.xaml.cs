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
        int GetStatusSortKey1(ST_INTERFACE_COMM_STATUS status)
        {
            return status.Number;
        }

        string GetStatusSortKey2(ST_INTERFACE_COMM_STATUS status)
        {
            return status.NickName;
        }

        CInterfaceStatusRow SelectStatus3(ST_INTERFACE_COMM_STATUS status)
        {
            return new CInterfaceStatusRow(status);
        }

        var rows = statuses
            .OrderBy(GetStatusSortKey1)
            .ThenBy(GetStatusSortKey2, StringComparer.OrdinalIgnoreCase)
            .Select(SelectStatus3)
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
            string EvaluateStateSwitch1()
            {
                var switchValue = state;
                switch (switchValue)
                {
                    case EN_COMM_STATE.Online:
                        return "ONLINE";
                    case EN_COMM_STATE.Offline:
                        return "OFFLINE";
                    default:
                        return "SIMULATION";
                }
            }

            return EvaluateStateSwitch1();
        }
    }
}
