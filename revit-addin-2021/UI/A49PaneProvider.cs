using System.Windows;
using Autodesk.Revit.UI;

namespace A49AIRevitAssistant.UI
{
    public class A49PaneProvider : IDockablePaneProvider
    {
        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElementCreator = new FrameworkElementCreator();
        }

        private class FrameworkElementCreator : IFrameworkElementCreator
        {
            public FrameworkElement CreateFrameworkElement()
            {
                var viewer = new DockablePaneViewer();
                DockablePaneViewer.Instance = viewer;   // Critical!
                return viewer;
            }
        }
    }
}
