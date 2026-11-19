#pragma once

#include "MainWindow.g.h"
#include "Deployment.h"

namespace winrt::SetupUI::implementation
{
    struct MainWindow : MainWindowT<MainWindow>
    {
        MainWindow();

        void PrimaryButton_Click(Windows::Foundation::IInspectable const&, Microsoft::UI::Xaml::RoutedEventArgs const&);
        void SecondaryButton_Click(Windows::Foundation::IInspectable const&, Microsoft::UI::Xaml::RoutedEventArgs const&);
        void CloseButton_Click(Windows::Foundation::IInspectable const&, Microsoft::UI::Xaml::RoutedEventArgs const&);
        void LogButton_Click(Windows::Foundation::IInspectable const&, Microsoft::UI::Xaml::RoutedEventArgs const&);

    private:
        enum class Page
        {
            Welcome,
            Working,
            Done,
            Error,
        };

        void SetupWindow();

        fire_and_forget LoadAsync();
        fire_and_forget InstallAsync();
        fire_and_forget UninstallAsync(bool confirm);

        void ShowWelcome();
        void ShowWorking(wchar_t const* heading, wchar_t const* status);
        void ShowStatus(wchar_t const* status);
        void ShowProgress(uint32_t percentage);
        void ShowDone(bool uninstalled);
        void ShowError(wchar_t const* heading, std::wstring const& message, long code, bool canRetry);
        void SetButton(Microsoft::UI::Xaml::Controls::Button const& button, wchar_t const* text);

        Microsoft::UI::Dispatching::DispatcherQueue m_queue{ nullptr };
        setup::Payload m_payload;
        setup::InstalledState m_installed;
        Page m_page = Page::Working;
        bool m_busy = false;
        bool m_uninstalled = false;
        wchar_t const* m_status = L"";
    };
}

namespace winrt::SetupUI::factory_implementation
{
    struct MainWindow : MainWindowT<MainWindow, implementation::MainWindow>
    {
    };
}
