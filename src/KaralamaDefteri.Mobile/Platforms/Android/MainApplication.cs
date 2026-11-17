using Android.App;
using Android.Runtime;

namespace KaralamaDefteri.Mobile;

[Application]
public sealed class MainApplication(nint handle, JniHandleOwnership ownership)
    : MauiApplication(handle, ownership)
{
    protected override MauiApp CreateMauiApp() => MauiApp.CreateBuilder().UseMauiApp<MobileApp>().Build();
}
