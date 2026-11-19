#include "pch.h"

#include "App.xaml.h"
#include "Deployment.h"
#include "MainWindow.xaml.h"

using namespace winrt;
using namespace winrt::Microsoft::UI::Xaml;

namespace winrt::SetupUI::implementation
{
    App::App()
    {
        InitializeComponent();

#if defined _DEBUG && !defined DISABLE_XAML_GENERATED_BREAK_ON_UNHANDLED_EXCEPTION
        UnhandledException([](IInspectable const&, UnhandledExceptionEventArgs const& e)
        {
            if (IsDebuggerPresent())
            {
                auto message = e.Message();
                __debugbreak();
            }
        });
#endif
    }

    void App::OnLaunched(LaunchActivatedEventArgs const&)
    {
        m_window = make<MainWindow>();
        m_window.Activate();
    }
}

// the generated main is replaced so that /quiet can run without creating any window
int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    if (setup::HasSwitch(L"quiet") || setup::HasSwitch(L"q") || setup::HasSwitch(L"silent"))
    {
        init_apartment(apartment_type::multi_threaded);
        return setup::RunQuiet(setup::HasSwitch(L"uninstall"));
    }

    init_apartment(apartment_type::single_threaded);
    Application::Start([](auto&&)
    {
        make<SetupUI::implementation::App>();
    });
    return setup::ExitCode();
}
