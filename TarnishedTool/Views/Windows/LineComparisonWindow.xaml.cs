//

using System.Windows;

namespace TarnishedTool.Views.Windows;

public partial class LineComparisonWindow : TopmostWindow
{
    public LineComparisonWindow()
    {
        InitializeComponent();

        if (Application.Current.MainWindow != null)
            Application.Current.MainWindow.Closing += (_, _) => Close();
    }
}
