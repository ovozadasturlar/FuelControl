using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FuelControl.Desktop.Services;

namespace FuelControl.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // §8.18: klaviatura/sichqoncha harakati — foydalanuvchi faol (token fonda uzaytiriladi)
        AddHandler(KeyDownEvent, (_, _) => Malumot.Api.FaollikBelgila(), Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, (_, _) => Malumot.Api.FaollikBelgila(), Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, (_, _) => Malumot.Api.FaollikBelgila(), Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        // Til almashganda matnlar uzunligi o'zgaradi; ba'zi tugmalar (WrapPanel ichidagi chiplar) eski o'lchamda qolib,
        // matn kesilib qolardi. Yangi matnlar qo'llangach butun daraxt qayta o'lchanadi (til kam almashadi — arzon).
        Til.Ozgardi += () => Dispatcher.UIThread.Post(() =>
        {
            foreach (var l in this.GetVisualDescendants().OfType<Layoutable>()) l.InvalidateMeasure();
        }, DispatcherPriority.Background);
    }
}
