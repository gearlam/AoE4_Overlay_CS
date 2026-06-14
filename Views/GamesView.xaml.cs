using System.Diagnostics;
using System.Windows.Controls;

namespace AoE4OverlayCS.Views
{
    public partial class GamesView : System.Windows.Controls.UserControl
    {
        public GamesView()
        {
            InitializeComponent();
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Uri?.ToString()))
            {
                Process.Start(new ProcessStartInfo(e.Uri.ToString()) { UseShellExecute = true });
                e.Handled = true;
            }
        }
    }
}
