using Microsoft.Extensions.DependencyInjection;

namespace EntglDb.Test.Maui;

public partial class App : Application
{
	private readonly IServiceProvider _services;

	public App(IServiceProvider services)
	{
		InitializeComponent();
		_services = services;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		// Resolve AppShell from DI to enable Injection in its children (like MainPage if registered)
		// Or if AppShell itself needs dependencies.
		// However, typically in MAUI Shell, we set MainPage = new AppShell(); in Constructor.
		// But CreateWindow is the new way.
		
		var shell = _services.GetRequiredService<AppShell>();
		return new Window(shell);
	}
}